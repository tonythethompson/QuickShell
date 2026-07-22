namespace QuickShell.Services;

/// <summary>
/// Shared Adaptive Card field fragments for Command Palette forms.
/// </summary>
internal static class AdaptiveCardFormJson
{
    private const string LabelInputSpacing = "Small";
    private const string SectionSpacing = "Medium";

    public static string FieldLabel(string label, string? tooltip = null, bool bold = true, bool wrap = true)
    {
        var tooltipLine = string.IsNullOrWhiteSpace(tooltip)
            ? string.Empty
            : $",\n          \"tooltip\": \"{Escape(tooltip)}\" ";
        var weightLine = bold
            ? ",\n          \"weight\": \"Bolder\" "
            : string.Empty;
        return $$"""
        {
          "type": "TextBlock",
          "text": "{{Escape(label)}}",
          "wrap": {{(wrap ? "true" : "false")}},
          "spacing": "None"{{weightLine}}{{tooltipLine}}
        }
        """;
    }

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
        string rightWeight = "2",
        string? leftHelp = null,
        string? rightHelp = null)
    {
        var leftHelpBlock = OptionalFieldHelpEntry(leftHelp);
        var rightHelpBlock = OptionalFieldHelpEntry(rightHelp);
        // Labels do not wrap: wrapping "Home keyword (optional)" grows the column and can
        // force Adaptive Card hosts to stack the pair onto two rows when the window is narrow.
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
                    {{FieldLabel(leftLabel, wrap: false)}},
                    {{leftHelpBlock}}
                    {{InputAfterLabel(leftInputJson)}}
                  ]
                },
                {
                  "type": "Column",
                  "width": "{{rightWeight}}",
                  "items": [
                    {{FieldLabel(rightLabel, wrap: false)}},
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

    /// <summary>
    /// Input + trailing action buttons. One <see cref="ActionColumn"/> per button.    /// Default action alignment is <c>Top</c>: CmdPal ActionSets carry bottom chrome that
    /// makes Center/Bottom sit under the text box. Label belongs above this row, not on the input.
    /// </summary>
    public static string InputWithTrailingActionsRow(
        string inputJson,
        string actionsJson,
        string inputVerticalAlignment = "Center",
        string actionVerticalAlignment = "Top")
    {
        var actions = SplitTopLevelJsonObjects(actionsJson);
        if (actions.Count == 0)
        {
            return inputJson;
        }

        var actionColumns = string.Join(
            ",\n",
            actions.Select(action => ActionColumn(action, actionVerticalAlignment)));
        return $$"""
        {
          "type": "ColumnSet",
          "spacing": "None",
          "columns": [
            {
              "type": "Column",
              "width": "stretch",
              "verticalContentAlignment": "{{Escape(inputVerticalAlignment)}}",
              "spacing": "None",
              "items": [
                {{inputJson}}
              ]
            },
            {{actionColumns}}
          ]
        }
        """;
    }

    private static List<string> SplitTopLevelJsonObjects(string jsonList)
    {
        var results = new List<string>();
        if (string.IsNullOrWhiteSpace(jsonList))
        {
            return results;
        }

        var depth = 0;
        var start = -1;
        var inString = false;
        var escape = false;

        for (var i = 0; i < jsonList.Length; i++)
        {
            var ch = jsonList[i];
            if (inString)
            {
                if (escape)
                {
                    escape = false;
                    continue;
                }

                if (ch == '\\')
                {
                    escape = true;
                    continue;
                }

                if (ch == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (ch == '"')
            {
                inString = true;
                continue;
            }

            if (ch == '{')
            {
                if (depth == 0)
                {
                    start = i;
                }

                depth++;
                continue;
            }

            if (ch == '}')
            {
                depth--;
                if (depth == 0 && start >= 0)
                {
                    results.Add(jsonList[start..(i + 1)].Trim());
                    start = -1;
                }
            }
        }

        return results;
    }

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
    /// Compact companion arguments input for the picker row (beside the preset dropdown).
    /// Placeholder, value, and tooltip bind from form data JSON.
    /// </summary>
    public static string InlineCompanionArgumentsInput(int index = 0) =>
        $$"""
        {
          "type": "Input.Text",
          "id": "CompanionAppArguments_{{index}}",
          "placeholder": "${CompanionArgumentPlaceholder_{{index}}}",
          "value": "${CompanionAppArguments_{{index}}}",
          "tooltip": "${CompanionArgumentTooltip_{{index}}}",
          "maxLength": {{ShortcutValidation.MaxCompanionAppArgumentsLength}},
          "style": "text"
        }
        """;

    private static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
