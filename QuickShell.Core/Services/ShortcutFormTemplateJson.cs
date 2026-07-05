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

        public bool ShowRestoredDraftNote { get; init; }

        public bool RunAsAdmin { get; init; }

        public bool ShowTaskTypePicker { get; init; }

        public string TaskTypePicker { get; init; } = TaskTypeCatalog.None;
    }

    public static string BuildTemplate(
        string terminalChoices,
        string companionChoices,
        IReadOnlyList<(string Command, string TaskType, string LaunchTarget)> commands,
        string taskTypePickerChoices,
        string displayName = DisplayNameDefault)
    {
        var commandRows = ShortcutLaunchFormJson.BuildCommandRowsJson(commands, terminalChoices);
        var tipDirectory = Escape(WorkspaceFormTooltips.Directory);
        var tipName = Escape(WorkspaceFormTooltips.Name);
        var tipHomeKeyword = Escape(WorkspaceFormTooltips.HomeKeyword);
        var tipDevServerUrl = Escape(WorkspaceFormTooltips.DevServerUrl);
        var tipDevServerOnLaunch = Escape(WorkspaceFormTooltips.DevServerOnLaunch);
        var tipRepoUrl = Escape(WorkspaceFormTooltips.RepoUrl);
        var tipCompanionPreset = Escape(WorkspaceFormTooltips.CompanionAppPreset);
        var tipTaskTypePicker = Escape(WorkspaceFormTooltips.TaskTypePicker);
        var tipRunAsAdmin = Escape(WorkspaceFormTooltips.RunAsAdmin);
        var browseCompanionTitle = Escape(CompanionAppCatalog.BrowseActionTitle);
        var taskTypePickerBlock = ShortcutLaunchFormJson.BuildTaskTypePickerBlock($$"""
                {
                  "type": "Input.ChoiceSet",
                  "id": "TaskTypePicker",
                  "style": "compact",
                  "value": "${TaskTypePicker}",
                  "tooltip": "{{tipTaskTypePicker}}",
                  "choices": {{taskTypePickerChoices}}
                }
                """);
        var commandsSection = ShortcutLaunchFormJson.BuildCommandsSectionJson(
            commandRows,
            taskTypePickerBlock);

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
              "spacing": "Medium",
              "items": [
                {{AdaptiveCardFormJson.FieldLabel("Folder path")}},
                {{AdaptiveCardFormJson.FieldHelp(WorkspaceFormTooltips.DirectoryExample)}},
                {{AdaptiveCardFormJson.InputAfterLabel(AdaptiveCardFormJson.InputWithTrailingActionsRow("""
                {
                  "type": "Input.Text",
                  "id": "Directory",
                  "isRequired": true,
                  "errorMessage": "Folder path is required",
                  "tooltip": "{{tipDirectory}}",
                  "value": "${Directory}"
                }
                """,
                $$"""
                {{AdaptiveCardFormJson.IconSubmitAction(
                    FormActionGlyphs.FolderOpen,
                    FormActionGlyphs.BrowseFolderTooltip,
                    "browse",
                    "none")}},
                {{AdaptiveCardFormJson.IconSubmitAction(
                    FormActionGlyphs.Paste,
                    FormActionGlyphs.PastePathTooltip,
                    "paste",
                    "none")}}
                """))}}
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
                """)}},
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
            {{AdaptiveCardFormJson.FieldWithActionRow(
                "App preset",
                $$"""
                {
                  "type": "Input.ChoiceSet",
                  "id": "CompanionAppPreset",
                  "style": "compact",
                  "tooltip": "{{tipCompanionPreset}}",
                  "value": "${CompanionAppPreset}",
                  "choices": {{companionChoices}}
                }
                """,
                $$"""
                {
                  "type": "Action.Submit",
                  "title": "{{browseCompanionTitle}}",
                  "tooltip": "Pick any installed application.",
                  "data": { "action": "browseCompanionApp" },
                  "associatedInputs": "auto"
                }
                """)}},
            {
              "type": "TextBlock",
              "$when": "${ShowCompanionBrowseRequired}",
              "text": "${CompanionBrowseRequiredMessage}",
              "color": "Attention",
              "wrap": true,
              "spacing": "Small"
            },
            {
              "type": "Container",
              "$when": "${ShowCompanionExecutablePath}",
              "spacing": "Small",
              "items": [
                {{AdaptiveCardFormJson.FieldLabel("Executable")}},
                {
                  "type": "TextBlock",
                  "text": "${CompanionAppPathDisplay}",
                  "wrap": true
                }
              ]
            },
            {
              "type": "TextBlock",
              "$when": "${ShowCompanionPathWarning}",
              "text": "${CompanionPathWarning}",
              "color": "Attention",
              "wrap": true,
              "spacing": "Small"
            },
            {
              "type": "Container",
              "$when": "${ShowCompanionArguments}",
              "spacing": "Medium",
              "items": [
                {{AdaptiveCardFormJson.FieldLabel(CompanionAppArgumentValidation.FieldLabel)}},
                {{AdaptiveCardFormJson.InputAfterLabel(AdaptiveCardFormJson.NarrowCompanionArgumentsInput())}},
                {
                  "type": "TextBlock",
                  "$when": "${ShowCompanionArgumentWarning}",
                  "text": "${CompanionArgumentWarning}",
                  "color": "Attention",
                  "wrap": true,
                  "spacing": "Small"
                }
              ]
            },
            {{commandsSection}},
            {{AdaptiveCardFormJson.FieldGroup("Administrator", help: null, """
            {
              "type": "Input.Toggle",
              "id": "RunAsAdmin",
              "title": "Always run as administrator",
              "tooltip": "{{tipRunAsAdmin}}",
              "value": "${RunAsAdmin}",
              "valueOn": "true",
              "valueOff": "false"
            }
            """)}}
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
        IReadOnlyList<(string Command, string TaskType, string LaunchTarget)>? commands = null)
    {
        commands ??= [];
        var commandFields = string.Join(
            ",\n",
            commands.SelectMany((row, index) => new[]
            {
                $"\"LaunchCommand_{index}\": \"{Escape(row.Command)}\"",
                $"\"LaunchType_{index}\": \"{Escape(TaskTypeCatalog.Normalize(row.TaskType))}\"",
                $"\"LaunchTarget_{index}\": \"{Escape(row.LaunchTarget)}\"",
            }));

        var commandSection = commandFields.Length > 0 ? ",\n" + commandFields : string.Empty;

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
          "CompanionAppPreset": "{{Escape(CompanionAppCatalog.ToFormPresetValue(draft.CompanionAppPreset, draft.CompanionAppPath))}}",
          "CompanionAppPathDisplay": "{{Escape(draft.CompanionAppPath)}}",
          "ShowCompanionBrowseRequired": {{(CompanionAppCatalog.ShouldShowBrowseRequiredPrompt(draft.CompanionAppPreset, draft.CompanionAppPath) ? "true" : "false")}},
          "CompanionBrowseRequiredMessage": "{{Escape(CompanionAppCatalog.BrowseRequiredMessage)}}",
          "ShowCompanionExecutablePath": {{(CompanionAppCatalog.ShouldShowExecutablePath(draft.CompanionAppPath) ? "true" : "false")}},
          "ShowCompanionPathWarning": {{(CompanionAppCatalog.ShouldShowPathWarning(draft.CompanionAppPreset, draft.CompanionAppPath) ? "true" : "false")}},
          "CompanionPathWarning": "{{Escape(CompanionAppCatalog.BuildPathWarning(draft.CompanionAppPreset, draft.CompanionAppPath))}}",
          "ShowCompanionArguments": {{(CompanionAppArgumentValidation.ShouldShowArgumentsField(draft.CompanionAppPreset, draft.CompanionAppPath) ? "true" : "false")}},
          "CompanionAppArguments": "{{Escape(draft.CompanionAppArguments)}}",
          "CompanionArgumentPlaceholder": "{{Escape(CompanionAppArgumentValidation.GetArgumentPlaceholder(draft.CompanionAppPreset, draft.CompanionAppPath))}}",
          "CompanionArgumentTooltip": "{{Escape(CompanionAppArgumentValidation.GetArgumentTooltip(draft.CompanionAppPreset, draft.CompanionAppPath))}}",
          "ShowCompanionArgumentWarning": {{(CompanionAppArgumentValidation.BuildArgumentWarning(draft.CompanionAppPreset, draft.CompanionAppPath, draft.CompanionAppArguments, draft.Directory) is not null ? "true" : "false")}},
          "CompanionArgumentWarning": "{{Escape(CompanionAppArgumentValidation.BuildArgumentWarning(draft.CompanionAppPreset, draft.CompanionAppPath, draft.CompanionAppArguments, draft.Directory) ?? string.Empty)}}",
          "RunAsAdmin": "{{(draft.RunAsAdmin ? "true" : "false")}}",
          "ShowRestoredDraftNote": {{(draft.ShowRestoredDraftNote ? "true" : "false")}},
          "ShowTaskTypePicker": {{(draft.ShowTaskTypePicker ? "true" : "false")}},
          "TaskTypePicker": "{{Escape(TaskTypeCatalog.Normalize(draft.TaskTypePicker))}}"{{commandSection}}
        }
        """;
    }

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
