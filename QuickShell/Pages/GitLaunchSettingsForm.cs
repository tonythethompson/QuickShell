using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Services;
using System.Text.Json.Nodes;

namespace QuickShell.Pages;

internal sealed partial class GitLaunchSettingsForm : FormContent
{
    private const string BlockDirtyBranchSwitchField = "blockDirtyBranchSwitch";

    private readonly QuickShellSettingsManager _settingsManager;
    private readonly Action? _onReload;
    private readonly Action? _onSettingsChanged;
    private bool _pendingBlockDirtyBranchSwitch;

    public GitLaunchSettingsForm(
        QuickShellSettingsManager settingsManager,
        Action? onReload = null,
        Action? onSettingsChanged = null)
    {
        _settingsManager = settingsManager;
        _onReload = onReload;
        _onSettingsChanged = onSettingsChanged;
        _pendingBlockDirtyBranchSwitch = settingsManager.BlockDirtyBranchSwitch;
        RebuildTemplate();
    }

    internal void SyncFromSettings()
    {
        _pendingBlockDirtyBranchSwitch = _settingsManager.BlockDirtyBranchSwitch;
        RebuildTemplate();
    }

    public override CommandResult SubmitForm(string payload) => SubmitForm(payload, string.Empty);

    public override CommandResult SubmitForm(string inputs, string data)
    {
        var action = TryGetAction(data) ?? TryGetActionFromInputs(inputs);
        return action switch
        {
            "saveGitLaunch" => SaveFromInputs(inputs, data),
            _ => CommandResult.KeepOpen(),
        };
    }

    private CommandResult SaveFromInputs(string inputs, string data)
    {
        var values = ParseValues(inputs, data);
        var blockDirtyBranchSwitch = ParseToggleBool(
            values?[BlockDirtyBranchSwitchField]?.ToString(),
            _pendingBlockDirtyBranchSwitch);

        if (blockDirtyBranchSwitch != _settingsManager.BlockDirtyBranchSwitch)
        {
            _settingsManager.UpdateBlockDirtyBranchSwitch(blockDirtyBranchSwitch);
            SettingsFormHelpers.SchedulePostNavigationRefresh(_onReload);
            SettingsFormHelpers.ScheduleRefresh(_onSettingsChanged);
            QuickShellStatus.ShowToast("Saved");
        }

        _pendingBlockDirtyBranchSwitch = blockDirtyBranchSwitch;
        RebuildTemplate();
        return CommandResult.KeepOpen();
    }

    private void RebuildTemplate()
    {
        TemplateJson = $$"""
            {
              "type": "AdaptiveCard",
              "version": "1.6",
              "body": [
                {{SettingsCardJson.SectionHeader("Git launch")}},
                {
                  "type": "Container",
                  "spacing": "None",
                  "items": [
                    {
                      "type": "Input.Toggle",
                      "id": "{{BlockDirtyBranchSwitchField}}",
                      "title": "Block launch when dirty and branch would change",
                      "spacing": "None",
                      "value": "{{(_pendingBlockDirtyBranchSwitch ? "true" : "false")}}",
                      "valueOn": "true",
                      "valueOff": "false",
                      {{SettingsCardJson.ChangeActionSave("saveGitLaunch")}}
                    },
                    {{SettingsCardJson.SubtleText("When a worktree target branch differs from HEAD, block launch and branch switching if the working tree has uncommitted changes.")}},
                    {{SettingsCardJson.SubtleText("Need two branches open at once? Use git worktree add to create a separate folder and workspace.")}}
                  ]
                }
              ]
            }
            """;
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
