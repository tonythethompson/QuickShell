using QuickShell.Abstractions;
using QuickShell.Abstractions.Classification;
using QuickShell.Core.Services;

namespace QuickShell.Services;

internal static class ShortcutFormTemplateJson
{
    public const string DisplayNameDefault = "Quick Shell";

    internal sealed class DataPayload
    {
        public string OriginalName { get; init; } = string.Empty;

        public string Name { get; init; } = string.Empty;

        public string Abbreviation { get; init; } = string.Empty;

        public string Directory { get; init; } = string.Empty;

        public string LaunchTarget { get; init; } = "default";

        public string DevServerUrl { get; init; } = string.Empty;

        public bool OpenDevServerOnLaunch { get; init; }

        public string RepoUrl { get; init; } = string.Empty;

        public string CompanionAppPreset { get; init; } = CompanionAppCatalog.PresetNone;

        public string CompanionAppPath { get; init; } = string.Empty;

        public string CompanionAppArguments { get; init; } = string.Empty;

        public IReadOnlyList<CompanionAppFormRow> Companions { get; init; } = [CompanionAppFormRow.Empty()];

        public bool ShowRestoredDraftNote { get; init; }

        public bool ExpandSuggestionPills { get; init; }

        /// <summary>
        /// When true, pill data is omitted so the form can paint before project analysis finishes.
        /// </summary>
        public bool SuggestionScanning { get; init; }

        /// <summary>Non-empty when the last Save failed validation; shown as an attention banner.</summary>
        public string SaveError { get; init; } = string.Empty;
    }

    public static string BuildTemplate(
        string terminalChoices,
        string companionChoices,
        IReadOnlyList<LaunchRowDraft> commands,
        LaunchEditorText launchText,
        string displayName = DisplayNameDefault,
        int companionCount = 1)
    {
        ArgumentNullException.ThrowIfNull(launchText);
        var commandRows = ShortcutLaunchFormJson.BuildCommandRowsJson(commands, terminalChoices, launchText);
        var tipDirectory = Escape(WorkspaceFormTooltips.Directory);
        var tipName = Escape(WorkspaceFormTooltips.Name);
        var tipHomeKeyword = Escape(WorkspaceFormTooltips.HomeKeyword);
        var tipDevServerUrl = Escape(WorkspaceFormTooltips.DevServerUrl);
        var tipDevServerOnLaunch = Escape(WorkspaceFormTooltips.DevServerOnLaunch);
        var tipRepoUrl = Escape(WorkspaceFormTooltips.RepoUrl);
        var suggestionPillsBlock = ShortcutLaunchFormJson.BuildSuggestionPillsBlock();
        var commandsSection = ShortcutLaunchFormJson.BuildCommandsSectionJson(
            commandRows,
            suggestionPillsBlock,
            launchText);
        var companionRows = Enumerable.Range(0, Math.Max(1, companionCount))
            .Select(_ => CompanionAppFormRow.Empty())
            .ToList();
        var companionsSection = CompanionAppFormJson.BuildSection(companionRows, companionChoices);

        return $$"""
        {
          "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
          "type": "AdaptiveCard",
          "version": "1.6",
          "body": [
            {
              "type": "Input.Text",
              "id": "OriginalName",
              "isVisible": false,
              "value": "${OriginalName}"
            },
            {
              "type": "TextBlock",
              "text": "Restored unsaved changes from your last edit. Save or Cancel when you are done.",
              "wrap": true,
              "isSubtle": true,
              "spacing": "Small",
              "$when": "${ShowRestoredDraftNote}"
            },
            {
              "type": "Container",
              "spacing": "Small",
              "style": "attention",
              "$when": "${ShowSaveError}",
              "items": [
                {
                  "type": "TextBlock",
                  "text": "Could not save",
                  "weight": "Bolder",
                  "color": "Attention",
                  "wrap": true,
                  "spacing": "None"
                },
                {
                  "type": "TextBlock",
                  "text": "${SaveError}",
                  "color": "Attention",
                  "wrap": true,
                  "spacing": "Small"
                }
              ]
            },
            {
              "type": "Container",
              "spacing": "Medium",
              "items": [
                {{AdaptiveCardFormJson.FieldLabel("Folder path")}},
                {{AdaptiveCardFormJson.FieldHelp(WorkspaceFormTooltips.DirectoryExample)}},
                {
                  "type": "Container",
                  "spacing": "Small",
                  "items": [
                    {{AdaptiveCardFormJson.InputWithTrailingActionsRow("""
                    {
                      "type": "Input.Text",
                      "id": "Directory",
                      "isRequired": true,
                      "errorMessage": "Folder path is required",
                      "tooltip": "{{tipDirectory}}",
                      "spacing": "None",
                      "value": "${Directory}"
                    }
                    """,
                    $$"""
                    {{AdaptiveCardFormJson.IconSubmitAction(
                        FormActionGlyphs.BrowseLabel,
                        FormActionGlyphs.BrowseFolderTooltip,
                        "browse",
                        "none")}},
                    {{AdaptiveCardFormJson.IconSubmitAction(
                        FormActionGlyphs.PasteLabel,
                        FormActionGlyphs.PastePathTooltip,
                        "paste",
                        "none")}}
                    """,
                    inputVerticalAlignment: "Center",
                    actionVerticalAlignment: "Top")}}
                  ]
                }
              ]
            },
            {{AdaptiveCardFormJson.PairedFieldRow(
                "Name",
                """
                {
                  "type": "Input.Text",
                  "id": "Name",
                  "tooltip": "{{tipName}}",
                  "value": "${Name}"
                }
                """,
                "Home keyword (optional)",
                """
                {
                  "type": "Input.Text",
                  "id": "Abbreviation",
                  "tooltip": "{{tipHomeKeyword}}",
                  "value": "${Abbreviation}"
                }
                """,
                leftWeight: "3",
                rightWeight: "2")}},
            {{AdaptiveCardFormJson.DevServerFieldRow(
                """
                {
                  "type": "Input.Text",
                  "id": "DevServerUrl",
                  "tooltip": "{{tipDevServerUrl}}",
                  "value": "${DevServerUrl}"
                }
                """,
                """
                {
                  "type": "Input.Toggle",
                  "id": "OpenDevServerOnLaunch",
                  "title": "Open in browser",
                  "tooltip": "{{tipDevServerOnLaunch}}",
                  "value": "${OpenDevServerOnLaunch}",
                  "valueOn": "true",
                  "valueOff": "false"
                }
                """,
                WorkspaceFormTooltips.DevServerUrlExample)}},
            {{AdaptiveCardFormJson.FieldGroup("Repository URL (optional)", WorkspaceFormTooltips.RepoUrlExample, """
            {
              "type": "Input.Text",
              "id": "RepoUrl",
              "tooltip": "{{tipRepoUrl}}",
              "value": "${RepoUrl}"
            }
            """)}},
            {{companionsSection}},
            {{commandsSection}}
          ],
          "actions": [
            {
              "type": "Action.Submit",
              "title": "Save workspace",
              "associatedInputs": "auto"
            },
            {
              "type": "Action.Submit",
              "title": "Cancel",
              "tooltip": "Unsaved changes prompt you before leaving.",
              "data": { "action": "cancel" },
              "associatedInputs": "none"
            }
          ]
        }
        """;
    }

