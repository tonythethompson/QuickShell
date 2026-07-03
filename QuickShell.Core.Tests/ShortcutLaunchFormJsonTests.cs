using QuickShell.Services;
using System.Text.Json;

namespace QuickShell.Core.Tests;

public sealed class ShortcutLaunchFormJsonTests
{
    [Fact]
    public void BuildCommandRowsJson_TwoCommands_UsesDistinctIds()
    {
        var json = ShortcutLaunchFormJson.WrapLaunchRowsForTest(
            ShortcutLaunchFormJson.BuildCommandRowsJson(["npm start", "dotnet watch"]));

        using var document = JsonDocument.Parse(json);
        var text = document.RootElement.GetRawText();

        Assert.Contains("LaunchCommand_0", text);
        Assert.Contains("LaunchCommand_1", text);
        Assert.Contains("${LaunchCommand_0}", text);
        Assert.Contains("${LaunchCommand_1}", text);
        Assert.Contains("+ Add command", text);
    }

    [Fact]
    public void BuildLaunchRowsJson_SingleLaunch_ContainsActualLabelAndDistinctId()
    {
        var json = ShortcutLaunchFormJson.WrapLaunchRowsForTest(
            ShortcutLaunchFormJson.BuildLaunchRowsJson(
                [new ShortcutLaunchFormJson.LaunchRowDraft { Label = "Main", Command = "npm start" }],
                """[{ "title": "Default", "value": "default" }]"""));

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
            """[{ "title": "Default", "value": "default" }]""");

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
}
