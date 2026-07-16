using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Services;
using System.Text.Json.Nodes;

namespace QuickShell.Pages;

/// <summary>
/// Combined settings card: terminal defaults, Home / Multi / Git toggles, and Backup & Transfer.
/// One Adaptive Card so vertical spacing is controlled (no host gap between separate forms).
/// </summary>
internal sealed partial class BehaviorSettingsForm : FormContent
{
    private const string SingleWindowTabsField = "singleWindowTabs";
    private const string ShowRecentsField = "showRecents";
    private const string BlockDirtyBranchSwitchField = "blockDirtyBranchSwitch";

    private readonly QuickShellSettingsManager _settingsManager;
    private readonly Action? _onReload;
    private readonly Action? _onSettingsChanged;
    private readonly TerminalDefaultsSettingsForm _terminalForm;
    private readonly ShortcutTransferSettingsForm _transferForm;
    private bool _pendingSingleWindowTabs;
    private bool _pendingShowRecents;
    private bool _pendingBlockDirtyBranchSwitch;
    private bool _rebuilding;

    public BehaviorSettingsForm(
        QuickShellSettingsManager settingsManager,
        Action? onReload = null,
        Action? onSettingsChanged = null)
    {
        _settingsManager = settingsManager;
        _onReload = onReload;
        _onSettingsChanged = onSettingsChanged;
        _terminalForm = new TerminalDefaultsSettingsForm(
            settingsManager,
            onReload,
            onSettingsChanged,
            RebuildTemplate);
        _transferForm = new ShortcutTransferSettingsForm(onReload, onSettingsChanged, RebuildTemplate);
        SyncPendingFromSettings();
        RebuildTemplate();
    }

    internal void SyncFromSettings()
    {
        var nextTabs = !_settingsManager.SeparateWindowsForMultiLaunch;
        var nextRecents = QuickShellRecentSettings.IsEnabled(_settingsManager.RecentWorkspaceCount);
        var nextGit = _settingsManager.BlockDirtyBranchSwitch;
        var nextApp = _settingsManager.TerminalApplicationId;
        var nextProfile = _settingsManager.DefaultProfileId;

        // Avoid rebuilding the large settings Adaptive Card when nothing changed.
        if (nextTabs == _pendingSingleWindowTabs
            && nextRecents == _pendingShowRecents
            && nextGit == _pendingBlockDirtyBranchSwitch
            && _terminalForm.MatchesPending(nextApp, nextProfile)
            && !string.IsNullOrEmpty(TemplateJson))
        {
            return;
        }

        _terminalForm.SyncFromSettings(notifyParent: false);
        _pendingSingleWindowTabs = nextTabs;
        _pendingShowRecents = nextRecents;
        _pendingBlockDirtyBranchSwitch = nextGit;
        RebuildTemplate();
    }

    private void SyncPendingFromSettings()
    {
        _pendingSingleWindowTabs = !_settingsManager.SeparateWindowsForMultiLaunch;
        _pendingShowRecents = QuickShellRecentSettings.IsEnabled(_settingsManager.RecentWorkspaceCount);
        _pendingBlockDirtyBranchSwitch = _settingsManager.BlockDirtyBranchSwitch;
    }

    public override CommandResult SubmitForm(string payload) => SubmitForm(payload, string.Empty);

    public override CommandResult SubmitForm(string inputs, string data)
    {
        var action = TryGetAction(data) ?? TryGetActionFromInputs(inputs);
        if (string.Equals(action, "refreshTerminals", StringComparison.OrdinalIgnoreCase))
        {
            // Terminal refresh lives inside the combined settings card, whose
            // unsaved toggles must survive the embedded action as well.
            ApplyAllPendingFromValues(ParseValues(inputs, data));
        }

        if (_terminalForm.TryHandleAction(action, inputs, data, out var terminalResult))
        {
            return terminalResult;
        }

        if (_transferForm.TryHandleAction(action, inputs, data, out var transferResult))
        {
            return transferResult;
        }

        return action switch
        {
            "previewSettings" => PreviewSettings(inputs, data),
            "saveAndCloseSettings" => SaveAndClose(inputs, data),
            "cancelSettings" => CancelSettings(),
            // Legacy action names (if any host caches old templates).
            "saveMultiLaunch" or "saveRecents" or "saveGitLaunch" => PreviewSettings(inputs, data),
            _ => CommandResult.KeepOpen(),
        };
    }

    private CommandResult PreviewSettings(string inputs, string data)
    {
        var values = ParseValues(inputs, data);
        ApplyAllPendingFromValues(values);
        RebuildTemplate();
        return CommandResult.KeepOpen();
    }

    private CommandResult SaveAndClose(string inputs, string data)
    {
        var values = ParseValues(inputs, data);
        ApplyAllPendingFromValues(values);

        if (!_terminalForm.TryCommitPending(values, out var error))
        {
            RebuildTemplate();
            return QuickShellNavigation.StayOnSettings(error);
        }

        CommitToggleSettings();
        SettingsFormHelpers.SchedulePostNavigationRefresh(_onReload);
        SettingsFormHelpers.ScheduleRefresh(_onSettingsChanged);
        return QuickShellNavigation.GoBack(Strings.Saved_Toast);
    }

