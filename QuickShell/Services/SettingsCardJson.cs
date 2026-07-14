using Microsoft.CommandPalette.Extensions.Toolkit;
using System.Text.Json;

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
        ToggleSetting(
            id: "showRecents",
            label: "Show recent workspaces",
            help: $"Show up to {QuickShellRecentSettings.EnabledCount} recently used workspaces on the home page.",
            value: enabled,
            saveAction: "saveRecents");

    public static string MultiLaunchTabsToggle(bool singleWindowTabs, bool showWtHint) =>
        ToggleSetting(
            id: "singleWindowTabs",
            label: "Open multiple commands in one Windows Terminal window",
            help: showWtHint
                ? "When supported, extra commands open as tabs in the same window. Mixed elevation or Console Host still opens separate windows."
                : "Requires Windows Terminal as the default terminal application. Console Host always opens separate windows.",
            value: singleWindowTabs,
            saveAction: "saveMultiLaunch");

    /// <summary>
    /// CmdPal Adaptive Cards do not wrap <c>Input.Toggle</c> titles, so long titles overflow
    /// neighboring columns. Put the label in a wrapping TextBlock and keep the toggle title short.
    /// </summary>
    public static string ToggleSetting(
        string id,
        string label,
        string help,
        bool value,
        string saveAction) =>
        $$"""
        {
          "type": "Container",
          "spacing": "None",
          "items": [
            {
              "type": "TextBlock",
              "text": "{{Escape(label)}}",
              "wrap": true,
              "spacing": "None"
            },
            {
              "type": "Input.Toggle",
              "id": "{{Escape(id)}}",
              "title": "Enabled",
              "spacing": "Small",
              "value": "{{(value ? "true" : "false")}}",
              "valueOn": "true",
              "valueOff": "false",
              {{ChangeActionSave(saveAction)}}
            },
            {{SubtleText(help)}}
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
              "width": "stretch",
              "items": [
                {{leftJson}}
              ]
            },
            {
              "type": "Column",
              "width": "stretch",
              "spacing": "Medium",
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

    private static string Escape(string? value) =>
        value is null ? string.Empty : JsonEncodedText.Encode(value).Value;
}
