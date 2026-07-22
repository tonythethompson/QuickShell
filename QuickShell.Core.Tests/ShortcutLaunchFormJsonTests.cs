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
                [("", "npm start", TaskTypeCatalog.None, "default", false, true), ("", "dotnet watch", TaskTypeCatalog.Api, "default", true, true)],
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
        Assert.Contains("clearLaunch", text);
        Assert.DoesNotContain("removeLaunch", text);
        Assert.DoesNotContain("addLaunch", text);
        Assert.DoesNotContain("+ Add command", text);
        Assert.DoesNotContain("Command 1", text);
        Assert.DoesNotContain("\"title\": \"Remove command\"", text);
        Assert.DoesNotContain("Always run as administrator", text);
    }

    [Fact]
    public void BuildCommandRowsJson_DoesNotIncludePerRowTaskTypeChoiceSet()
    {
        var json = ShortcutLaunchFormJson.WrapLaunchRowsForTest(
            ShortcutLaunchFormJson.BuildCommandRowsJson(
                [("", "npm start", TaskTypeCatalog.Frontend, "default", false, true), ("", "dotnet watch", TaskTypeCatalog.Api, "default", false, true)],
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

    private static JsonElement FindElementById(JsonElement container, string id)
    {
        foreach (var element in container.EnumerateArray())
        {
            if (element.TryGetProperty("id", out var idProp) && idProp.GetString() == id)
            {
                return element;
            }

            // Adaptive Cards nest inputs under items/columns (and sometimes body).
            if (TryFindInChildArray(element, "items", id, out var found)
                || TryFindInChildArray(element, "columns", id, out found)
                || TryFindInChildArray(element, "body", id, out found))
            {
                return found;
            }
        }

        return default;
    }

    private static bool TryFindInChildArray(JsonElement element, string propertyName, string id, out JsonElement found)
    {
        found = default;
        if (!element.TryGetProperty(propertyName, out var nested) || nested.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        found = FindElementById(nested, id);
        return found.ValueKind != JsonValueKind.Undefined;
    }
}
