using QuickShell.Services;
using System.Text.Json;

namespace QuickShell.Core.Tests;

public sealed class ShortcutLaunchFormJsonTests
{
    private const string TerminalChoices = """[{ "title": "Default", "value": "default" }]""";

    [Fact]
    public void BuildCommandRowsJson_TwoCommands_UsesDistinctIds()
    {
        var json = ShortcutLaunchFormJson.WrapLaunchRowsForTest(
            ShortcutLaunchFormJson.BuildCommandRowsJson(
                [new() { Command = "npm start", LaunchTarget = "default" }, new() { Command = "dotnet watch", TaskType = TaskTypeCatalog.Api, LaunchTarget = "default", RunAsAdmin = true }],
                TerminalChoices));

        using var document = JsonDocument.Parse(json);
        var text = document.RootElement.GetRawText();

        Assert.Contains("LaunchCommand_0", text);
        Assert.Contains("LaunchCommand_1", text);
        Assert.Contains("LaunchTarget_0", text);
        Assert.Contains("LaunchTarget_1", text);
        Assert.Contains("LaunchRunAsAdmin_0", text);
        Assert.Contains("LaunchRunAsAdmin_1", text);
        Assert.Contains("${LaunchCommand_0}", text);
        Assert.Contains("${LaunchCommand_1}", text);
        Assert.Contains("${LaunchRunAsAdmin_0}", text);
        Assert.Contains("\"title\": \"Admin\"", text);
        Assert.DoesNotContain("clearLaunch", text);
        Assert.Contains("removeLaunch", text);
        Assert.Contains("addCommandRow", text);
        Assert.Contains("Add command", text);
        Assert.Contains("LaunchKind_0", text);
        Assert.Contains("LaunchLabel_0", text);
        Assert.Contains("LaunchIsEnabled_0", text);
        Assert.DoesNotContain("\"title\": \"Remove command\"", text);
        Assert.DoesNotContain("Always run as administrator", text);
    }

    [Fact]
    public void BuildCommandRowsJson_DoesNotIncludePerRowTaskTypeChoiceSet()
    {
        var json = ShortcutLaunchFormJson.WrapLaunchRowsForTest(
            ShortcutLaunchFormJson.BuildCommandRowsJson(
                [new() { Command = "npm start", TaskType = TaskTypeCatalog.Frontend, LaunchTarget = "default" }, new() { Command = "dotnet watch", TaskType = TaskTypeCatalog.Api, LaunchTarget = "default" }],
                TerminalChoices));

        using var document = JsonDocument.Parse(json);
        var text = document.RootElement.GetRawText();

        Assert.Contains("LaunchCommand_0", text);
        Assert.Contains("LaunchCommand_1", text);
        Assert.DoesNotContain("LaunchType_0", text);
        Assert.DoesNotContain("LaunchType_1", text);
        Assert.DoesNotContain("Task type", text);
    }

    [Fact]
    public void BuildCommandRowsJson_OpenInTerminal_RendersLabelWithoutCommandInput()
    {
        var text = ShortcutLaunchFormJson.BuildCommandRowsJson(
            [new() { Kind = LaunchRowKind.OpenInTerminal, Label = "Open in terminal" }],
            TerminalChoices);

        Assert.Contains("Open in terminal", text);
        Assert.Contains("removeLaunch", text);
        Assert.DoesNotContain("LaunchCommand_0", text);
    }

    [Fact]
    public void BuildCommandRowsJson_ZeroRows_RendersEmptyStateWithoutSyntheticCommand()
    {
        var text = ShortcutLaunchFormJson.BuildCommandRowsJson([], TerminalChoices);

        Assert.Contains("No launches yet", text);
        Assert.Contains("addCommandRow", text);
        Assert.Contains("addOpenInTerminalRow", text);
        Assert.DoesNotContain("LaunchCommand_0", text);
    }

    [Fact]
    public void BuildLaunchRowsJson_SingleLaunch_ContainsActualLabelAndDistinctId()
    {
        var json = ShortcutLaunchFormJson.WrapLaunchRowsForTest(
            ShortcutLaunchFormJson.BuildLaunchRowsJson(
                [new ShortcutLaunchFormJson.LaunchRowDraft { Label = "Main", Command = "npm start" }],
                TerminalChoices));

        using var document = JsonDocument.Parse(json);
        var text = document.RootElement.GetRawText();

        Assert.Contains("LaunchLabel_0", text);
        Assert.Contains("Main", text);
        Assert.Contains("npm start", text);
        Assert.DoesNotContain("{{Escape(", text);
    }

