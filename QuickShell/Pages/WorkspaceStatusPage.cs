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
    private readonly Action _onChanged;
    private bool _gitCommandsLoaded;
    private WorkspaceStatusForm? _form;

    public WorkspaceStatusPage(
        TerminalShortcut shortcut,
        QuickShellSettingsManager settings,
        Action onChanged)
    {
        _shortcut = shortcut;
        _settings = settings;
        _onChanged = onChanged;
        Id = ShortcutCommandIds.WorkspaceStatus(shortcut.Id);
        Name = "Workspace status";
        Title = shortcut.Name;
        Icon = new IconInfo("");
        // Do not run git here. Every home-list row builds this page for the
        // "Workspace status…" context command; eager TryGetStatus made open
        // take tens of seconds with ~45 workspaces (and worse for WSL paths).
        Commands = [];
    }

    public override IContent[] GetContent()
    {
        EnsureGitCommands();
        return [_form ??= new WorkspaceStatusForm(_shortcut, _settings, () => _form = null)];
    }

    private void EnsureGitCommands()
    {
        if (_gitCommandsLoaded)
        {
            return;
        }

        _gitCommandsLoaded = true;
        Commands = BuildGitCommands(_shortcut, _settings, _onChanged);
    }

    private static CommandContextItem[] BuildGitCommands(
        TerminalShortcut shortcut,
        QuickShellSettingsManager settings,
        Action onChanged)
    {
        if (!WorkspaceGitOperations.TryGetStatus(shortcut.Directory, out var status))
        {
            return [];
        }

        var target = WorktreeBranchTargetStore.GetTargetForDirectory(shortcut.Directory);
        var items = new List<CommandContextItem>
        {
            new(new WorktreeBranchPickerPage(shortcut.Id, settings, onChanged, status, target))
            {
                Title = "Switch branch…",
                Icon = new IconInfo(""),
            },
        };

        if (!string.IsNullOrWhiteSpace(target))
        {
            items.Add(new CommandContextItem(new UseCurrentWorktreeBranchCommand(shortcut.Id, onChanged))
            {
                Title = "Use current branch",
                Icon = new IconInfo(""),
            });
        }

        return items.ToArray();
    }
}

internal sealed partial class WorkspaceStatusForm : FormContent
{
    private readonly TerminalShortcut _shortcut;
    private readonly QuickShellSettingsManager _settings;
    private readonly Action _releaseForm;

    public WorkspaceStatusForm(
        TerminalShortcut shortcut,
        QuickShellSettingsManager settings,
        Action releaseForm)
    {
        _shortcut = shortcut;
        _settings = settings;
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
            forceRefresh);
        DataJson = new JsonObject
        {
            ["Launches"] = BuildLaunchSummary(_shortcut),
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

    private static string BuildLaunchSummary(TerminalShortcut shortcut)
    {
        ShortcutLaunchNormalization.EnsureLaunchesFromLegacy(shortcut);
        var count = ShortcutLaunchNormalization.GetEnabledLaunches(shortcut).Count;
        var companion = CompanionAppLauncher.IsConfigured(shortcut)
            ? $" · Companion: {CompanionAppLauncher.BuildDisplaySummary(shortcut)}"
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
