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

        public string RepoUrl { get; init; } = string.Empty;

        public string CompanionAppPreset { get; init; } = CompanionAppCatalog.PresetNone;

        public string CompanionAppPath { get; init; } = string.Empty;

        public bool ShowRestoredDraftNote { get; init; }

        public bool RunAsAdmin { get; init; }
    }

    public static string BuildTemplate(
        string terminalChoices,
        string companionChoices,
        IReadOnlyList<string> commands,
        string displayName = DisplayNameDefault)
    {
        var commandRows = ShortcutLaunchFormJson.BuildCommandRowsJson(commands);
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
                {{AdaptiveCardFormJson.FieldHelp("Folder opened when you run this workspace. Browse or paste to pick a folder.")}},
                {
                  "type": "Input.Text",
                  "id": "Directory",
                  "isRequired": true,
                  "errorMessage": "Folder path is required",
                  "placeholder": "Type or paste a path, e.g. C:\\Projects\\MyApp",
                  "value": "${Directory}"
                },
                {
                  "type": "ActionSet",
                  "spacing": "Small",
                  "actions": [
                    {
                      "type": "Action.Submit",
                      "title": "Browse folder",
                      "data": { "action": "browse" },
                      "associatedInputs": "none"
                    },
                    {
                      "type": "Action.Submit",
                      "title": "Paste path",
                      "data": { "action": "paste" },
                      "associatedInputs": "none"
                    }
                  ]
                }
              ]
            },
            {{AdaptiveCardFormJson.FieldGroup("Name", $"Shown in your {displayName} list. Filled in from the folder name when you browse or paste—you can edit it.", """
            {
              "type": "Input.Text",
              "id": "Name",
              "value": "${Name}"
            }
            """)}},
            {{AdaptiveCardFormJson.FieldGroup("Home keyword (optional)", "Type this at Command Palette home to jump straight to this workspace.", """
            {
              "type": "Input.Text",
              "id": "Abbreviation",
              "placeholder": "e.g. api",
              "value": "${Abbreviation}"
            }
            """)}},
            {{AdaptiveCardFormJson.FieldGroup("Dev server URL (optional)", "Opens in your browser when you run this workspace (e.g. http://localhost:3000). Use a launch command such as npm run dev to start the server in a terminal.", """
            {
              "type": "Input.Text",
              "id": "DevServerUrl",
              "value": "${DevServerUrl}"
            }
            """)}},
            {{AdaptiveCardFormJson.FieldGroup("Repository URL (optional)", "Opens from the workspace action menu, e.g. your GitHub repo page.", """
            {
              "type": "Input.Text",
              "id": "RepoUrl",
              "placeholder": "https://github.com/you/your-repo",
              "value": "${RepoUrl}"
            }
            """)}},
            {
              "type": "Container",
              "spacing": "Medium",
              "items": [
                {
                  "type": "Container",
                  "spacing": "Small",
                  "items": [
                    {{AdaptiveCardFormJson.FieldLabel("App preset")}},
                    {{AdaptiveCardFormJson.FieldHelp("Optionally open an editor or other app with this workspace folder when you run the workspace.")}},
                    {
                      "type": "Input.ChoiceSet",
                      "id": "CompanionAppPreset",
                      "style": "compact",
                      "value": "${CompanionAppPreset}",
                      "choices": {{companionChoices}}
                    },
                    {
                      "type": "ActionSet",
                      "spacing": "Small",
                      "actions": [
                        {
                          "type": "Action.Submit",
                          "title": "Choose custom app…",
                          "tooltip": "Pick any installed application.",
                          "data": { "action": "browseCompanionApp" },
                          "associatedInputs": "auto"
                        }
                      ]
                    }
                  ]
                }
              ]
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
              "type": "TextBlock",
              "text": "Commands",
              "weight": "Bolder",
              "spacing": "Medium"
            },
            {
              "type": "TextBlock",
              "text": "Each command uses this workspace's terminal. Leave blank to open the folder only.",
              "wrap": true,
              "isSubtle": true,
              "spacing": "Small"
            },
            {{commandRows}},
            {
              "type": "Container",
              "spacing": "Medium",
              "items": [
                {{AdaptiveCardFormJson.FieldLabel("Terminal profile")}},
                {{AdaptiveCardFormJson.FieldHelp("Applies to every command in this workspace.")}},
                {
                  "type": "Input.ChoiceSet",
                  "id": "LaunchTarget",
                  "style": "compact",
                  "value": "${LaunchTarget}",
                  "choices": {{terminalChoices}}
                },
                {
                  "type": "ActionSet",
                  "spacing": "Small",
                  "actions": [
                    {
                      "type": "Action.Submit",
                      "title": "Refresh profile list",
                      "tooltip": "Reload after installing a shell or editing Windows Terminal settings.",
                      "associatedInputs": "auto",
                      "data": { "action": "refreshTerminals" }
                    }
                  ]
                }
              ]
            },
            {{AdaptiveCardFormJson.FieldGroup("Administrator", "Launch elevated. Windows may show a UAC prompt each time.", """
            {
              "type": "Input.Toggle",
              "id": "RunAsAdmin",
              "title": "Always run as administrator",
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

    public static string BuildDataJson(DataPayload draft, IReadOnlyList<string>? commands = null)
    {
        commands ??= [];
        var commandFields = string.Join(
            ",\n",
            commands.Select((command, index) =>
                $"\"LaunchCommand_{index}\": \"{Escape(command)}\""));

        var commandSection = commandFields.Length > 0 ? ",\n" + commandFields : string.Empty;

        return $$"""
        {
          "OriginalName": "{{Escape(draft.OriginalName)}}",
          "Name": "{{Escape(draft.Name)}}",
          "Abbreviation": "{{Escape(draft.Abbreviation)}}",
          "Directory": "{{Escape(draft.Directory)}}",
          "LaunchTarget": "{{Escape(draft.LaunchTarget)}}",
          "DevServerUrl": "{{Escape(draft.DevServerUrl)}}",
          "RepoUrl": "{{Escape(draft.RepoUrl)}}",
          "CompanionAppPreset": "{{Escape(draft.CompanionAppPreset)}}",
          "CompanionAppPathDisplay": "{{Escape(draft.CompanionAppPath)}}",
          "ShowCompanionExecutablePath": {{(CompanionAppCatalog.ShouldShowExecutablePath(draft.CompanionAppPreset, draft.CompanionAppPath) ? "true" : "false")}},
          "ShowCompanionPathWarning": {{(CompanionAppCatalog.ShouldShowPathWarning(draft.CompanionAppPreset, draft.CompanionAppPath) ? "true" : "false")}},
          "CompanionPathWarning": "{{Escape(CompanionAppCatalog.BuildPathWarning(draft.CompanionAppPreset, draft.CompanionAppPath))}}",
          "RunAsAdmin": "{{(draft.RunAsAdmin ? "true" : "false")}}",
          "ShowRestoredDraftNote": {{(draft.ShowRestoredDraftNote ? "true" : "false")}}{{commandSection}}
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

    private static string Escape(string? value) =>
        (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");

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