    private CommandResult CancelSettings()
    {
        // Discard in-memory pending; nothing written for toggles/terminal until Save & close.
        _terminalForm.SyncFromSettings(notifyParent: false);
        SyncPendingFromSettings();
        RebuildTemplate();
        return QuickShellNavigation.GoBack();
    }

    private void ApplyAllPendingFromValues(JsonObject? values)
    {
        _terminalForm.ApplyPendingFromValues(values);
        _pendingSingleWindowTabs = ParseToggleBool(
            values?[SingleWindowTabsField]?.ToString(),
            _pendingSingleWindowTabs);
        _pendingShowRecents = ParseToggleBool(
            values?[ShowRecentsField]?.ToString(),
            _pendingShowRecents);
        _pendingBlockDirtyBranchSwitch = ParseToggleBool(
            values?[BlockDirtyBranchSwitchField]?.ToString(),
            _pendingBlockDirtyBranchSwitch);
    }

    private void CommitToggleSettings()
    {
        var nextRecents = QuickShellRecentSettings.FromEnabled(_pendingShowRecents);
        if (nextRecents != _settingsManager.RecentWorkspaceCount)
        {
            _settingsManager.UpdateRecentWorkspaceCount(nextRecents);
        }

        if (_pendingSingleWindowTabs != !_settingsManager.SeparateWindowsForMultiLaunch)
        {
            _settingsManager.UpdateMultiLaunchPresentation(_pendingSingleWindowTabs);
        }

        if (_pendingBlockDirtyBranchSwitch != _settingsManager.BlockDirtyBranchSwitch)
        {
            _settingsManager.UpdateBlockDirtyBranchSwitch(_pendingBlockDirtyBranchSwitch);
        }
    }

    private void RebuildTemplate()
    {
        if (_rebuilding)
        {
            return;
        }

        _rebuilding = true;
        try
        {
            // Prefer pending terminal app (while editing) for multi-launch WT hint.
            var usesWt = TerminalHostIds.UsesWindowsTerminalProfiles(
                ExtractPendingTerminalApp() ?? _settingsManager.TerminalApplicationId);

            var homeDisplayColumn = $$"""
                {{SettingsCardJson.SectionHeader(Strings.HomeDisplay_SectionHeader)}},
                {{SettingsCardJson.RecentEnabledToggle(_pendingShowRecents)}}
                """;
            var multiLaunchColumn = $$"""
                {{SettingsCardJson.SectionHeader(Strings.MultiLaunch_SectionHeader)}},
                {{SettingsCardJson.MultiLaunchTabsToggle(_pendingSingleWindowTabs, usesWt)}}
                """;
            var gitLaunchColumn = $$"""
                {{SettingsCardJson.SectionHeader(Strings.GitLaunch_SectionHeader)}},
                {{SettingsCardJson.BlockDirtyBranchToggle(_pendingBlockDirtyBranchSwitch)}}
                """;

            TemplateJson = $$"""
                {
                  "type": "AdaptiveCard",
                  "version": "1.6",
                  "body": [
                    {{_terminalForm.BodyElementsJson}},
                    {{SettingsCardJson.ThreeColumnSection(
                        homeDisplayColumn,
                        multiLaunchColumn,
                        gitLaunchColumn,
                        spacing: "Medium")}},
                    {{_transferForm.BodyElementsJson}},
                    {{SettingsCardJson.SettingsFooterActions()}}
                  ]
                }
                """;
            DataJson = _terminalForm.BoundDataJson;
        }
        finally
        {
            _rebuilding = false;
        }
    }

    private string? ExtractPendingTerminalApp()
    {
        try
        {
            return JsonNode.Parse(_terminalForm.BoundDataJson)?["terminalApplication"]?.GetValue<string>();
        }
        catch
        {
            return null;
        }
    }

    private static string? TryGetAction(string? data) =>
        string.IsNullOrWhiteSpace(data)
            ? null
            : JsonNode.Parse(data)?.AsObject()?["action"]?.ToString();

    private static string? TryGetActionFromInputs(string inputs) =>
        JsonNode.Parse(inputs)?.AsObject()?["action"]?.ToString();

    private static JsonObject? ParseValues(string inputs, string data)
    {
        JsonObject? merged = null;

        if (!string.IsNullOrWhiteSpace(inputs))
        {
            merged = JsonNode.Parse(inputs)?.AsObject();
        }

        if (!string.IsNullOrWhiteSpace(data))
        {
            var dataObject = JsonNode.Parse(data)?.AsObject();
            if (dataObject is not null)
            {
                merged ??= new JsonObject();
                foreach (var property in dataObject)
                {
                    merged[property.Key] = property.Value?.DeepClone();
                }
            }
        }

        return merged;
    }

    private static bool ParseToggleBool(string? value, bool fallback) =>
        value switch
        {
            "true" => true,
            "false" => false,
            _ => fallback,
        };
}
