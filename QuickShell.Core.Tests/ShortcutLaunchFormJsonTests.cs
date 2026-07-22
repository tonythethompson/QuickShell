using QuickShell.Core.Services;
using QuickShell.Services;
using System.Text.Json;

namespace QuickShell.Core.Tests;

public sealed class ShortcutLaunchFormJsonTests
{
    private const string TerminalChoices = """[{ "title": "Default", "value": "default" }]""";

    private static readonly LaunchEditorText EditorText = LaunchEditorText.EnglishDefaults;

    [Fact]
    public void BuildCommandRowsJson_TwoCommands_UsesDistinctIds()
    {
        var json = ShortcutLaunchFormJson.WrapLaunchRowsForTest(
            ShortcutLaunchFormJson.BuildCommandRowsJson(
                [new() { Command = "npm start", LaunchTarget = "default" }, new() { Command = "dotnet watch", TaskType = TaskTypeCatalog.Api, LaunchTarget = "default", RunAsAdmin = true }],
                TerminalChoices,
                EditorText));

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
        Assert.Contains("addOpenInTerminalRow", text);
        Assert.Contains("Add terminal", text);
        Assert.Contains("LaunchKind_0", text);
        Assert.Contains("LaunchLabel_0", text);
        Assert.Contains("LaunchIsEnabled_0", text);
        Assert.DoesNotContain("\"title\": \"Remove command\"", text);
        Assert.DoesNotContain("Always run as administrator", text);
        Assert.DoesNotContain("refreshTerminals", text);
        Assert.DoesNotContain(FormActionGlyphs.RefreshLabel, text);
    }

    [Fact]
    public void BuildCommandRowsJson_DoesNotIncludePerRowTaskTypeChoiceSet()
    {
        var json = ShortcutLaunchFormJson.WrapLaunchRowsForTest(
            ShortcutLaunchFormJson.BuildCommandRowsJson(
                [new() { Command = "npm start", TaskType = TaskTypeCatalog.Frontend, LaunchTarget = "default" }, new() { Command = "dotnet watch", TaskType = TaskTypeCatalog.Api, LaunchTarget = "default" }],
                TerminalChoices,
                EditorText));

        using var document = JsonDocument.Parse(json);
        var text = document.RootElement.GetRawText();

        Assert.Contains("LaunchCommand_0", text);
        Assert.Contains("LaunchCommand_1", text);
        Assert.DoesNotContain("LaunchType_0", text);
        Assert.DoesNotContain("LaunchType_1", text);
        Assert.DoesNotContain("Task type", text);
    }

    [Fact]
    public void BuildCommandRowsJson_OpenInTerminal_RendersNonEditableFieldChrome()
    {
        var text = ShortcutLaunchFormJson.BuildCommandRowsJson(
            [new() { Kind = LaunchRowKind.OpenInTerminal, Label = "Open in terminal" }],
            TerminalChoices,
            EditorText);

        Assert.Contains("Open in terminal", text);
        Assert.Contains("\"type\": \"Container\"", text);
        Assert.Contains("\"style\": \"emphasis\"", text);
        Assert.Contains("\"type\": \"TextBlock\"", text);
        Assert.DoesNotContain("LaunchOpenInTerminalDisplay_0", text);
        Assert.DoesNotContain("LaunchCommand_0", text);
        Assert.Contains("Add terminal", text);
        Assert.Contains("addOpenInTerminalRow", text);
        Assert.Contains("removeLaunch", text);
        Assert.DoesNotContain("ShowAddOpenInTerminal", text);
    }

    [Fact]
    public void BuildCommandRowsJson_ZeroRows_RendersEmptyStateWithoutSyntheticCommand()
    {
        var text = ShortcutLaunchFormJson.BuildCommandRowsJson([], TerminalChoices, EditorText);

        Assert.Contains("No launches yet", text);
        Assert.Contains("addCommandRow", text);
        Assert.Contains("addOpenInTerminalRow", text);
        Assert.DoesNotContain("LaunchCommand_0", text);
        Assert.DoesNotContain("refreshTerminals", text);
    }

    [Fact]
    public void BuildCommandsSectionJson_SeparatesSuggestionPillsFromCommandRows()
    {
        var section = ShortcutLaunchFormJson.BuildCommandsSectionJson(
            """{ "type": "TextBlock", "text": "rows" }""",
            """{ "type": "TextBlock", "text": "pills" }""",
            EditorText);

        Assert.Contains("\"separator\": true", section);
        Assert.Contains("pills", section);
        Assert.Contains("rows", section);
        var pillsAt = section.IndexOf("pills", StringComparison.Ordinal);
        var separatorAt = section.IndexOf("\"separator\": true", StringComparison.Ordinal);
        var rowsAt = section.IndexOf("rows", StringComparison.Ordinal);
        Assert.True(pillsAt >= 0 && separatorAt > pillsAt && rowsAt > separatorAt);
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
