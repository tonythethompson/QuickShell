namespace QuickShell.Services;

internal static partial class ShortcutLaunchFormJson
{
    public static string BuildSuggestionPillsBlock()
    {
        // One ActionSet for every slot: CmdPal lays actions out across the card width
        // (wrapping as needed). Hard row breaks of 2–3 pills left a narrow left stack and
        // split same-type groups from BuildSelectablePills' TypeTitle sort.
        var actions = new List<string>(SuggestionPillPresentation.MaxSlots);
        for (var slot = 0; slot < SuggestionPillPresentation.MaxSlots; slot++)
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
                "pillTaskType": "${PillTaskType_{{slot}}}",
                "pillIndex": {{slot}}
              }
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
            {
              "type": "ActionSet",
              "spacing": "Small",
              "actions": [
                {{string.Join(",\n", actions)}}
              ]
            },
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
