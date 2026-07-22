using System.Text.Json;

namespace QuickShell.Services;

/// <summary>Adaptive Card body + data fields for multi-companion form rows.</summary>
internal static class CompanionAppFormJson
{
    public static string BuildSection(
        IReadOnlyList<CompanionAppFormRow> companions,
        string companionChoicesJson)
    {
        if (companions.Count == 0)
        {
            companions = [CompanionAppFormRow.Empty()];
        }

        var blocks = new List<string>();
        for (var i = 0; i < companions.Count; i++)
        {
            blocks.Add(BuildRow(i, companions.Count, companionChoicesJson));
        }

        return string.Join(",\n            ", blocks);
    }

    public static IEnumerable<string> BuildDataFields(
        IReadOnlyList<CompanionAppFormRow> companions,
        string directory)
    {
        if (companions.Count == 0)
        {
            companions = [CompanionAppFormRow.Empty()];
        }

        for (var i = 0; i < companions.Count; i++)
        {
            var row = companions[i];
            var preset = CompanionAppCatalog.ToFormPresetValue(row.Preset, row.Path);
            yield return $"\"CompanionAppPreset_{i}\": \"{Escape(preset)}\"";
            yield return $"\"CompanionAppPresetTooltip_{i}\": \"{Escape(BuildPresetTooltip(row.Path))}\"";
            yield return $"\"ShowCompanionBrowseRequired_{i}\": {(CompanionAppCatalog.ShouldShowBrowseRequiredPrompt(row.Preset, row.Path) ? "true" : "false")}";
            yield return $"\"CompanionBrowseRequiredMessage_{i}\": \"{Escape(CompanionAppCatalog.BrowseRequiredMessage)}\"";
            yield return $"\"ShowCompanionPathWarning_{i}\": {(CompanionAppCatalog.ShouldShowPathWarning(row.Preset, row.Path) ? "true" : "false")}";
            yield return $"\"CompanionPathWarning_{i}\": \"{Escape(CompanionAppCatalog.BuildPathWarning(row.Preset, row.Path))}\"";
            yield return $"\"ShowCompanionArguments_{i}\": {(CompanionAppArgumentValidation.ShouldShowArgumentsField(row.Preset, row.Path) ? "true" : "false")}";
            yield return $"\"CompanionAppArguments_{i}\": \"{Escape(row.Arguments)}\"";
            yield return $"\"CompanionArgumentPlaceholder_{i}\": \"{Escape(CompanionAppArgumentValidation.GetArgumentPlaceholder(row.Preset, row.Path))}\"";
            yield return $"\"CompanionArgumentTooltip_{i}\": \"{Escape(CompanionAppArgumentValidation.GetArgumentTooltip(row.Preset, row.Path))}\"";
            var warning = CompanionAppArgumentValidation.BuildArgumentWarning(
                row.Preset,
                row.Path,
                row.Arguments,
                directory);
            yield return $"\"ShowCompanionArgumentWarning_{i}\": {(warning is not null ? "true" : "false")}";
            yield return $"\"CompanionArgumentWarning_{i}\": \"{Escape(warning ?? string.Empty)}\"";
            yield return $"\"ShowCompanionAdd_{i}\": {(i == companions.Count - 1 && CompanionAppFormEditor.CanAdd(companions) ? "true" : "false")}";
            yield return $"\"ShowCompanionRemove_{i}\": {(companions.Count > 1 ? "true" : "false")}";
        }
    }

    private static string BuildPresetTooltip(string? path) =>
        CompanionAppCatalog.ShouldShowExecutablePath(path)
            ? path!.Trim()
            : WorkspaceFormTooltips.CompanionAppPreset;

    private static string BuildRow(int index, int totalCount, string companionChoicesJson)
    {
        var browseTitle = Escape(CompanionAppCatalog.BrowseActionTitle);
        var label = index == 0 ? "Companion app" : $"Companion app {index + 1}";

        var actions = new List<string>
        {
            $$"""
            {
              "type": "Action.Submit",
              "title": "{{browseTitle}}",
              "tooltip": "Pick any installed application.",
              "data": { "action": "{{CompanionAppFormEditor.BrowseAction}}", "companionIndex": {{index}} },
              "associatedInputs": "auto"
            }
            """,
        };

        // Keep + on the same row as the picker (last row only, while under the cap).
        if (index == totalCount - 1 && totalCount < CompanionAppFormEditor.MaxCount)
        {
            actions.Add(
                $$"""
                {
                  "type": "Action.Submit",
                  "title": "+",
                  "tooltip": "{{Escape(CompanionAppFormEditor.AddTooltip)}}",
                  "data": { "action": "{{CompanionAppFormEditor.AddAction}}" },
                  "associatedInputs": "auto"
                }
                """);
        }

        if (totalCount > 1)
        {
            actions.Add(
                $$"""
                {
                  "type": "Action.Submit",
                  "title": "−",
                  "tooltip": "{{Escape(CompanionAppFormEditor.RemoveTooltip)}}",
                  "data": { "action": "{{CompanionAppFormEditor.RemoveAction}}", "companionIndex": {{index}} },
                  "associatedInputs": "auto"
                }
                """);
        }

        var actionList = string.Join(",\n                        ", actions);

        return $$"""
        {
          "type": "Container",
          "spacing": "Medium",
          "items": [
            {{AdaptiveCardFormJson.FieldLabel(label)}},
            {
              "type": "ColumnSet",
              "spacing": "Small",
              "columns": [
                {
                  "type": "Column",
                  "width": "stretch",
                  "verticalContentAlignment": "Center",
                  "items": [
                    {
                      "type": "Input.ChoiceSet",
                      "id": "CompanionAppPreset_{{index}}",
                      "style": "compact",
                      "tooltip": "${CompanionAppPresetTooltip_{{index}}}",
                      "value": "${CompanionAppPreset_{{index}}}",
                      "choices": {{companionChoicesJson}}
                    }
                  ]
                },
                {
                  "type": "Column",
                  "$when": "${ShowCompanionArguments_{{index}}}",
                  "width": "2",
                  "verticalContentAlignment": "Center",
                  "items": [
                    {{AdaptiveCardFormJson.InlineCompanionArgumentsInput(index)}}
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
                        {{actionList}}
                      ]
                    }
                  ]
                }
              ]
            },
            {
              "type": "TextBlock",
              "$when": "${ShowCompanionBrowseRequired_{{index}}}",
              "text": "${CompanionBrowseRequiredMessage_{{index}}}",
              "color": "Attention",
              "wrap": true,
              "spacing": "Small"
            },
            {
              "type": "TextBlock",
              "$when": "${ShowCompanionPathWarning_{{index}}}",
              "text": "${CompanionPathWarning_{{index}}}",
              "color": "Attention",
              "wrap": true,
              "spacing": "Small"
            },
            {
              "type": "TextBlock",
              "$when": "${ShowCompanionArgumentWarning_{{index}}}",
              "text": "${CompanionArgumentWarning_{{index}}}",
              "color": "Attention",
              "wrap": true,
              "spacing": "Small"
            }
          ]
        }
        """;
    }

    private static string Escape(string value)
    {
        var encoded = JsonSerializer.Serialize(
            value,
            global::QuickShell.QuickShellJsonContext.Default.String);
        return encoded[1..^1];
    }
}