    public static string BuildDataJson(
        DataPayload draft,
        IProjectAnalysisService projectAnalysis,
        ICommandSuggestionService commandSuggestions,
        IReadOnlyList<LaunchRowDraft>? commands = null)
    {
        ArgumentNullException.ThrowIfNull(commandSuggestions);
        commands ??= [];
        var commandFields = string.Join(
            ",\n",
            commands.SelectMany((row, index) => new[]
            {
                $"\"LaunchCommand_{index}\": \"{Escape(row.Command)}\"",
                $"\"LaunchKind_{index}\": \"{row.Kind}\"",
                $"\"LaunchLabel_{index}\": \"{Escape(row.Label)}\"",
                $"\"LaunchIsEnabled_{index}\": \"{(row.IsEnabled ? "true" : "false")}\"",
                $"\"LaunchType_{index}\": \"{Escape(TaskTypeCatalog.Normalize(row.TaskType))}\"",
                $"\"LaunchTarget_{index}\": \"{Escape(row.LaunchTarget)}\"",
                $"\"LaunchRunAsAdmin_{index}\": \"{(row.RunAsAdmin ? "true" : "false")}\"",
            }));

        var commandSection = commandFields.Length > 0 ? ",\n" + commandFields : string.Empty;
        var pillFields = BuildPillDataFields(draft, commands, projectAnalysis, commandSuggestions);
        var pillSection = pillFields.Length > 0 ? ",\n" + pillFields : string.Empty;
        var companions = draft.Companions is { Count: > 0 }
            ? draft.Companions
            :
            [
                new CompanionAppFormRow
                {
                    Preset = draft.CompanionAppPreset,
                    Path = draft.CompanionAppPath,
                    Arguments = draft.CompanionAppArguments,
                },
            ];
        var companionFields = string.Join(",\n", CompanionAppFormJson.BuildDataFields(companions, draft.Directory));
        var companionSection = companionFields.Length > 0 ? ",\n" + companionFields : string.Empty;

        return $$"""
        {
          "OriginalName": "{{Escape(draft.OriginalName)}}",
          "Name": "{{Escape(draft.Name)}}",
          "Abbreviation": "{{Escape(draft.Abbreviation)}}",
          "Directory": "{{Escape(draft.Directory)}}",
          "LaunchTarget": "{{Escape(draft.LaunchTarget)}}",
          "DevServerUrl": "{{Escape(draft.DevServerUrl)}}",
          "OpenDevServerOnLaunch": "{{(draft.OpenDevServerOnLaunch ? "true" : "false")}}",
          "RepoUrl": "{{Escape(draft.RepoUrl)}}",
          "ShowRestoredDraftNote": {{(draft.ShowRestoredDraftNote ? "true" : "false")}},
          "ShowSaveError": {{(!string.IsNullOrWhiteSpace(draft.SaveError) ? "true" : "false")}},
          "ShowAddOpenInTerminal": {{(!commands.Any(row => row.Kind == LaunchRowKind.OpenInTerminal) ? "true" : "false")}},
          "SaveError": "{{Escape(draft.SaveError)}}"{{companionSection}}{{commandSection}}{{pillSection}}
        }
        """;
    }

