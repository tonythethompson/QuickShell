using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Commands;
using QuickShell.Models;
using QuickShell.Services;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace QuickShell.Pages;

internal sealed partial class WorkspaceStatusPage : ContentPage
{
    private readonly TerminalShortcut _shortcut;
    private readonly QuickShellSettingsManager _settings;
    private readonly IQuickShellServices _services;
    private readonly Action _onChanged;
    private WorkspaceStatusForm? _form;

    public WorkspaceStatusPage(
        IQuickShellServices services,
        TerminalShortcut shortcut,
        Action onChanged)
    {
        _shortcut = shortcut;
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _settings = services.Settings;
        _onChanged = onChanged;
        Id = CommandDescriptor.WorkspaceStatus(shortcut.Id).Id;
        Name = "Workspace status";
        Title = shortcut.Name;
        Icon = new IconInfo("\ue799");
        Commands = [];
    }

    public override IContent[] GetContent()
    {
        // Deliberately does not drain IExtensionCallbackQueue: that queue is process-wide, so
        // draining it here would run other pages' callbacks (including a full home-list
        // rebuild) on this page's fetch thread. The background capture hands off through a
        // field this form owns instead.
        _form?.ApplyPendingSnapshot();
        return [_form ??= new WorkspaceStatusForm(
            _shortcut,
            _settings,
            _services,
            () => _form = null,
            NotifyContentChanged)];
    }

    private void NotifyContentChanged()
    {
        try
        {
            RaiseItemsChanged();
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // Host may reject a notification while tearing the page down. The queued
            // DataJson update still applies on the next GetContent.
        }
    }
}

internal sealed partial class WorkspaceStatusForm : FormContent
{
    private readonly TerminalShortcut _shortcut;
    private readonly QuickShellSettingsManager _settings;
    private readonly IQuickShellServices _services;
    private readonly Action _releaseForm;
    private readonly Action _notifyContentChanged;
    /// <summary>Handoff from the background capture to this page's fetch thread.</summary>
    private PendingSnapshot? _pendingSnapshot;
    private readonly record struct PendingSnapshot(WorkspaceStatusSnapshot Snapshot, int Generation);
    /// <summary>Monotonic generation so an older capture cannot overwrite a newer refresh.</summary>
    private int _refreshGeneration;
    /// <summary>Serializes generation bump, pending handoff, and apply so races cannot drop the newest snapshot.</summary>
    private readonly object _refreshGate = new();

    /// <summary>
    /// Applies a completed background capture. Called from <c>GetContent</c> so the host
    /// thread owns the <see cref="FormContent.DataJson"/> write.
    /// </summary>
    internal void ApplyPendingSnapshot()
    {
        PendingSnapshot? toPublish = null;
        lock (_refreshGate)
        {
            var pending = _pendingSnapshot;
            _pendingSnapshot = null;
            if (pending is { } ready && ready.Generation == _refreshGeneration)
            {
                toPublish = ready;
            }
        }

        if (toPublish is { } snapshot)
        {
            PublishSnapshot(snapshot.Snapshot);
        }
    }

    public WorkspaceStatusForm(
        TerminalShortcut shortcut,
        QuickShellSettingsManager settings,
        IQuickShellServices services,
        Action releaseForm,
        Action notifyContentChanged)
    {
        _shortcut = shortcut;
        _settings = settings;
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _releaseForm = releaseForm;
        _notifyContentChanged = notifyContentChanged;
        TemplateJson = BuildTemplate();

        // Capture runs git status plus health probes — ~0.5s on a real repository. Publish
        // whatever is already cached (or a "checking" placeholder) so navigating into this
        // page is instant, then fill in the real values from a background capture.
        PublishSnapshot(TryGetCachedSnapshot());
        ScheduleRefresh();
    }

    public override CommandResult SubmitForm(string inputs, string data) =>
        HandleAction(TryGetAction(data) ?? TryGetAction(inputs));

    public override CommandResult SubmitForm(string payload) =>
        HandleAction(TryGetAction(payload));

    private CommandResult HandleAction(string? action)
    {
        switch (action)
        {
            case "refresh":
                PublishSnapshot(null);
                ScheduleRefresh();
                return CommandResult.KeepOpen();
            case "copyDiagnostics":
                LaunchDiagnosticsState.TryCopyLastReport(out var message);
                return QuickShellNavigation.StayOpen(message);
            case "copySupportBundle":
                SupportDiagnostics.Default.TryCopyBundle(LaunchDiagnosticsState.LastReport, out var supportMessage);
                return QuickShellNavigation.StayOpen(supportMessage);
            case "openSupportLogs":
                return SupportDiagnostics.Default.TryOpenLogFolder(out var error)
                    ? QuickShellNavigation.StayOpen(Strings.Diagnostics_LogFolderOpened)
                    : QuickShellNavigation.StayOpen(error);
            case "close":
                _releaseForm();
                return QuickShellNavigation.GoBack();
            default:
                return CommandResult.KeepOpen();
        }
    }

    private WorkspaceStatusSnapshot? TryGetCachedSnapshot() =>
        WorkspaceStatusService.TryGetCached(
            _shortcut,
            _settings.TerminalApplicationId,
            _settings.DefaultProfileId,
            _services.HealthChecker,
            _services.GitOperations,
            out var cached)
            ? cached
            : null;