    [Fact]
    public void BuildLaunchRowsJson_TwoLaunches_UsesDistinctIdsAndLabels()
    {
        var rows = ShortcutLaunchFormJson.BuildLaunchRowsJson(
            [
                new ShortcutLaunchFormJson.LaunchRowDraft { Label = "Frontend", Command = "npm run dev" },
                new ShortcutLaunchFormJson.LaunchRowDraft { Label = "Backend", Command = "dotnet watch" },
            ],
            TerminalChoices);

        var json = ShortcutLaunchFormJson.WrapLaunchRowsForTest(rows);
        using var document = JsonDocument.Parse(json);
        var text = document.RootElement.GetRawText();

        Assert.Contains("LaunchLabel_0", text);
        Assert.Contains("LaunchLabel_1", text);
        Assert.Contains("LaunchCommand_0", text);
        Assert.Contains("LaunchCommand_1", text);
        Assert.Contains("Frontend", text);
        Assert.Contains("Backend", text);
        Assert.DoesNotContain("LaunchLabel_{{i}}", text);
        Assert.DoesNotContain("{{Escape(", text);
    }

    [Fact]
    public void BuildLaunchRowsJson_EscapesQuotesInValues()
    {
        var json = ShortcutLaunchFormJson.WrapLaunchRowsForTest(
            ShortcutLaunchFormJson.BuildLaunchRowsJson(
                [new ShortcutLaunchFormJson.LaunchRowDraft { Label = "Say \"hi\"", Command = "echo \"test\"" }],
                "[]"));

        JsonDocument.Parse(json);
        Assert.Contains("\\\"hi\\\"", json);
        Assert.Contains("echo \\\"test\\\"", json);
    }

    [Fact]
    public void BuildLaunchRowsJson_VerifiesLaunchLabelAndEnabledFields()
    {
        var json = ShortcutLaunchFormJson.WrapLaunchRowsForTest(
            ShortcutLaunchFormJson.BuildLaunchRowsJson(
                [
                    new ShortcutLaunchFormJson.LaunchRowDraft { Label = "Main", Command = "npm start", IsEnabled = true },
                    new ShortcutLaunchFormJson.LaunchRowDraft { Label = "Disabled Task", Command = "npm test", IsEnabled = false },
                    new ShortcutLaunchFormJson.LaunchRowDraft { Label = "", Command = "dotnet run", IsEnabled = true }
                ],
                TerminalChoices));

        using var document = JsonDocument.Parse(json);
        var body = document.RootElement.GetProperty("body");

        var launchLabel0 = FindElementById(body, "LaunchLabel_0");
        var launchLabel1 = FindElementById(body, "LaunchLabel_1");
        var launchLabel2 = FindElementById(body, "LaunchLabel_2");
        var launchEnabled0 = FindElementById(body, "LaunchEnabled_0");
        var launchEnabled1 = FindElementById(body, "LaunchEnabled_1");
        var launchEnabled2 = FindElementById(body, "LaunchEnabled_2");

        Assert.Equal("Main", launchLabel0.GetProperty("value").GetString());
        Assert.Equal("Disabled Task", launchLabel1.GetProperty("value").GetString());
        Assert.Equal("", launchLabel2.GetProperty("value").GetString());
        Assert.Equal("true", launchEnabled0.GetProperty("value").GetString());
        Assert.Equal("false", launchEnabled1.GetProperty("value").GetString());
        Assert.Equal("true", launchEnabled2.GetProperty("value").GetString());
    }

    private static JsonElement FindElementById(JsonElement body, string id)
    {
        foreach (var element in body.EnumerateArray())
        {
            if (element.TryGetProperty("id", out var idProp) && idProp.GetString() == id)
            {
                return element;
            }
            if (element.TryGetProperty("body", out var nestedBody) && nestedBody.ValueKind == JsonValueKind.Array)
            {
                var found = FindElementById(nestedBody, id);
                if (found.ValueKind != JsonValueKind.Undefined)
                {
                    return found;
                }
            }
        }
        return default;
    }
}
