using Microsoft.CommandPalette.Extensions.Toolkit;

namespace QuickShell.Services;

internal enum SettingsFeedbackTone
{
    Info,
    Success,
    Warning,
    Error,
}

internal static class SettingsCardJson
{
    public static string SectionHeader(string title) =>
        $$"""
        {
          "type": "TextBlock",
          "text": "{{Escape(title)}}",
          "weight": "Bolder",
          "size": "Medium",
          "spacing": "None"
        }
        """;

    public static string SubtleText(string text) =>
        $$"""
        {
          "type": "TextBlock",
          "text": "{{Escape(text)}}",
          "wrap": true,
          "isSubtle": true,
          "spacing": "None"
        }
        """;

    public static string StatusText(string text, SettingsFeedbackTone tone = SettingsFeedbackTone.Success) =>
        $$"""
        {
          "type": "TextBlock",
          "text": "{{Escape(text)}}",
          "wrap": true,
          "color": "{{ToneColor(tone)}}",
          "spacing": "None"
        }
        """;

    public static string FieldLabel(string label) => AdaptiveCardFormJson.FieldLabel(label);

    public static string FieldHelp(string text) => AdaptiveCardFormJson.FieldHelp(text);

    public static string FieldGroup(string label, string help, string inputElementJson) =>
        AdaptiveCardFormJson.FieldGroup(label, help, inputElementJson);

    public static string ChangeActionSave(string action = "save") =>
        $$"""
        "changeAction": {
          "type": "Action.Submit",
          "associatedInputs": "auto",
          "data": { "action": "{{Escape(action)}}" }
        }
        """;

    public static string RecentEnabledToggle(bool enabled) =>
        $$"""
        {
          "type": "Container",
          "spacing": "None",
          "items": [
            {
              "type": "Input.Toggle",
              "id": "showRecents",
              "title": "Show recent workspaces",
              "spacing": "None",
              "value": "{{(enabled ? "true" : "false")}}",
              "valueOn": "true",
              "valueOff": "false",
              {{ChangeActionSave("saveRecents")}}
            },
            {{SubtleText($"Show up to {QuickShellRecentSettings.EnabledCount} recently used workspaces on the home page.")}}
          ]
        }
        """;

    public static string BuildChoicesJson(IEnumerable<ChoiceSetSetting.Choice> choices) =>
        string.Join(",\n", choices.Select(choice =>
            $$"""{ "title": "{{Escape(choice.Title)}}", "value": "{{Escape(choice.Value)}}" }"""));

    public static string TransferRow(string header, string description, string actionsJson, string topSpacing = "Small") =>
        $$"""
        {
          "type": "Container",
          "spacing": "{{topSpacing}}",
          "items": [
            {
              "type": "TextBlock",
              "text": "{{Escape(header)}}",
              "weight": "Bolder",
              "size": "Medium",
              "spacing": "None"
            },
            {{SubtleText(description)}},
            {{actionsJson}}
          ]
        }
        """;

    public static string TransferActionRow(
        string exportActionJson,
        string importActionJson,
        string resetActionJson) =>
        $$"""
        {
          "type": "ColumnSet",
          "spacing": "Small",
          "columns": [
            {
              "type": "Column",
              "width": "auto",
              "items": [
                {
                  "type": "ActionSet",
                  "spacing": "None",
                  "actions": [
                    {{exportActionJson}},
                    {{importActionJson}}
                  ]
                }
              ]
            },
            {
              "type": "Column",
              "width": "auto",
              "items": [
                {
                  "type": "ActionSet",
                  "spacing": "None",
                  "actions": [
                    {{resetActionJson}}
                  ]
                }
              ]
            }
          ]
        }
        """;

    private static string ToneColor(SettingsFeedbackTone tone) => tone switch
    {
        SettingsFeedbackTone.Warning => "Warning",
        SettingsFeedbackTone.Error => "Attention",
        SettingsFeedbackTone.Info => "Default",
        _ => "Good",
    };

    private static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
