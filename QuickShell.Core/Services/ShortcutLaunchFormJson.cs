namespace QuickShell.Services;

internal static class ShortcutLaunchFormJson
{
    public sealed class LaunchRowDraft
    {
        public string Label { get; set; } = string.Empty;

        public string Command { get; set; } = string.Empty;

        public string LaunchTarget { get; set; } = "default";

        public bool RunAsAdmin { get; set; }

        public bool IsEnabled { get; set; } = true;
    }

    public static string BuildCommandRowsJson(IReadOnlyList<string> commands)
    {
        if (commands.Count == 0)
        {
            commands = [string.Empty];
        }

        var blocks = new List<string>();
        for (var i = 0; i < commands.Count; i++)
        {
            var removeBlock = commands.Count > 1
                ? $$"""
                ,{
                  "type": "ActionSet",
                  "spacing": "Small",
                  "actions": [
                    {
                      "type": "Action.Submit",
                      "title": "Remove command",
                      "data": { "action": "removeLaunch", "launchIndex": {{i}} },
                      "associatedInputs": "auto"
                    }
                  ]
                }
                """
                : string.Empty;

            blocks.Add($$"""
            {
              "type": "Container",
              "spacing": "Medium",
              "separator": true,
              "items": [
                {
                  "type": "TextBlock",
                  "text": "Command {{i + 1}}",
                  "weight": "Bolder",
                  "spacing": "Small"
                },
                {
                  "type": "Input.Text",
                  "id": "LaunchCommand_{{i}}",
                  "placeholder": "Optional command or script",
                  "value": "${LaunchCommand_{{i}}}"
                }
                {{removeBlock}}
              ]
            }
            """);
        }

        blocks.Add("""
        {
          "type": "ActionSet",
          "spacing": "Small",
          "actions": [
            {
              "type": "Action.Submit",
              "title": "+ Add command",
              "data": { "action": "addLaunch" },
              "associatedInputs": "auto"
            }
          ]
        }
        """);

        return string.Join(',', blocks);
    }

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

            var commandInput = $$"""
            {
              "type": "Input.Text",
              "id": "LaunchCommand_{{i}}",
              "value": "{{escapedCommand}}"
            }
            """;

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

            var removeBlock = launches.Count > 1
                ? $$"""
                ,{
                  "type": "ActionSet",
                  "spacing": "Small",
                  "actions": [
                    {
                      "type": "Action.Submit",
                      "title": "Remove terminal",
                      "data": { "action": "removeLaunch", "launchIndex": {{i}} },
                      "associatedInputs": "auto"
                    }
                  ]
                }
                """
                : string.Empty;

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
                {{removeBlock}}
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
