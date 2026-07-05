// Minimal CmdPal form repro pages — exact issue drafts for local UI verification.
// Dev-only; reachable via Quick Shell > CmdPal form repros.

using System.Text.Json.Nodes;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace QuickShell.Pages.Dev;

internal static class CmdPalReproLog
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "QuickShell",
        "cmdpal-repro.log");

    public static void Write(string message)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
        File.AppendAllText(LogPath, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
    }

    public static void Reset() => File.WriteAllText(LogPath, string.Empty);
}

internal sealed partial class CmdPalFormReproIndexPage : ListPage
{
    public CmdPalFormReproIndexPage()
    {
        Name = "CmdPal form repros";
        Title = "CmdPal form repros";
        Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");
    }

    public override IListItem[] GetItems() =>
    [
        new ListItem(new WhenRefreshReproPage()) { Title = "Repro: $when + DataJson refresh" },
        new ListItem(new ChangeActionReproPage()) { Title = "Repro: changeAction ChoiceSet" },
    ];
}

internal sealed partial class WhenRefreshReproPage : ContentPage
{
    private readonly WhenRefreshReproForm _form = new();

    public WhenRefreshReproPage()
    {
        Name = "Repro when DataJson refresh";
        Title = "Repro when DataJson refresh";
    }

    public override IContent[] GetContent() => [_form];
}

internal sealed partial class WhenRefreshReproForm : FormContent
{
    public WhenRefreshReproForm()
    {
        TemplateJson = """
        {
          "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
          "type": "AdaptiveCard",
          "version": "1.6",
          "body": [
            {
              "type": "Input.ChoiceSet",
              "id": "Mode",
              "label": "Mode",
              "style": "compact",
              "value": "${Mode}",
              "choices": [
                { "title": "Off", "value": "off" },
                { "title": "Custom (requires file)", "value": "custom" }
              ]
            },
            {
              "type": "TextBlock",
              "$when": "${ShowWarning}",
              "text": "Pick a file before saving.",
              "color": "Attention",
              "wrap": true
            },
            {
              "type": "TextBlock",
              "$when": "${ShowPath}",
              "text": "Selected: ${PathDisplay}",
              "wrap": true
            },
            {
              "type": "TextBlock",
              "text": "Status: ${Status}",
              "wrap": true,
              "isSubtle": true
            }
          ],
          "actions": [
            {
              "type": "Action.Submit",
              "title": "Apply",
              "associatedInputs": "auto"
            }
          ]
        }
        """;

        DataJson = """
        {
          "Mode": "off",
          "ShowWarning": false,
          "ShowPath": false,
          "PathDisplay": "",
          "Status": "ShowWarning=false, ShowPath=false"
        }
        """;
    }

    public override CommandResult SubmitForm(string inputs, string data)
    {
        var values = JsonNode.Parse(inputs)?.AsObject();
        var mode = values?["Mode"]?.GetValue<string>() ?? "off";
        var showWarning = string.Equals(mode, "custom", StringComparison.OrdinalIgnoreCase);

        DataJson = new JsonObject
        {
            ["Mode"] = mode,
            ["ShowWarning"] = showWarning,
            ["ShowPath"] = false,
            ["PathDisplay"] = string.Empty,
            ["Status"] = $"ShowWarning={showWarning.ToString().ToLowerInvariant()}, ShowPath=false",
        }.ToJsonString();

        CmdPalReproLog.Write($"Issue1 SubmitForm inputs={inputs} data={data} DataJson={DataJson}");
        return CommandResult.KeepOpen();
    }

    public override CommandResult SubmitForm(string payload) =>
        SubmitForm(payload, string.Empty);
}

internal sealed partial class ChangeActionReproPage : ContentPage
{
    private readonly ChangeActionReproForm _form = new();

    public ChangeActionReproPage()
    {
        Name = "Repro changeAction ChoiceSet";
        Title = "Repro changeAction ChoiceSet";
    }

