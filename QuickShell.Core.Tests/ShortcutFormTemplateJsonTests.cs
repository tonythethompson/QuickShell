using QuickShell.Services;
using System.Text.Json;

namespace QuickShell.Core.Tests;

public sealed class ShortcutFormTemplateJsonTests
{
    private static readonly string[] RequiredInputIds =
  [
      "OriginalName",
      "Directory",
      "Name",
      "Abbreviation",
      "DevServerUrl",
      "RepoUrl",
      "CompanionAppPreset",
      "LaunchTarget",
      "RunAsAdmin",
      "LaunchCommand_0",
  ];

    [Fact]
    public void BuildTemplate_WithLiveChoiceArrays_ParsesAsJson()
    {
        var template = BuildDefaultTemplate();

        using var document = JsonDocument.Parse(template);
        Assert.Equal("AdaptiveCard", document.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public void BuildTemplate_DoesNotLeaveUnexpandedBuildTokens()
    {
        var template = BuildDefaultTemplate(["npm run dev", "dotnet watch"]);

        var exception = Record.Exception(() => ShortcutFormTemplateJson.AssertRenderableTemplate(template));
        Assert.Null(exception);
        Assert.DoesNotContain("{{companionChoices}}", template, StringComparison.Ordinal);
        Assert.DoesNotContain("{{terminalChoices}}", template, StringComparison.Ordinal);
        Assert.DoesNotContain("{{commandRows}}", template, StringComparison.Ordinal);
    }

    [Fact]
    public void AssertRenderableTemplate_ThrowsWhenCompanionChoicesTokenRemains()
    {
        var broken = BuildDefaultTemplate().Replace(
            "\"value\":\"none\"",
            "\"value\":\"none\"}}{{companionChoices}}",
            StringComparison.Ordinal);

        Assert.Throws<InvalidOperationException>(() =>
            ShortcutFormTemplateJson.AssertRenderableTemplate(broken));
    }

    [Fact]
    public void BuildTemplate_ContainsRequiredInputIds()
    {
        var template = BuildDefaultTemplate();

        foreach (var id in RequiredInputIds)
        {
            Assert.Contains($"\"id\": \"{id}\"", template, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void BuildTemplate_EmbedsCompanionChoicesAsJsonArray()
    {
        var template = BuildDefaultTemplate();
        using var document = JsonDocument.Parse(template);

        var companionChoices = FindChoiceSetChoices(document.RootElement, "CompanionAppPreset");
        Assert.True(companionChoices.GetArrayLength() >= 2);
        Assert.Equal("none", companionChoices[0].GetProperty("value").GetString());
    }

    [Fact]
    public void BuildTemplate_EmbedsTerminalChoicesAsJsonArray()
    {
        var template = BuildDefaultTemplate();
        using var document = JsonDocument.Parse(template);

        var terminalChoices = FindChoiceSetChoices(document.RootElement, "LaunchTarget");
        Assert.True(terminalChoices.GetArrayLength() >= 1);
    }

    [Fact]
    public void BuildTemplate_IncludesSaveAndCancelActions()
    {
        var template = BuildDefaultTemplate();
        using var document = JsonDocument.Parse(template);

        var actions = document.RootElement.GetProperty("actions");
        var titles = actions.EnumerateArray()
            .Select(action => action.GetProperty("title").GetString())
            .ToList();

        Assert.Contains("Save workspace", titles);
        Assert.Contains("Cancel", titles);
    }

    [Fact]
    public void BuildDataJson_ParsesAsJson()
    {
        var dataJson = ShortcutFormTemplateJson.BuildDataJson(new ShortcutFormTemplateJson.DataPayload
        {
            Name = "My App",
            Directory = @"C:\Projects\My App",
            CompanionAppPreset = CompanionAppCatalog.PresetCustom,
            CompanionAppPath = @"C:\Apps\Code.exe",
            ShowRestoredDraftNote = true,
            RunAsAdmin = true,
        });

        using var document = JsonDocument.Parse(dataJson);
        Assert.Equal("My App", document.RootElement.GetProperty("Name").GetString());
        Assert.True(document.RootElement.GetProperty("ShowRestoredDraftNote").GetBoolean());
        Assert.True(document.RootElement.GetProperty("ShowCompanionExecutablePath").GetBoolean());
    }

    [Fact]
    public void BuildDataJson_EscapesBackslashesInDirectory()
    {
        var dataJson = ShortcutFormTemplateJson.BuildDataJson(new ShortcutFormTemplateJson.DataPayload
        {
            Directory = @"C:\Projects\demo",
        });

        JsonDocument.Parse(dataJson);
        Assert.Contains(@"C:\\Projects\\demo", dataJson, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildDataJson_IncludesLaunchCommandValues()
    {
        var dataJson = ShortcutFormTemplateJson.BuildDataJson(
            new ShortcutFormTemplateJson.DataPayload { Name = "App" },
            ["npm run dev", "dotnet watch"]);

        using var document = JsonDocument.Parse(dataJson);
        Assert.Equal("npm run dev", document.RootElement.GetProperty("LaunchCommand_0").GetString());
        Assert.Equal("dotnet watch", document.RootElement.GetProperty("LaunchCommand_1").GetString());
    }

    [Fact]
    public void BuildDiscardPromptTemplate_ParsesAsJson()
    {
        using var document = JsonDocument.Parse(ShortcutFormTemplateJson.BuildDiscardPromptTemplate());
        var actions = document.RootElement.GetProperty("actions");
        Assert.Equal(2, actions.GetArrayLength());
    }

    [Fact]
    public void AdaptiveCardFormJson_FieldGroup_DoesNotExpandNestedChoiceTokens()
    {
        var fragment = AdaptiveCardFormJson.FieldGroup("App preset", "help", """
        {
          "type": "Input.ChoiceSet",
          "id": "CompanionAppPreset",
          "choices": {{companionChoices}}
        }
        """);

        Assert.Contains("{{companionChoices}}", fragment, StringComparison.Ordinal);
        Assert.ThrowsAny<Exception>(() => JsonDocument.Parse(fragment));
    }

    private static string BuildDefaultTemplate(IReadOnlyList<string>? commands = null)
    {
        commands ??= [string.Empty];
        return ShortcutFormTemplateJson.BuildTemplate(
            TerminalCatalog.BuildFormChoicesJson(includeDefaultChoice: true),
            CompanionAppCatalog.BuildFormChoicesJson(),
            commands);
    }

    private static JsonElement FindChoiceSetChoices(JsonElement root, string choiceSetId)
    {
        foreach (var choices in EnumerateChoiceSets(root))
        {
            if (string.Equals(choices.Id, choiceSetId, StringComparison.Ordinal))
            {
                return choices.Choices;
            }
        }

        throw new InvalidOperationException($"Choice set '{choiceSetId}' was not found in template JSON.");
    }

    private static IEnumerable<(string Id, JsonElement Choices)> EnumerateChoiceSets(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (element.TryGetProperty("type", out var type)
                    && type.GetString() == "Input.ChoiceSet"
                    && element.TryGetProperty("id", out var id)
                    && element.TryGetProperty("choices", out var choices))
                {
                    yield return (id.GetString() ?? string.Empty, choices);
                }

                foreach (var property in element.EnumerateObject())
                {
                    foreach (var nested in EnumerateChoiceSets(property.Value))
                    {
                        yield return nested;
                    }
                }

                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    foreach (var nested in EnumerateChoiceSets(item))
                    {
                        yield return nested;
                    }
                }

                break;
        }
    }
}
