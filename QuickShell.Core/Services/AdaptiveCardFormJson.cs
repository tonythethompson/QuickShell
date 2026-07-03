namespace QuickShell.Services;

/// <summary>
/// Shared Adaptive Card field fragments for Command Palette forms.
/// </summary>
internal static class AdaptiveCardFormJson
{
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

    private static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
