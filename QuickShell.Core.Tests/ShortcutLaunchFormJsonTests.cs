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
}
