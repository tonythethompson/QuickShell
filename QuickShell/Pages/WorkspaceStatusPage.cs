using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Commands;
using QuickShell.Models;
using QuickShell.Services;
using System.Globalization;
using System.Text.Json.Nodes;

namespace QuickShell.Pages;

internal sealed partial class WorkspaceStatusPage : ContentPage
{
    private readonly TerminalShortcut _shortcut;
    private readonly QuickShellSettingsManager _settings;
    private readonly Action _onChanged;
    private bool _commandsReady;
    private WorkspaceStatusSnapshot? _snapshot;

    public WorkspaceStatusPage(
        TerminalShortcut shortcut,
        QuickShellSettingsManager settings,
        Action onChanged)
    {
        _shortcut = shortcut;
        _settings = settings;
        _onChanged = () =>
        {
            // Branch switches invalidate the captured snapshot so the next
            // open re-probes git status instead of reusing stale commands.
            _commandsReady = false;
            _snapshot = null;
            onChanged();
        };
        Id = ShortcutCommandIds.WorkspaceStatus(shortcut.Id);
        Name = "Workspace status";
        Title = shortcut.Name;
        Icon = new IconInfo("");
        // Do not probe git status here. Home list builds this page for every
        // workspace when attaching MoreCommands; defer until the page opens.
        Commands = [];
    }

    public override IContent[] GetContent()
    {
        EnsureGitCommands();
        if (_form is not null)
        {
            _form.Refresh(_snapshot!);
            return [_form];
        }

        return [_form = new WorkspaceStatusForm(
            _shortcut,
            _settings,
            () => _form = null,
            onRefresh: () => _commandsReady = false,
            _snapshot!)];
    }

    private WorkspaceStatusForm? _form;

    private void EnsureGitCommands()
    {
        if (_commandsReady)
        {
            return;
        }

        _snapshot = WorkspaceStatusService.Capture(
            _shortcut,
            _settings.TerminalApplicationId,
            _settings.DefaultProfileId,
            forceRefresh: true);
        Commands = BuildGitCommands(_shortcut, _settings, _onChanged, _snapshot);
        _commandsReady = true;
    }

    private static CommandContextItem[] BuildGitCommands(
        TerminalShortcut shortcut,
        QuickShellSettingsManager settings,
        Action onChanged,
        WorkspaceStatusSnapshot? snapshot)
    {
        if (snapshot?.Git is null)
        {
            return [];
        }

        var items = new List<CommandContextItem>
        {
            new(new WorktreeBranchPickerPage(shortcut.Id, settings, onChanged, snapshot.Git, snapshot.TargetBranch))
            {
                Title = "Switch branch…",
                Icon = new IconInfo(""),
            },
        };

        if (!string.IsNullOrWhiteSpace(snapshot.TargetBranch))
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
    private readonly Action _onRefresh;

    public WorkspaceStatusForm(
        TerminalShortcut shortcut,
        QuickShellSettingsManager settings,
        Action releaseForm,
        Action onRefresh,
        WorkspaceStatusSnapshot? initialSnapshot = null)
    {
        _shortcut = shortcut;
        _settings = settings;
        _releaseForm = releaseForm;
        _onRefresh = onRefresh;
        TemplateJson = Template;
        if (initialSnapshot is not null)
        {
            Refresh(initialSnapshot);
        }
        else
        {
            Refresh(forceRefresh: true);
        }
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
                _onRefresh();
                return CommandResult.KeepOpen();
            case "copyDiagnostics":
                LaunchDiagnosticsState.TryCopyLastReport(out var message);
                return QuickShellNavigation.StayOpen(message);
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
        Refresh(snapshot);
    }

    internal void Refresh(WorkspaceStatusSnapshot snapshot)
    {
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
            ? $" · Companion: {CompanionAppCatalog.GetDisplayName(shortcut.CompanionAppPath)}"
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

    private const string Template = """
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
        { "$when": "${HasDiagnostics}", "type": "Action.Submit", "title": "Copy launch diagnostics", "data": { "action": "copyDiagnostics" } },
        { "type": "Action.Submit", "title": "Close", "data": { "action": "close" } }
      ]
    }
    """;
}
