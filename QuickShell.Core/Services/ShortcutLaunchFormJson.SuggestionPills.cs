namespace QuickShell.Services;

internal static partial class ShortcutLaunchFormJson
{
    /// <summary>
    /// Builds pill ActionSets for exactly <paramref name="visiblePillCount"/> slots.
    /// CmdPal mishandles ActionSets that mix <c>$when:true</c> and <c>$when:false</c>
    /// actions (often only the first couple render). Do not pad rows with hidden slots.
    /// </summary>
    public static string BuildSuggestionPillsBlock(int visiblePillCount)
    {
        visiblePillCount = Math.Clamp(visiblePillCount, 0, SuggestionPillPresentation.MaxSlots);
        if (visiblePillCount == 0)
        {
            return """
            {
              "type": "Container",
              "spacing": "Small",
              "items": []
            }
            """;
        }

        var pillsPerRow = SuggestionPillPresentation.PillsPerRow;
        var pillRows = new List<string>();
        for (var rowStart = 0; rowStart < visiblePillCount; rowStart += pillsPerRow)
        {
            var actions = new List<string>();
            var rowEnd = Math.Min(rowStart + pillsPerRow, visiblePillCount);
            for (var slot = rowStart; slot < rowEnd; slot++)
            {
                actions.Add($$"""
                {
                  "type": "Action.Submit",
                  "title": "${PillTitle_{{slot}}}",
                  "tooltip": "${PillTooltip_{{slot}}}",
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

    /// <summary>
    /// Matches command-row chrome (full-width text field + Remove) without binding
    /// <c>LaunchCommand_*</c>. Display id is ignored on submit; kind stays OpenInTerminal.
    /// </summary>
    private static string BuildOpenInTerminalInputWithClear(
        int index,
        string openInTerminalLabel,
        string removeTooltip) =>
        AdaptiveCardFormJson.InputWithTrailingActionsRow(
            $$"""
            {
              "type": "Input.Text",
              "id": "LaunchOpenInTerminalDisplay_{{index}}",
              "value": "{{Escape(openInTerminalLabel)}}",
              "tooltip": "Opens a terminal in the workspace folder without running a command."
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
