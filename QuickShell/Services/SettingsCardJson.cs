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
          "spacing": "Small"
        }
        """;

    public static string StatusText(string text, SettingsFeedbackTone tone = SettingsFeedbackTone.Success) =>
        $$"""
        {
          "type": "TextBlock",
          "text": "{{Escape(text)}}",
          "wrap": true,
          "color": "{{ToneColor(tone)}}",
          "spacing": "Small"
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

    public static string RecentCountStepper(int count)
    {
        var canDecrement = count > QuickShellRecentSettings.MinCount;
        var canIncrement = count < QuickShellRecentSettings.MaxCount;
        return $$"""
        {
          "type": "Container",
          "spacing": "Small",
          "items": [
            {{FieldLabel("Recent workspaces to show")}},
            {{SubtleText("How many recently used workspaces appear on the QuickShell home page. Set to 0 to hide the Recent section.")}},
            {
              "type": "Container",
              "style": "emphasis",
              "spacing": "None",
              "items": [
                {
                  "type": "ColumnSet",
                  "spacing": "None",
                  "columns": [
                    {
                      "type": "Column",
                      "width": "auto",
                      "verticalContentAlignment": "Center",
                      "items": [
                        {
                          "type": "ActionSet",
                          "spacing": "None",
                          "actions": [
                            {
                              "type": "Action.Submit",
                              "title": "\u2212",
                              "tooltip": "Show fewer recent workspaces",
                              "associatedInputs": "none",
                              "isEnabled": {{canDecrement.ToString().ToLowerInvariant()}},
                              "data": { "action": "recentDecrement" }
                            }
                          ]
                        }
                      ]
                    },
                    {
                      "type": "Column",
                      "width": "stretch",
                      "verticalContentAlignment": "Center",
                      "items": [
                        {
                          "type": "TextBlock",
                          "text": "{{count}}",
                          "horizontalAlignment": "Center",
                          "size": "Medium",
                          "weight": "Bolder"
                        }
                      ]
                    },
                    {
                      "type": "Column",
                      "width": "auto",
                      "verticalContentAlignment": "Center",
                      "items": [
                        {
                          "type": "ActionSet",
                          "spacing": "None",
                          "actions": [
                            {
                              "type": "Action.Submit",
                              "title": "+",
                              "tooltip": "Show more recent workspaces",
                              "associatedInputs": "none",
                              "isEnabled": {{canIncrement.ToString().ToLowerInvariant()}},
                              "data": { "action": "recentIncrement" }
                            }
                          ]
                        }
                      ]
                    }
                  ]
                }
              ]
            }
          ]
        }
        """;
    }

    public static string BuildChoicesJson(IEnumerable<ChoiceSetSetting.Choice> choices) =>
        string.Join(",\n", choices.Select(choice =>
            $$"""{ "title": "{{Escape(choice.Title)}}", "value": "{{Escape(choice.Value)}}" }"""));

    public static string TransferRow(string header, string description, string actionsJson, string topSpacing = "Large") =>
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
              "spacing": "Small"
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
          "spacing": "Medium",
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