    /// <summary>
    /// Runs the full capture off the navigation thread and applies the result through the
    /// callback queue, so the host thread owns the <see cref="FormContent.DataJson"/> write.
    /// </summary>
    private void ScheduleRefresh()
    {
        int generation;
        lock (_refreshGate)
        {
            generation = ++_refreshGeneration;
        }

        var cancellationToken = _services.Lifetime.CancellationToken;
        _ = Task.Run(
            () =>
            {
                WorkspaceStatusSnapshot snapshot;
                try
                {
                    snapshot = WorkspaceStatusService.Capture(
                        _shortcut,
                        _settings.TerminalApplicationId,
                        _settings.DefaultProfileId,
                        _services.HealthChecker,
                        _services.GitOperations,
                        _services.TargetStore,
                        forceRefresh: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    // Leave the placeholder in place; the Refresh action can retry.
                    return;
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                // Drop superseded captures under the same gate as ApplyPendingSnapshot so a
                // slow older refresh cannot overwrite (and then be discarded against) a newer one.
                lock (_refreshGate)
                {
                    if (_refreshGeneration != generation)
                    {
                        return;
                    }

                    _pendingSnapshot = new PendingSnapshot(snapshot, generation);
                }

                _notifyContentChanged();
            },
            cancellationToken);
    }

    /// <summary>
    /// Renders <paramref name="snapshot"/>, or a "checking" placeholder when none is available yet.
    /// </summary>
    private void PublishSnapshot(WorkspaceStatusSnapshot? snapshot)
    {
        DataJson = new JsonObject
        {
            ["Launches"] = BuildLaunchSummary(_shortcut, _services),
            ["Git"] = snapshot is { } git ? BuildGitSummary(git) : Checking,
            ["Runtime"] = snapshot?.ActivitySummary ?? Checking,
            ["Attention"] = snapshot?.AttentionEvidence ?? Checking,
            ["HasDiagnostics"] = LaunchDiagnosticsState.LastReport is not null,
            ["Refreshed"] = snapshot is { } refreshed
                ? refreshed.RefreshedAt.ToLocalTime().ToString("t", CultureInfo.CurrentCulture)
                : Checking,
        }.ToJsonString();
    }

    private const string Checking = "Checking…";

    private static string BuildGitSummary(WorkspaceStatusSnapshot snapshot)
    {
        if (snapshot.Git is null)
        {
            return "Not a git workspace or Git is unavailable";
        }

        var current = snapshot.Git.IsDetached
            ? "detached HEAD"
            : snapshot.Git.Branch;
        var target = string.IsNullOrWhiteSpace(snapshot.TargetBranch)
            ? "follow current branch"
            : snapshot.TargetBranch;
        var workingTree = snapshot.Git.IsDirty ? "dirty" : "clean";
        return $"Current: {current} · Target: {target} · {workingTree}";
    }

    private static string BuildLaunchSummary(TerminalShortcut shortcut, IQuickShellServices services)
    {
        ShortcutLaunchNormalization.EnsureLaunchesFromLegacy(shortcut);
        var count = ShortcutLaunchNormalization.GetEnabledLaunches(shortcut).Count;
        var companion = services.CompanionApps.IsConfigured(shortcut)
            ? $" · Companion: {services.CompanionApps.BuildDisplaySummary(shortcut)}"
            : string.Empty;
        return count == 1
            ? $"1 enabled launch{companion}"
            : $"{count} enabled launches{companion}";
    }

    private static string? TryGetAction(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(payload)?["action"]?.GetValue<string>();
        }
        catch
        {
            return null;
        }
    }

    private static string BuildTemplate() => $$"""
    {
      "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
      "type": "AdaptiveCard",
      "version": "1.6",
      "body": [
        { "type": "TextBlock", "text": "Workspace status", "size": "Large", "weight": "Bolder" },
        { "type": "TextBlock", "text": "Updated ${Refreshed}", "isSubtle": true, "spacing": "None" },
        { "type": "TextBlock", "text": "Launches", "weight": "Bolder", "spacing": "Medium" },
        { "type": "TextBlock", "text": "${Launches}", "wrap": true },
        { "type": "TextBlock", "text": "Git", "weight": "Bolder", "spacing": "Medium" },
        { "type": "TextBlock", "text": "${Git}", "wrap": true },
        { "type": "TextBlock", "text": "Runtime", "weight": "Bolder", "spacing": "Medium" },
        { "type": "TextBlock", "text": "${Runtime}", "wrap": true },
        { "type": "TextBlock", "text": "Attention", "weight": "Bolder", "spacing": "Medium" },
        { "type": "TextBlock", "text": "${Attention}", "wrap": true }
      ],
      "actions": [
        { "type": "Action.Submit", "title": "Refresh", "data": { "action": "refresh" } },
        { "$when": "${HasDiagnostics}", "type": "Action.Submit", "title": "{{Escape(Strings.Diagnostics_CopyLaunch_Title)}}", "data": { "action": "copyDiagnostics" } },
        { "type": "Action.Submit", "title": "{{Escape(Strings.Diagnostics_CopySupportBundle_Title)}}", "data": { "action": "copySupportBundle" } },
        { "type": "Action.Submit", "title": "{{Escape(Strings.Diagnostics_OpenLogFolder_Title)}}", "data": { "action": "openSupportLogs" } },
        { "type": "Action.Submit", "title": "Close", "data": { "action": "close" } }
      ]
    }
    """;

    private static string Escape(string value)
    {
        var serialized = JsonSerializer.Serialize(value, QuickShellJsonContext.Default.String);
        return serialized.Substring(1, serialized.Length - 2);
    }
}
