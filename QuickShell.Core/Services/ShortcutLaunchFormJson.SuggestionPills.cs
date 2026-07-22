namespace QuickShell.Services;

internal static partial class ShortcutLaunchFormJson
{
    // 2 per row instead of 3 -- gives each pill more horizontal room for longer commands
    // before DisplayTitleMaxLength truncates them.
    private const int PillsPerRow = 2;

    public static string BuildSuggestionPillsBlock()
    {
        var pillRows = new List<string>();
        for (var rowStart = 0; rowStart < SuggestionPillPresentation.MaxSlots; rowStart += PillsPerRow)
        {
            var actions = new List<string>();
            for (var slot = rowStart; slot < rowStart + PillsPerRow && slot < SuggestionPillPresentation.MaxSlots; slot++)
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
            {{AdaptiveCardFormJson.FieldLabel("Suggested commands", "Click a pill to add.", bold: false)}},
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

    private static string BuildCommandInputWithClear(int index, string removeTooltip) =>
        BuildCommandInputWithClear(index, literalValue: null, removeTooltip);

    private static string BuildCommandInputWithClear(int index, string? literalValue, string removeTooltip) =>
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
            BuildRemoveLaunchAction(index, removeTooltip));

    private static string BuildRemoveLaunchAction(int index, string removeTooltip) =>
        AdaptiveCardFormJson.IconSubmitAction(
            FormActionGlyphs.RemoveLabel,
            removeTooltip,
            "removeLaunch",
            "auto",
            dataJson: $$"""{ "action": "removeLaunch", "launchIndex": {{index}} }""");
}
