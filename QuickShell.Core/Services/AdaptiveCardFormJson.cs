namespace QuickShell.Services;

/// <summary>
/// Shared Adaptive Card field fragments for Command Palette forms.
/// </summary>
internal static class AdaptiveCardFormJson
{
    public const string NarrowInputWidth = "1";
    public const string NarrowSpacerWidth = "4";
    public const string MediumInputWidth = "2";
    public const string MediumSpacerWidth = "3";
    private const string LabelInputSpacing = "Small";
    private const string SectionSpacing = "Medium";

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

    public static string InputAfterLabel(string inputJson) =>
        $$"""
        {
          "type": "Container",
          "spacing": "{{LabelInputSpacing}}",
          "items": [
            {{inputJson}}
          ]
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
          "spacing": "{{LabelInputSpacing}}"
        }
        """;

    public static string FullWidthColumn(string elementJson) =>
        $$"""
        {
          "type": "ColumnSet",
          "spacing": "Small",
          "columns": [
            {
              "type": "Column",
              "width": "stretch",
              "items": [
                {{elementJson}}
              ]
            }
          ]
        }
        """;

    public static string PairedFieldRow(
        string leftLabel,
        string leftInputJson,
        string rightLabel,
        string rightInputJson,
        string leftWeight = "3",
        string rightWeight = "1",
        string? leftHelp = null,
        string? rightHelp = null)
    {
        var leftHelpBlock = OptionalFieldHelpEntry(leftHelp);
        var rightHelpBlock = OptionalFieldHelpEntry(rightHelp);

        return $$"""
        {
          "type": "Container",
          "spacing": "{{SectionSpacing}}",
          "items": [
            {
              "type": "ColumnSet",
              "spacing": "Small",
              "columns": [
                {
                  "type": "Column",
                  "width": "{{leftWeight}}",
                  "items": [
                    {{FieldLabel(leftLabel)}},
                    {{leftHelpBlock}}
                    {{InputAfterLabel(leftInputJson)}}
                  ]
                },
                {
                  "type": "Column",
                  "width": "{{rightWeight}}",
                  "items": [
                    {{FieldLabel(rightLabel)}},
                    {{rightHelpBlock}}
                    {{InputAfterLabel(rightInputJson)}}
                  ]
                }
              ]
            }
          ]
        }
        """;
    }

    public static string DevServerFieldRow(string urlInputJson, string toggleInputJson, string? exampleHelp = null)
    {
        var helpBlock = OptionalFieldHelpEntry(exampleHelp);

        return $$"""
        {
          "type": "Container",
          "spacing": "{{SectionSpacing}}",
          "items": [
            {{FieldLabel("Dev server URL (optional)")}},
            {{helpBlock}}
            {
              "type": "ColumnSet",
              "spacing": "{{LabelInputSpacing}}",
              "columns": [
                {
                  "type": "Column",
                  "width": "stretch",
                  "verticalContentAlignment": "Center",
                  "items": [
                    {{urlInputJson}}
                  ]
                },
                {
                  "type": "Column",
                  "width": "auto",
                  "verticalContentAlignment": "Center",
                  "items": [
                    {{toggleInputJson}}
                  ]
                }
              ]
            }
          ]
        }
        """;
    }

    public static string InputWithTrailingActionsRow(string inputJson, string actionsJson) =>
        $$"""
        {
          "type": "ColumnSet",
          "spacing": "Small",
          "columns": [
            {
              "type": "Column",
              "width": "stretch",
              "verticalContentAlignment": "Bottom",
              "items": [
                {{inputJson}}
              ]
            },
            {
              "type": "Column",
              "width": "auto",
              "verticalContentAlignment": "Bottom",
              "items": [
                {
                  "type": "ActionSet",
                  "spacing": "None",
                  "actions": [
                    {{actionsJson}}
                  ]
                }
              ]
            }
          ]
        }
        """;

    public static string MatchedPrimaryWidth(string elementJson) =>
        PairRow(elementJson, string.Empty, "3", "1");

