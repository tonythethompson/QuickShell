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

    public TerminalDefaultsSettingsForm(
        QuickShellSettingsManager settingsManager,
        Action? onReload = null,
        Action? onSettingsChanged = null)
    {
        _settingsManager = settingsManager;
        _onReload = onReload;
        _onSettingsChanged = onSettingsChanged;
        RebuildTemplate();
    }

    public override CommandResult SubmitForm(string payload) => SubmitForm(payload, string.Empty);

    public override CommandResult SubmitForm(string inputs, string data)
    {
        var action = TryGetAction(data);
        if (action == "refreshTerminals")
        {
            return RefreshTerminals();
        }

        return SaveFromInputs(inputs, data);
    }

    private CommandResult SaveFromInputs(string inputs, string data)
    {
        var values = ParseValues(inputs, data);
        var app = values?[TerminalApplicationField]?.ToString() ?? _settingsManager.TerminalApplicationId;
        var profile = values?[DefaultProfileField]?.ToString() ?? _settingsManager.DefaultProfileId;

        if (string.IsNullOrWhiteSpace(app) || string.IsNullOrWhiteSpace(profile))
        {
            return QuickShellNavigation.StayOnSettings("Pick a terminal application and profile.");
        }

        _settingsManager.UpdateTerminalDefaults(app, profile);
        RebuildTemplate();
        SettingsFormHelpers.ScheduleRefresh(_onSettingsChanged);
        return CommandResult.KeepOpen();
    }

    private CommandResult RefreshTerminals()
    {
        TerminalDiscovery.Refresh(_settingsManager);
        _onReload?.Invoke();
        RebuildTemplate();
        SettingsFormHelpers.ScheduleRefresh(_onSettingsChanged);
        return QuickShellNavigation.StayOnSettings("Terminal list refreshed.");
    }

    private void RebuildTemplate()
    {
        var app = _settingsManager.TerminalApplicationId;
        var profile = _settingsManager.DefaultProfileId;
        var appChoices = SettingsCardJson.BuildChoicesJson(TerminalCatalogChoices.GetTerminalApplicationChoices());
        var profileChoices = SettingsCardJson.BuildChoicesJson(TerminalCatalogChoices.GetDefaultProfileChoices(app));
        var bodyParts = new List<string>
        {
            SettingsCardJson.SectionHeader("Terminal defaults"),
            SettingsCardJson.SubtleText("Default host and profile for workspaces set to Default. Changes save when you pick a value."),
            """
            {
              "type": "ActionSet",
              "spacing": "Small",
              "actions": [
                {
                  "type": "Action.Submit",
                  "title": "Refresh terminal list",
                  "tooltip": "Reload profiles after installing a shell or editing Windows Terminal settings.",
                  "associatedInputs": "none",
                  "data": { "action": "refreshTerminals" }
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
              "spacing": "Small",
              "value": "{{EscapeJson(app)}}",
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
              "spacing": "Small",
              "value": "{{EscapeJson(profile)}}",
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
    }

    private static string? TryGetAction(string? data) =>
        string.IsNullOrWhiteSpace(data)
            ? null
            : JsonNode.Parse(data)?.AsObject()?["action"]?.ToString();

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

    private static string EscapeJson(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
