namespace QuickShell.Services;



internal static partial class ShortcutLaunchFormJson

{

    private const string CommandColumnWidth = "2";

    private const string ProfileColumnWidth = "2";

    private const string AdminColumnWidth = "auto";



    public sealed class LaunchRowDraft

    {

        public string Label { get; set; } = string.Empty;



        public string Command { get; set; } = string.Empty;



        public string LaunchTarget { get; set; } = "default";



        public bool RunAsAdmin { get; set; }



        public bool IsEnabled { get; set; } = true;

    }



    public static string BuildCommandRowsJson(
        IReadOnlyList<QuickShell.Services.LaunchRowDraft> rows,
        string terminalChoices,
        LaunchEditorText? text = null)

    {
        text ??= LaunchEditorText.English;

        var blocks = new List<string>();

        var tipAdmin = Escape(WorkspaceFormTooltips.RunAsAdmin);

        for (var i = 0; i < rows.Count; i++)
        {
            var rowContent = rows[i].Kind == LaunchRowKind.OpenInTerminal
                ? AdaptiveCardFormJson.InputWithTrailingActionsRow(
                    $$"""{ "type": "TextBlock", "text": "{{Escape(text.OpenInTerminal)}}", "weight": "Bolder", "wrap": true }""",
                    BuildRemoveLaunchAction(i, text.RemoveTooltip))
                : BuildCommandInputWithClear(i, text.RemoveTooltip);
            blocks.Add($$"""
            {
              "type": "ColumnSet",
              "spacing": "Small",
              "columns": [
                {
                  "type": "Column",
                  "width": "{{CommandColumnWidth}}",
                  "verticalContentAlignment": "Center",
                  "items": [
                    { "type": "Input.Text", "id": "LaunchKind_{{i}}", "isVisible": false, "value": "${LaunchKind_{{i}}}" },
                    { "type": "Input.Text", "id": "LaunchLabel_{{i}}", "isVisible": false, "value": "${LaunchLabel_{{i}}}" },
                    { "type": "Input.Text", "id": "LaunchIsEnabled_{{i}}", "isVisible": false, "value": "${LaunchIsEnabled_{{i}}}" },
                    {{rowContent}}
                  ]
                },
                {
                  "type": "Column",
                  "width": "{{ProfileColumnWidth}}",
                  "verticalContentAlignment": "Center",
                  "items": [
                    {
                      "type": "Input.ChoiceSet",
                      "id": "LaunchTarget_{{i}}",
                      "style": "compact",
                      "value": "${LaunchTarget_{{i}}}",
                      "tooltip": "{{Escape(FormActionGlyphs.TerminalProfileTooltip)}}",
                      "choices": {{terminalChoices}}
                    }
                  ]
                },
                {
                  "type": "Column",
                  "width": "{{AdminColumnWidth}}",
                  "verticalContentAlignment": "Center",
                  "items": [
                    {
                      "type": "Input.Toggle",
                      "id": "LaunchRunAsAdmin_{{i}}",
                      "title": "Admin",
                      "tooltip": "{{tipAdmin}}",
                      "value": "${LaunchRunAsAdmin_{{i}}}",
                      "valueOn": "true",
                      "valueOff": "false"
                    }
                  ]
                }
              ]
            }
            """);
        }

        if (rows.Count == 0)
        {
            blocks.Add($$"""{ "type": "Container", "spacing": "Small", "items": [{ "type": "TextBlock", "text": "{{Escape(text.EmptyTitle)}}", "weight": "Bolder", "wrap": true }, { "type": "TextBlock", "text": "{{Escape(text.EmptyGuidance)}}", "isSubtle": true, "wrap": true }] }""");
        }

        blocks.Add($$"""{ "type": "ActionSet", "spacing": "Small", "actions": [{ "type": "Action.Submit", "title": "{{Escape(text.AddCommand)}}", "associatedInputs": "auto", "data": { "action": "addCommandRow" } }, { "type": "Action.Submit", "title": "{{Escape(text.OpenInTerminal)}}", "$when": "${ShowAddOpenInTerminal}", "associatedInputs": "auto", "data": { "action": "addOpenInTerminalRow" } }] }""");

        blocks.Add($$"""
        {
          "type": "ColumnSet",
          "spacing": "Small",
          "columns": [
            {
              "type": "Column",
              "width": "{{ProfileColumnWidth}}",
              "items": [
                {
                  "type": "ActionSet",
                  "spacing": "None",
                  "actions": [
                    {{AdaptiveCardFormJson.IconSubmitAction(
                        FormActionGlyphs.RefreshLabel,
                        FormActionGlyphs.RefreshProfileListTooltip,
                        "refreshTerminals",
                        "none")}}
                  ]
                }
              ]
            }
          ]
        }
        """);



        return string.Join(',', blocks);

    }



    public const string CommandsSectionTooltip =
        "Add a command or open the folder in a terminal.";

    public static string BuildCommandsSectionHeaderJson() =>
        $$"""
        {
          "type": "Container",
          "spacing": "None",
          "items": [
            {{AdaptiveCardFormJson.FieldLabel("Commands", CommandsSectionTooltip)}}
          ]
        }
        """;