    private static string BuildPillDataFields(
        DataPayload draft,
        IReadOnlyList<LaunchRowDraft> commands,
        IProjectAnalysisService projectAnalysis,
        ICommandSuggestionService commandSuggestions)
    {
        var launchRows = commands.ToList();

        var fields = new List<string>();
        foreach (var entry in SuggestionPillPresentation.BuildDataFields(
                     draft.Directory,
                     launchRows.Select(row => row.Command),
                     projectAnalysis,
                     commandSuggestions,
                     draft.ExpandSuggestionPills,
                     isScanningSuggestions: draft.SuggestionScanning))
        {
            fields.Add(FormatPillDataField(entry.Key, entry.Value));
        }

        return string.Join(",\n", fields);
    }

    private static string FormatPillDataField(string key, string value) =>
        IsBooleanPillField(key)
            ? $"\"{key}\": {(string.Equals(value, "true", StringComparison.Ordinal) ? "true" : "false")}"
            : $"\"{key}\": \"{Escape(value)}\"";

    private static bool IsBooleanPillField(string key) =>
        key is "ShowSuggestionPills"
            or "SuggestionScanning"
            or "ExpandSuggestionPills"
            or "ShowMoreSuggestions"
            or "ShowFewerSuggestions"
        || key.StartsWith("ShowPill_", StringComparison.Ordinal);

    public static string BuildDiscardPromptTemplate() =>
        """
        {
          "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
          "type": "AdaptiveCard",
          "version": "1.6",
          "body": [
            {
              "type": "TextBlock",
              "text": "Unsaved changes",
              "weight": "Bolder",
              "size": "Medium"
            },
            {
              "type": "TextBlock",
              "text": "Save your changes, or discard them and leave?",
              "wrap": true
            }
          ],
          "actions": [
            {
              "type": "Action.Submit",
              "title": "Save and close",
              "data": { "action": "save" },
              "associatedInputs": "none"
            },
            {
              "type": "Action.Submit",
              "title": "Discard",
              "data": { "action": "discard" },
              "associatedInputs": "none"
            }
          ]
        }
        """;

    private static string Escape(string? value)
    {
        var encoded = global::System.Text.Json.JsonSerializer.Serialize(
            value ?? string.Empty,
            global::QuickShell.QuickShellJsonContext.Default.String);
        return encoded.Length >= 2 ? encoded[1..^1] : string.Empty;
    }

    /// <summary>
    /// Choice arrays and command rows must be interpolated in the outer template scope.
    /// Nested raw strings (e.g. FieldGroup input fragments) cannot expand {{tokens}}.
    /// </summary>
    public static void AssertRenderableTemplate(string templateJson)
    {
        if (string.IsNullOrWhiteSpace(templateJson))
        {
            throw new InvalidOperationException("Workspace form template is empty.");
        }

        foreach (var token in new[]
                 {
                     "{{companionChoices}}",
                     "{{terminalChoices}}",
                     "{{commandRows}}",
                     "{{AdaptiveCardFormJson",
                     "{{SettingsCardJson",
                     "{{Escape(",
                 })
        {
            if (templateJson.Contains(token, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Workspace form template contains unexpanded build token '{token}'.");
            }
        }
    }
}
