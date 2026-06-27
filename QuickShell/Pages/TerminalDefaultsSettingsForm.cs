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

        var values = ParseValues(inputs, data);
        var app = values?[TerminalApplicationField]?.ToString() ?? _settingsManager.TerminalApplicationId;
        var profile = values?[DefaultProfileField]?.ToString() ?? _settingsManager.DefaultProfileId;

        if (string.IsNullOrWhiteSpace(app) || string.IsNullOrWhiteSpace(profile))
        {
            return Finish("Pick a terminal application and profile.");
        }

        _settingsManager.UpdateTerminalDefaults(app, profile);
        return Finish("Terminal defaults saved.");
    }

    private CommandResult RefreshTerminals()
    {
        TerminalDiscovery.Refresh(_settingsManager);
        _onReload?.Invoke();
        RebuildTemplate();
        return Finish("Terminal list refreshed.", refreshContent: false);
    }

    private CommandResult Finish(string message, bool refreshContent = true)
    {
        RebuildTemplate();
        if (refreshContent)
        {
            SettingsFormHelpers.ScheduleRefresh(_onSettingsChanged);
        }

        return QuickShellNavigation.StayOnSettings(message);
    }

    private void RebuildTemplate()
    {
        var app = _settingsManager.TerminalApplicationId;
        var profile = _settingsManager.DefaultProfileId;
        var appChoices = SettingsCardJson.BuildChoicesJson(TerminalCatalog.GetTerminalApplicationChoices());
        var profileChoices = SettingsCardJson.BuildChoicesJson(TerminalCatalog.GetDefaultProfileChoices(app));
        var bodyParts = new List<string>
        {
            SettingsCardJson.SectionHeader("Terminal defaults"),
            SettingsCardJson.SubtleText("Default host and profile for shortcuts set to Default."),
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
              ],
              "actions": [
                {
                  "type": "Action.Submit",
                  "title": "Save",
                  "associatedInputs": "auto"
                }
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
