namespace QuickShell.Services;

internal static partial class ShortcutLaunchFormJson
{
    public static string BuildSuggestionPillsBlock()
    {
        var pillRows = new List<string>();
        for (var rowStart = 0; rowStart < SuggestionPillPresentation.MaxSlots; rowStart += 3)
        {
            var actions = new List<string>();
            for (var slot = rowStart; slot < rowStart + 3 && slot < SuggestionPillPresentation.MaxSlots; slot++)
            {
                actions.Add($$"""
                {
                  "type": "Action.Submit",
                  "title": "${PillTitle_{{slot}}}",
                  "tooltip": "${PillTooltip_{{slot}}}",
                  "$when": "${ShowPill_{{slot}}}",
                  "associatedInputs": "auto",
                  "data": {
                    "action": "addSuggestedCommand",
                    "pillCommand": "${PillCommand_{{slot}}}",
                    "pillTaskType": "${PillTaskType_{{slot}}}"
                  }
                }
                """);
            }

            pillRows.Add($$"""
            {
              "type": "ActionSet",
              "spacing": "Small",
              "actions": [
                {{string.Join(",\n", actions)}}
              ]
            }
            """);
        }

        return $$"""
        {
          "type": "Container",
          "spacing": "Small",
          "$when": "${ShowSuggestionPills}",
          "items": [
            {{AdaptiveCardFormJson.FieldLabel(CommandSuggestionService.FieldLabel)}},
            {{AdaptiveCardFormJson.FieldHelp(CommandSuggestionService.FieldHelp)}},
            {{string.Join(",\n", pillRows)}},
            {
              "type": "ActionSet",
              "spacing": "Small",
              "$when": "${ShowMoreSuggestions}",
              "actions": [
                {
                  "type": "Action.Submit",
                  "title": "Show more suggestions",
                  "associatedInputs": "auto",
                  "data": { "action": "expandSuggestionPills" }
                }
              ]
            },
            {
              "type": "ActionSet",
              "spacing": "Small",
              "$when": "${ShowFewerSuggestions}",
              "actions": [
                {
                  "type": "Action.Submit",
                  "title": "Show fewer suggestions",
                  "associatedInputs": "auto",
                  "data": { "action": "collapseSuggestionPills" }
                }
              ]
            }
          ]
        }
        """;
    }

    private static string BuildCommandInputWithClear(int index) =>
        BuildCommandInputWithClear(index, literalValue: null);

    private static string BuildCommandInputWithClear(int index, string? literalValue) =>
        AdaptiveCardFormJson.InputWithTrailingActionsRow(
            literalValue is null
                ? $$"""
                {
                  "type": "Input.Text",
                  "id": "LaunchCommand_{{index}}",
                  "value": "${LaunchCommand_{{index}}}"
                }
                """
                : $$"""
                {
                  "type": "Input.Text",
                  "id": "LaunchCommand_{{index}}",
                  "value": "{{literalValue}}"
                }
                """,
            AdaptiveCardFormJson.IconSubmitAction(
                FormActionGlyphs.RemoveLabel,
                FormActionGlyphs.ClearCommandTooltip,
                "clearLaunch",
                "auto",
                dataJson: $$"""{ "action": "clearLaunch", "launchIndex": {{index}} }""",
                whenExpression: "${ShowClearLaunch_" + index + "}"));
}
