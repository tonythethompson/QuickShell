using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Services;
using System.Text.Json.Nodes;

namespace QuickShell.Pages;

internal sealed partial class TerminalDefaultsSettingsForm : FormContent
{
    private const string TerminalApplicationField = "terminalApplication";
    private const string DefaultProfileField = "defaultProfile";

    private readonly QuickShellSettingsManager _settingsManager;
    private readonly Action? _onReload;
    private readonly Action? _onSettingsChanged;
    private string _pendingApp = TerminalHostIds.WindowsTerminal;
    private string _pendingProfile = TerminalHostIds.DefaultProfile;

    public TerminalDefaultsSettingsForm(
        QuickShellSettingsManager settingsManager,
        Action? onReload = null,
        Action? onSettingsChanged = null)
    {
        _settingsManager = settingsManager;
        _onReload = onReload;
        _onSettingsChanged = onSettingsChanged;
        SyncPendingFromSettings();
        RebuildTemplate();
    }

    internal void SyncFromSettings()
    {
        SyncPendingFromSettings();
        RebuildTemplate();
    }

    public override CommandResult SubmitForm(string inputs, string data) =>
        HandleSubmit(inputs, data);

    public override CommandResult SubmitForm(string payload) =>
        HandleSubmit(payload, string.Empty);

    private CommandResult HandleSubmit(string inputs, string data)
    {
        var values = ParseValues(inputs, data);
        var action = TryGetAction(data) ?? TryGetActionFromInputs(inputs);

        if (action == "refreshTerminals")
        {
            return RefreshTerminals();
        }

        if (IsSaveRequest(action, values))
        {
            return SaveFromValues(values);
        }

        return CommandResult.KeepOpen();
    }

    private CommandResult SaveFromValues(JsonObject? values)
    {
        var previousApp = _pendingApp;
        var previousProfile = _pendingProfile;

        var app = SettingsFormValueReader.ReadString(values, TerminalApplicationField) ?? _pendingApp;
        var profile = SettingsFormValueReader.ReadString(values, DefaultProfileField) ?? _pendingProfile;

        if (string.IsNullOrWhiteSpace(app) || string.IsNullOrWhiteSpace(profile))
        {
            return QuickShellNavigation.StayOnSettings("Pick a terminal application and profile.");
        }

        _settingsManager.UpdateTerminalDefaults(app, profile);
        SyncPendingFromSettings();
        RebuildTemplate();
        SettingsFormHelpers.ScheduleRefresh(_onSettingsChanged);

        if (!previousApp.Equals(_pendingApp, StringComparison.OrdinalIgnoreCase)
            || !previousProfile.Equals(_pendingProfile, StringComparison.OrdinalIgnoreCase))
        {
            QuickShellStatus.ShowToast("Saved");
        }

        return CommandResult.KeepOpen();
    }

    private CommandResult RefreshTerminals()
    {
        TerminalDiscovery.Refresh(_settingsManager);
        _onReload?.Invoke();
        SyncPendingFromSettings();
        RebuildTemplate();
        SettingsFormHelpers.ScheduleRefresh(_onSettingsChanged);
        return QuickShellNavigation.StayOnSettings("Terminal list refreshed.");
    }

    private void SyncPendingFromSettings()
    {
        _pendingApp = _settingsManager.TerminalApplicationId;
        _pendingProfile = _settingsManager.DefaultProfileId;
    }

    private void RebuildTemplate()
    {
        var appChoices = SettingsCardJson.BuildChoicesJson(TerminalCatalogChoices.GetTerminalApplicationChoices());
        var profileChoices = SettingsCardJson.BuildChoicesJson(TerminalCatalogChoices.GetDefaultProfileChoices(_pendingApp));
        var bodyParts = new List<string>
        {
            SettingsCardJson.SectionHeader("Terminal defaults"),
            SettingsCardJson.SubtleText("Default host and profile for workspaces set to Default. Changes save when you pick a value."),
            """
            {
              "type": "ActionSet",
              "spacing": "None",
              "actions": [
                {
                  "type": "Action.Submit",
                  "title": "Refresh terminal list",
                  "tooltip": "Reload profiles after installing a shell or editing Windows Terminal settings.",
                  "associatedInputs": "none",
                  "data": { "action": "refreshTerminals" }
                },
                {
                  "type": "Action.Submit",
                  "title": "Save terminal defaults",
                  "associatedInputs": "auto",
                  "data": { "action": "saveTerminalDefaults" }
                }
              ]
            }
            """,
            $$"""
            {
              "type": "Input.ChoiceSet",
              "id": "{{TerminalApplicationField}}",
              "label": "Terminal application",
              "style": "compact",
              "spacing": "None",
              "value": "${terminalApplication}",
              {{SettingsCardJson.ChangeActionSave("saveTerminalDefaults")}},
              "choices": [
                {{appChoices}}
              ]
            }
            """,
            $$"""
            {
              "type": "Input.ChoiceSet",
              "id": "{{DefaultProfileField}}",
              "label": "Default profile",
              "style": "compact",
              "spacing": "None",
              "value": "${defaultProfile}",
              {{SettingsCardJson.ChangeActionSave("saveTerminalDefaults")}},
              "choices": [
                {{profileChoices}}
              ]
            }
            """,
        };

        var bodyJson = string.Join(",\n                ", bodyParts);

        TemplateJson = $$"""
            {
              "type": "AdaptiveCard",
              "version": "1.6",
              "body": [
                {{bodyJson}}
              ]
            }
            """;

        DataJson = $$"""
            {
              "terminalApplication": "{{EscapeJson(_pendingApp)}}",
              "defaultProfile": "{{EscapeJson(_pendingProfile)}}"
            }
            """;
    }

    private static bool IsSaveRequest(string? action, JsonObject? values) =>
        action == "saveTerminalDefaults"
        || values?.ContainsKey(TerminalApplicationField) == true
        || values?.ContainsKey(DefaultProfileField) == true;

    private static string? TryGetAction(string? data) =>
        string.IsNullOrWhiteSpace(data)
            ? null
            : JsonNode.Parse(data)?.AsObject()?["action"]?.GetValue<string>();

    private static string? TryGetActionFromInputs(string inputs) =>
        string.IsNullOrWhiteSpace(inputs)
            ? null
            : JsonNode.Parse(inputs)?.AsObject()?["action"]?.GetValue<string>();

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
                    if (property.Key.Equals("action", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    merged[property.Key] = property.Value?.DeepClone();
                }
            }
        }

        return merged;
    }

    private static string EscapeJson(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