    public static string FieldWithActionRow(
        string label,
        string inputJson,
        string actionJson,
        string? help = null)
    {
        var helpBlock = OptionalFieldHelpEntry(help);

        return $$"""
        {
          "type": "Container",
          "spacing": "{{SectionSpacing}}",
          "items": [
            {{FieldLabel(label)}},
            {{helpBlock}}
            {
              "type": "ColumnSet",
              "spacing": "{{LabelInputSpacing}}",
              "columns": [
                {
                  "type": "Column",
                  "width": "stretch",
                  "verticalContentAlignment": "Center",
                  "items": [
                    {{inputJson}}
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
                        {{actionJson}}
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
    public static string PairRow(
        string leftColumnItems,
        string rightColumnItems,
        string leftWeight = "1",
        string rightWeight = "1") =>
        $$"""
        {
          "type": "ColumnSet",
          "spacing": "Small",
          "columns": [
            {
              "type": "Column",
              "width": "{{leftWeight}}",
              "items": [
                {{leftColumnItems}}
              ]
            },
            {
              "type": "Column",
              "width": "{{rightWeight}}",
              "items": [
                {{rightColumnItems}}
              ]
            }
          ]
        }
        """;

    public static string MediumWidthColumn(
        string elementJson,
        string inputWidth = MediumInputWidth,
        string spacerWidth = MediumSpacerWidth) =>
        $$"""
        {
          "type": "ColumnSet",
          "spacing": "Small",
          "columns": [
            {
              "type": "Column",
              "width": "{{inputWidth}}",
              "items": [
                {{elementJson}}
              ]
            },
            {
              "type": "Column",
              "width": "{{spacerWidth}}",
              "items": []
            }
          ]
        }
        """;

    public static string IconSubmitAction(
        string glyph,
        string tooltip,
        string action,
        string associatedInputs = "auto",
        string? style = null,
        string? dataJson = null,
        string? whenExpression = null)
    {
        var styleLine = string.IsNullOrWhiteSpace(style)
            ? string.Empty
            : ", \"style\": \"" + Escape(style) + "\"";
        var whenLine = string.IsNullOrWhiteSpace(whenExpression)
            ? string.Empty
            : ", \"$when\": \"" + Escape(whenExpression) + "\"";
        var data = dataJson ?? $$"""{ "action": "{{Escape(action)}}" }""";
        return $$"""
        {
          "type": "Action.Submit",
          "title": "{{Escape(glyph)}}",
          "tooltip": "{{Escape(tooltip)}}",
          "associatedInputs": "{{associatedInputs}}"{{styleLine}}{{whenLine}},
          "data": {{data}}
        }
        """;
    }

    public static string ActionColumn(string actionJson, string verticalAlignment = "Center") =>
        $$"""
        {
          "type": "Column",
          "width": "auto",
          "verticalContentAlignment": "{{verticalAlignment}}",
          "items": [
            {
              "type": "ActionSet",
              "spacing": "None",
              "actions": [
                {{actionJson}}
              ]
            }
          ]
        }
        """;

    public static string FieldGroup(string label, string? help, string inputElementJson)
    {
        var helpBlock = OptionalFieldHelpEntry(help);

        return $$"""
        {
          "type": "Container",
          "spacing": "{{SectionSpacing}}",
          "items": [
            {
              "type": "Container",
              "spacing": "None",
              "items": [
                {{FieldLabel(label)}},
                {{helpBlock}}
                {{InputAfterLabel(FullWidthColumn(inputElementJson))}}
              ]
            }
          ]
        }
        """;
    }

    private static string OptionalFieldHelpEntry(string? help) =>
        string.IsNullOrWhiteSpace(help)
            ? string.Empty
            : FieldHelp(help) + ",\n                ";

    /// <summary>
    /// Single-line companion arguments input in a narrow column.
    /// Placeholder, value, and tooltip bind from form data JSON.
    /// </summary>
    public static string NarrowCompanionArgumentsInput() =>
        MediumWidthColumn(
            $$"""
            {
              "type": "Input.Text",
              "id": "CompanionAppArguments",
              "placeholder": "${CompanionArgumentPlaceholder}",
              "value": "${CompanionAppArguments}",
              "tooltip": "${CompanionArgumentTooltip}",
              "maxLength": {{ShortcutValidation.MaxCompanionAppArgumentsLength}}
            }
            """,
            NarrowInputWidth,
            NarrowSpacerWidth);

    private static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