    public override IContent[] GetContent() => [_form];
}

internal sealed partial class ChangeActionReproForm : FormContent
{
    public ChangeActionReproForm()
    {
        TemplateJson = """
        {
          "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
          "type": "AdaptiveCard",
          "version": "1.6",
          "body": [
            {
              "type": "Input.Text",
              "id": "Name",
              "label": "Name",
              "value": "${Name}",
              "placeholder": "Type something first"
            },
            {
              "type": "Input.ChoiceSet",
              "id": "Picker",
              "label": "Picker",
              "style": "compact",
              "value": "${Picker}",
              "changeAction": {
                "type": "Action.Submit",
                "associatedInputs": "auto",
                "data": { "action": "onPick" }
              },
              "choices": [
                { "title": "None", "value": "none" },
                { "title": "Option A", "value": "a" },
                { "title": "Option B", "value": "b" }
              ]
            },
            {
              "type": "TextBlock",
              "text": "Last payload: ${LastPayload}",
              "wrap": true,
              "isSubtle": true
            }
          ],
          "actions": [
            {
              "type": "Action.Submit",
              "title": "Save",
              "associatedInputs": "auto",
              "data": { "action": "save" }
            }
          ]
        }
        """;

        DataJson = """
        {
          "Name": "hello",
          "Picker": "none",
          "LastPayload": "(none)"
        }
        """;
    }

    public override CommandResult SubmitForm(string inputs, string data)
    {
        var action = TryGetAction(data);
        if (string.IsNullOrEmpty(action))
        {
            action = TryGetActionFromInputs(inputs);
        }

        var values = ParseValues(inputs, data);
        var name = values["Name"]?.GetValue<string>() ?? string.Empty;
        var picker = values["Picker"]?.GetValue<string>() ?? "none";

        var lastPayload =
            $"action={(string.IsNullOrEmpty(action) ? "(null)" : action)}, " +
            $"Name={name}, Picker={picker}, " +
            $"inputs={TrimForDisplay(inputs)}, data={TrimForDisplay(data)}";

        DataJson = new JsonObject
        {
            ["Name"] = name,
            ["Picker"] = picker,
            ["LastPayload"] = lastPayload,
        }.ToJsonString();

        CmdPalReproLog.Write($"Issue2 SubmitForm inputs={inputs} data={data} DataJson={DataJson}");
        return CommandResult.KeepOpen();
    }

    public override CommandResult SubmitForm(string payload) =>
        SubmitForm(payload, string.Empty);

    private static string TryGetAction(string data) =>
        string.IsNullOrWhiteSpace(data)
            ? string.Empty
            : JsonNode.Parse(data)?.AsObject()?["action"]?.GetValue<string>() ?? string.Empty;

    private static string TryGetActionFromInputs(string inputs) =>
        string.IsNullOrWhiteSpace(inputs)
            ? string.Empty
            : JsonNode.Parse(inputs)?.AsObject()?["action"]?.GetValue<string>() ?? string.Empty;

    private static JsonObject ParseValues(string inputs, string data)
    {
        JsonObject merged = new();
        if (!string.IsNullOrWhiteSpace(inputs))
        {
            var parsedInputs = JsonNode.Parse(inputs)?.AsObject();
            if (parsedInputs is not null)
            {
                foreach (var property in parsedInputs)
                {
                    merged[property.Key] = property.Value?.DeepClone();
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(data))
        {
            var dataObject = JsonNode.Parse(data)?.AsObject();
            if (dataObject is not null)
            {
                foreach (var property in dataObject)
                {
                    if (property.Key.Equals("action", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    merged[property.Key] = property.Value?.DeepClone();
                }
            }
        }

        return merged;
    }

    private static string TrimForDisplay(string json) =>
        string.IsNullOrWhiteSpace(json) ? "(empty)" : json.Length <= 120 ? json : json[..120] + "...";
}
