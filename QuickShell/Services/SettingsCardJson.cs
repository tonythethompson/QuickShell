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
    public static string SectionHeader(string title, string spacing = "None", string? tooltip = null)
    {
        var tooltipLine = string.IsNullOrWhiteSpace(tooltip)
            ? string.Empty
            : $",\n          \"tooltip\": \"{Escape(tooltip)}\"";
        return $$"""
        {
          "type": "TextBlock",
          "text": "{{Escape(title)}}",
          "weight": "Bolder",
          "size": "Medium",
          "spacing": "{{spacing}}"{{tooltipLine}}
        }
        """;
    }

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
          "type": "Input.Toggle",
          "id": "showRecents",
          "title": "Show recent workspaces",
          "tooltip": "Up to {{QuickShellRecentSettings.EnabledCount}} on the home list.",
          "spacing": "None",
          "value": "{{(enabled ? "true" : "false")}}",
          "valueOn": "true",
          "valueOff": "false",
          {{ChangeActionSave("previewSettings")}}
        }
        """;

    public static string MultiLaunchTabsToggle(bool singleWindowTabs, bool showWtHint) =>
        $$"""
        {
          "type": "Input.Toggle",
          "id": "singleWindowTabs",
          "title": "Use tabs when possible",
          "tooltip": "{{Escape(showWtHint
              ? "Open multiple commands as tabs in one window when possible. Mixed elevation still opens separate windows."
              : "Open multiple commands as tabs in one window. Requires Windows Terminal; Console Host always opens separate windows.")}}",
          "spacing": "None",
          "value": "{{(singleWindowTabs ? "true" : "false")}}",
          "valueOn": "true",
          "valueOff": "false",
          {{ChangeActionSave("previewSettings")}}
        }
        """;

    public static string BlockDirtyBranchToggle(bool enabled) =>
        $$"""
        {
          "type": "Input.Toggle",
          "id": "blockDirtyBranchSwitch",
          "title": "Block dirty branch switches",
          "tooltip": "Stops launch or branch switch when the target differs and the tree is dirty. Use git worktree for two branches at once.",
          "spacing": "None",
          "value": "{{(enabled ? "true" : "false")}}",
          "valueOn": "true",
          "valueOff": "false",
          {{ChangeActionSave("previewSettings")}}
        }
        """;

    /// <summary>Bottom-right Cancel + Save &amp; close row for the settings page.</summary>
    public static string SettingsFooterActions() =>
        $$"""
        {
          "type": "ColumnSet",
          "spacing": "Large",
          "columns": [
            {
              "type": "Column",
              "width": "1",
              "items": []
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
                      "title": "Cancel",
                      "tooltip": "Discard unsaved settings and close.",
                      "associatedInputs": "none",
                      "data": { "action": "cancelSettings" }
                    }
                  ]
                }
              ]
            },
            {
              "type": "Column",
              "width": "auto",
              "verticalContentAlignment": "Center",
              "spacing": "Small",
              "items": [
                {
                  "type": "ActionSet",
                  "spacing": "None",
                  "actions": [
                    {
                      "type": "Action.Submit",
                      "title": "Save & close",
                      "tooltip": "Save all settings and close.",
                      "associatedInputs": "auto",
                      "data": { "action": "saveAndCloseSettings" }
                    }
                  ]
                }
              ]
            }
          ]
        }
        """;

    public static string TwoColumnSection(string leftJson, string rightJson) =>
        $$"""
        {
          "type": "ColumnSet",
          "spacing": "Medium",
          "columns": [
            {
              "type": "Column",
              "width": "1",
              "items": [
                {{leftJson}}
              ]
            },
            {
              "type": "Column",
              "width": "1",
              "items": [
                {{rightJson}}
              ]
            }
          ]
        }
        """;

    public static string ThreeColumnSection(string leftJson, string middleJson, string rightJson, string spacing = "None") =>
        $$"""
        {
          "type": "ColumnSet",
          "spacing": "{{spacing}}",
          "columns": [
            {
              "type": "Column",
              "width": "1",
              "items": [
                {{leftJson}}
              ]
            },
            {
              "type": "Column",
              "width": "1",
              "items": [
                {{middleJson}}
              ]
            },
            {
              "type": "Column",
              "width": "1",
              "items": [
                {{rightJson}}
              ]
            }
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

    /// <summary>Actions only (no subtitle). Optional short header with tooltip for hover detail.</summary>
    public static string TransferActionsBlock(
        string actionsJson,
        string topSpacing = "Small",
        string? header = null,
        string? tooltip = null)
    {
        var items = new List<string>();
        if (!string.IsNullOrWhiteSpace(header))
        {
            items.Add(SectionHeader(header, spacing: "None", tooltip: tooltip));
        }

        items.Add(actionsJson);
        var itemsJson = string.Join(",\n            ", items);
        return $$"""
        {
          "type": "Container",
          "spacing": "{{topSpacing}}",
          "items": [
            {{itemsJson}}
          ]
        }
        """;
    }

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
