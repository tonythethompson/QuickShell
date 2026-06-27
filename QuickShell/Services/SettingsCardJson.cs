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

    public static string FieldLabel(string label) =>
        $$"""
        {
          "type": "TextBlock",
          "text": "{{Escape(label)}}",
          "weight": "Bolder",
          "wrap": true,
          "spacing": "None"
        }
        """;

    public static string FieldHelp(string text) =>
        $$"""
        {
          "type": "TextBlock",
          "text": "{{Escape(text)}}",
          "wrap": true,
          "isSubtle": true,
          "size": "Small",
          "spacing": "None"
        }
        """;

    public static string FieldGroup(string label, string help, string inputElementJson) =>
        $$"""
        {
          "type": "Container",
          "spacing": "Medium",
          "items": [
            {
              "type": "Container",
              "spacing": "Small",
              "items": [
                {{FieldLabel(label)}},
                {{FieldHelp(help)}},
                {{inputElementJson}}
              ]
            }
          ]
        }
        """;

    public static string BuildChoicesJson(IEnumerable<ChoiceSetSetting.Choice> choices) =>
        string.Join(",\n", choices.Select(choice =>
            $$"""{ "title": "{{Escape(choice.Title)}}", "value": "{{Escape(choice.Value)}}" }"""));

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