    public static string BuildCommandsSectionJson(string commandRows, string suggestionPillsBlock) =>
        $$"""
        {
          "type": "Container",
          "spacing": "Medium",
          "items": [
            {{BuildCommandsSectionHeaderJson()}},
            {{suggestionPillsBlock}},
            {{commandRows}}
          ]
        }
        """;

    public static string BuildLaunchRowsJson(IReadOnlyList<LaunchRowDraft> launches, string terminalChoices)

    {

        if (launches.Count == 0)

        {

            return string.Empty;

        }



        var blocks = new List<string>();

        for (var i = 0; i < launches.Count; i++)

        {

            var launch = launches[i];

            var escapedLabel = Escape(launch.Label);

            var escapedCommand = Escape(launch.Command);

            var escapedTarget = Escape(launch.LaunchTarget);

            var adminValue = launch.RunAsAdmin ? "true" : "false";

            var enabledValue = launch.IsEnabled ? "true" : "false";



            var labelInput = $$"""

            {

              "type": "Input.Text",

              "id": "LaunchLabel_{{i}}",

              "isRequired": true,

              "value": "{{escapedLabel}}"

            }

            """;



            var commandInput = BuildCommandInputWithClear(i, literalValue: escapedCommand);



            var adminInput = $$"""

            {

              "type": "Input.Toggle",

              "id": "LaunchRunAsAdmin_{{i}}",

              "title": "Run as administrator",

              "value": "{{adminValue}}",

              "valueOn": "true",

              "valueOff": "false"

            }

            """;



            var enabledInput = $$"""

            {

              "type": "Input.Toggle",

              "id": "LaunchEnabled_{{i}}",

              "title": "Include when opening workspace",

              "value": "{{enabledValue}}",

              "valueOn": "true",

              "valueOff": "false"

            }

            """;



            blocks.Add($$"""

            {

              "type": "Container",

              "spacing": "Medium",

              "separator": true,

              "items": [

                {

                  "type": "TextBlock",

                  "text": "Terminal {{i + 1}}",

                  "weight": "Bolder",

                  "spacing": "Small"

                },

                {

                  "type": "Container",

                  "spacing": "Medium",

                  "items": [

                    {

                      "type": "Container",

                      "spacing": "Small",

                      "items": [

                        {

                          "type": "TextBlock",

                          "text": "Label",

                          "weight": "Bolder",

                          "wrap": true,

                          "spacing": "None"

                        },

                        {

                          "type": "TextBlock",

                          "text": "Shown in menus when this workspace has multiple terminals.",

                          "wrap": true,

                          "isSubtle": true,

                          "size": "Small",

                          "spacing": "None"

                        },

                        {{labelInput}}

                      ]

                    }

                  ]

                },

                {

                  "type": "Container",

                  "spacing": "Medium",

                  "items": [

                    {

                      "type": "Container",

                      "spacing": "Small",

                      "items": [

                        {

                          "type": "TextBlock",

                          "text": "Command (optional)",

                          "weight": "Bolder",

                          "wrap": true,

                          "spacing": "None"

                        },

                        {

                          "type": "TextBlock",

                          "text": "Optional command or script run after the terminal opens.",

                          "wrap": true,

                          "isSubtle": true,

                          "size": "Small",

                          "spacing": "None"

                        },

                        {{commandInput}}

                      ]

                    }

                  ]

                },

                {

                  "type": "Container",

                  "spacing": "Small",

                  "items": [

                    {

                      "type": "TextBlock",

                      "text": "Terminal profile",

                      "weight": "Bolder",

                      "wrap": true,

                      "spacing": "None"

                    },

                    {

                      "type": "Input.ChoiceSet",

                      "id": "LaunchTarget_{{i}}",

                      "style": "compact",

                      "value": "{{escapedTarget}}",

                      "choices": {{terminalChoices}}

                    }

                  ]

                },

                {

                  "type": "Container",

                  "spacing": "Medium",

                  "items": [

                    {

                      "type": "Container",

                      "spacing": "Small",

                      "items": [

                        {

                          "type": "TextBlock",

                          "text": "Administrator",

                          "weight": "Bolder",

                          "wrap": true,

                          "spacing": "None"

                        },

                        {{adminInput}}

                      ]

                    }

                  ]

                },

                {

                  "type": "Container",

                  "spacing": "Medium",

                  "items": [

                    {

                      "type": "Container",

                      "spacing": "Small",

                      "items": [

                        {

                          "type": "TextBlock",

                          "text": "Enabled",

                          "weight": "Bolder",

                          "wrap": true,

                          "spacing": "None"

                        },

                        {{enabledInput}}

                      ]

                    }

                  ]

                }

              ]

            }

            """);

        }



        return string.Join(',', blocks);

    }



    public static string WrapLaunchRowsForTest(string launchRows) =>

        $$"""{ "type": "AdaptiveCard", "version": "1.6", "body": [{{launchRows}}] }""";



    private static string Escape(string? value) =>

        (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");

}
