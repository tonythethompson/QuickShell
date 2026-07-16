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

        IReadOnlyList<(string Command, string TaskType, string LaunchTarget, bool RunAsAdmin)> rows,

        string terminalChoices)

    {

        if (rows.Count == 0)

        {

            rows = [(string.Empty, TaskTypeCatalog.None, "default", false)];

        }



        var blocks = new List<string>();

        var tipAdmin = Escape(WorkspaceFormTooltips.RunAsAdmin);

        for (var i = 0; i < rows.Count; i++)
        {
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
                    {{BuildCommandInputWithClear(i)}}
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
                        FormActionGlyphs.Refresh,
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
        "Blank = folder only · Admin elevates that row.";

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



            var commandInput = BuildCommandInputWithClear(i, escapedCommand);



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
