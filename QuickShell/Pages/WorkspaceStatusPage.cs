using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Commands;
using QuickShell.Models;
using QuickShell.Services;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

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

    public override IContent[] GetContent() =>
        [_form ??= new WorkspaceStatusForm(_shortcut, _settings, _services, () => _form = null)];
}

internal sealed partial class WorkspaceStatusForm : FormContent
{
    private readonly TerminalShortcut _shortcut;
    private readonly QuickShellSettingsManager _settings;
    private readonly IQuickShellServices _services;
    private readonly Action _releaseForm;

    public WorkspaceStatusForm(
        TerminalShortcut shortcut,
        QuickShellSettingsManager settings,
        IQuickShellServices services,
        Action releaseForm)
    {
        _shortcut = shortcut;
        _settings = settings;
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _releaseForm = releaseForm;
        TemplateJson = BuildTemplate();
        Refresh(forceRefresh: true);
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
                Refresh(forceRefresh: true);
                return CommandResult.KeepOpen();
            case "copyDiagnostics":
                LaunchDiagnosticsState.TryCopyLastReport(out var message);
                return QuickShellNavigation.StayOpen(message);
            case "copySupportBundle":
                SupportDiagnostics.TryCopyBundle(LaunchDiagnosticsState.LastReport, out var supportMessage);
                return QuickShellNavigation.StayOpen(supportMessage);
            case "openSupportLogs":
                return SupportDiagnostics.TryOpenLogFolder(out var error)
                    ? QuickShellNavigation.StayOpen(Strings.Diagnostics_LogFolderOpened)
                    : QuickShellNavigation.StayOpen(error);
            case "close":
                _releaseForm();
                return QuickShellNavigation.GoBack();
            default:
                return CommandResult.KeepOpen();
        }
    }

    private void Refresh(bool forceRefresh)
    {
        var snapshot = WorkspaceStatusService.Capture(
            _shortcut,
            _settings.TerminalApplicationId,
            _settings.DefaultProfileId,
            _services.HealthChecker,
            _services.GitOperations,
            forceRefresh);
        DataJson = new JsonObject
        {
            ["Launches"] = BuildLaunchSummary(_shortcut, _services),
            ["Git"] = BuildGitSummary(snapshot),
            ["Runtime"] = snapshot.ActivitySummary,
            ["Attention"] = snapshot.AttentionEvidence,
            ["HasDiagnostics"] = LaunchDiagnosticsState.LastReport is not null,
            ["Refreshed"] = snapshot.RefreshedAt.ToLocalTime().ToString("t", CultureInfo.CurrentCulture),
        }.ToJsonString();
    }

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
