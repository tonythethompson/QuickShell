using QuickShell.Services;
using System.Linq;
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
      "OpenDevServerOnLaunch",
      "RepoUrl",
      "CompanionAppPreset_0",
      "CompanionAppArguments_0",
      "LaunchTarget_0",
      "LaunchRunAsAdmin_0",
      "LaunchCommand_0",
  ];

    [Fact]
    public void BuildTemplate_IncludesPerRowAdminToggle_NotWorkspaceAlwaysAdmin()
    {
        var template = BuildDefaultTemplate(["npm run dev"]);

        Assert.Contains("LaunchRunAsAdmin_0", template, StringComparison.Ordinal);
        Assert.Contains("\"title\": \"Admin\"", template, StringComparison.Ordinal);
        Assert.DoesNotContain("Always run as administrator", template, StringComparison.Ordinal);
        Assert.DoesNotContain("\"id\": \"RunAsAdmin\"", template, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildTemplate_DirectoryBrowsePasteTopAlignActionsWithBareTextField()
    {
        var template = BuildDefaultTemplate();

        // Label above row; bare Input.Text; actions Top so ActionSet chrome does not drop them.
        Assert.Contains("\"id\": \"Directory\"", template, StringComparison.Ordinal);
        Assert.Contains("Folder path", template, StringComparison.Ordinal);
        Assert.DoesNotContain("\"label\": \"Folder path\"", template, StringComparison.Ordinal);
        Assert.Contains(FormActionGlyphs.BrowseLabel, template, StringComparison.Ordinal);
        Assert.Contains(FormActionGlyphs.PasteLabel, template, StringComparison.Ordinal);

        var directoryIndex = template.IndexOf("\"id\": \"Directory\"", StringComparison.Ordinal);
        Assert.True(directoryIndex >= 0);
        var window = template.Substring(
            Math.Max(0, directoryIndex - 400),
            Math.Min(2200, template.Length - Math.Max(0, directoryIndex - 400)));
        Assert.Contains(FormActionGlyphs.BrowseLabel, window, StringComparison.Ordinal);
        Assert.Contains(FormActionGlyphs.PasteLabel, window, StringComparison.Ordinal);
        Assert.Contains("\"verticalContentAlignment\": \"Top\"", window, StringComparison.Ordinal);
        Assert.True(
            window.Split("\"type\": \"ActionSet\"", StringSplitOptions.None).Length - 1 >= 2,
            "Expected separate ActionColumns for Browse and Paste.");
    }

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

        var companionChoices = FindChoiceSetChoices(document.RootElement, "CompanionAppPreset_0");
        Assert.True(companionChoices.GetArrayLength() >= 2);
        Assert.Equal("none", companionChoices[0].GetProperty("value").GetString());
        Assert.Equal(CompanionAppCatalog.FormChoiceTitleNone, companionChoices[0].GetProperty("title").GetString());
        Assert.Equal(
            CompanionAppCatalog.PresetCustom,
            companionChoices[companionChoices.GetArrayLength() - 1].GetProperty("value").GetString());
        Assert.Equal(
            CompanionAppCatalog.FormChoiceTitleCustom,
            companionChoices[companionChoices.GetArrayLength() - 1].GetProperty("title").GetString());
    }

    [Fact]
    public void BuildTemplate_EmbedsTerminalChoicesAsJsonArray()
    {
        var template = BuildDefaultTemplate();
        using var document = JsonDocument.Parse(template);

        var terminalChoices = FindChoiceSetChoices(document.RootElement, "LaunchTarget_0");
        Assert.True(terminalChoices.GetArrayLength() >= 1);
    }

    [Fact]
    public void BuildTemplate_EmbedsSuggestionPillSlots()
    {
        var template = BuildDefaultTemplate();
        Assert.Contains("addSuggestedCommand", template, StringComparison.Ordinal);
        Assert.Contains("${ShowSuggestionPills}", template, StringComparison.Ordinal);
        Assert.Contains("${ShowPill_0}", template, StringComparison.Ordinal);
        Assert.Contains("${PillTitle_0}", template, StringComparison.Ordinal);
        Assert.Contains(CommandSuggestionService.FieldLabel, template, StringComparison.Ordinal);
        Assert.Contains("expandSuggestionPills", template, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildTemplate_CommandRow_PairsCommandWithProfileAndRefreshGlyph()
    {
        var template = BuildDefaultTemplate(["dir", string.Empty]);
        Assert.Contains("\"id\": \"LaunchTarget_0\"", template, StringComparison.Ordinal);
        Assert.Contains(FormActionGlyphs.Refresh, template, StringComparison.Ordinal);
        Assert.Contains("refreshTerminals", template, StringComparison.Ordinal);
        Assert.Contains(FormActionGlyphs.RefreshProfileListTooltip, template, StringComparison.Ordinal);
        Assert.Contains(FormActionGlyphs.ClearCommandTooltip, template, StringComparison.Ordinal);
        Assert.Contains("clearLaunch", template, StringComparison.Ordinal);
        Assert.DoesNotContain("removeLaunch", template, StringComparison.Ordinal);
        Assert.DoesNotContain("addLaunch", template, StringComparison.Ordinal);
        Assert.DoesNotContain("\"title\": \"Refresh profile list\"", template, StringComparison.Ordinal);
        Assert.DoesNotContain("Command 1", template, StringComparison.Ordinal);
        Assert.DoesNotContain("+ Add command", template, StringComparison.Ordinal);
        Assert.DoesNotContain("\"title\": \"Browse folder\"", template, StringComparison.Ordinal);
        Assert.DoesNotContain("\"title\": \"Paste path\"", template, StringComparison.Ordinal);
        Assert.Contains(FormActionGlyphs.FolderOpen, template, StringComparison.Ordinal);
        Assert.Contains(FormActionGlyphs.Paste, template, StringComparison.Ordinal);
        Assert.Contains(FormActionGlyphs.BrowseFolderTooltip, template, StringComparison.Ordinal);
        Assert.Contains(FormActionGlyphs.PastePathTooltip, template, StringComparison.Ordinal);
        Assert.Contains("\"text\": \"Commands\"", template, StringComparison.Ordinal);
        Assert.DoesNotContain("addTaskTypeCommand", template, StringComparison.Ordinal);
        Assert.Contains("\"width\": \"2\"", template, StringComparison.Ordinal);
        Assert.Contains("\"width\": \"3\"", template, StringComparison.Ordinal);
        Assert.Contains(WorkspaceFormTooltips.DevServerUrlExample, template, StringComparison.Ordinal);
        Assert.Contains(WorkspaceFormTooltips.RepoUrlExample, template, StringComparison.Ordinal);
        Assert.Contains("\"isSubtle\": true", template, StringComparison.Ordinal);
        Assert.DoesNotContain("\"placeholder\": \"http://localhost:3000\"", template, StringComparison.Ordinal);
        Assert.DoesNotContain("\"placeholder\": \"https://github.com/you/your-repo\"", template, StringComparison.Ordinal);
        Assert.DoesNotContain("\"id\": \"LaunchTarget\"", template, StringComparison.Ordinal);
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
    public void BuildTemplate_SoloFieldsUseFullWidthStretchColumns()
    {
        var template = BuildDefaultTemplate();

        var repoIndex = template.IndexOf("\"id\": \"RepoUrl\"", StringComparison.Ordinal);
        Assert.True(repoIndex > 0);

        var repoSlice = template[repoIndex..Math.Min(repoIndex + 600, template.Length)];
        Assert.Contains("\"width\": \"stretch\"", repoSlice, StringComparison.Ordinal);
        Assert.DoesNotContain("\"width\": \"3\"", repoSlice, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildTemplate_UsesNarrowColumnSetForCompanionArguments()
    {
        var template = BuildDefaultTemplate();
        Assert.Contains("\"type\": \"ColumnSet\"", template, StringComparison.Ordinal);
        Assert.Contains("\"id\": \"CompanionAppArguments_0\"", template, StringComparison.Ordinal);
        Assert.Contains("\"width\": \"1\"", template, StringComparison.Ordinal);
        Assert.Contains("${ShowCompanionArguments_0}", template, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildTemplate_IncludesInlineAddCompanionButton()
    {
        var template = BuildDefaultTemplate();
        Assert.Contains("\"title\": \"+\"", template, StringComparison.Ordinal);
        Assert.Contains(CompanionAppFormEditor.AddTooltip, template, StringComparison.Ordinal);
        Assert.Contains(CompanionAppFormEditor.AddAction, template, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildDataJson_ShowsCompanionArgumentsForCatalogPreset()
    {
        if (!CompanionAppCatalog.IsPresetInstalled(CompanionAppCatalog.PresetVsCode))
        {
            return;
        }

        var path = CompanionAppCatalog.TryResolveExecutable(CompanionAppCatalog.PresetVsCode) ?? string.Empty;
        var dataJson = ShortcutFormTemplateJson.BuildDataJson(new ShortcutFormTemplateJson.DataPayload
        {
            Companions =
            [
                new CompanionAppFormRow
                {
                    Preset = CompanionAppCatalog.PresetVsCode,
                    Path = path,
                    Arguments = ".",
                    OpenOnLaunch = true,
                },
            ],
        });

        using var document = JsonDocument.Parse(dataJson);
        Assert.True(document.RootElement.GetProperty("ShowCompanionArguments_0").GetBoolean());
        Assert.Equal(".", document.RootElement.GetProperty("CompanionAppArguments_0").GetString());
        Assert.Equal(".", document.RootElement.GetProperty("CompanionArgumentPlaceholder_0").GetString());
    }

    [Fact]
    public void BuildDataJson_ParsesAsJson()
    {
        var dataJson = ShortcutFormTemplateJson.BuildDataJson(new ShortcutFormTemplateJson.DataPayload
        {
            Name = "My App",
            Directory = @"C:\Projects\My App",
            Companions =
            [
                new CompanionAppFormRow
                {
                    Preset = CompanionAppCatalog.PresetCustom,
                    Path = @"C:\Apps\Code.exe",
                },
            ],
            ShowRestoredDraftNote = true,
        });

        using var document = JsonDocument.Parse(dataJson);
        Assert.Equal("My App", document.RootElement.GetProperty("Name").GetString());
        Assert.True(document.RootElement.GetProperty("ShowRestoredDraftNote").GetBoolean());
        Assert.True(document.RootElement.GetProperty("ShowCompanionExecutablePath_0").GetBoolean());
        Assert.False(document.RootElement.GetProperty("ShowCompanionBrowseRequired_0").GetBoolean());
    }

    [Fact]
    public void BuildDataJson_CustomWithoutPath_ShowsBrowseRequired()
    {
        var dataJson = ShortcutFormTemplateJson.BuildDataJson(new ShortcutFormTemplateJson.DataPayload
        {
            Companions =
            [
                new CompanionAppFormRow { Preset = CompanionAppCatalog.PresetCustom },
            ],
        });

        using var document = JsonDocument.Parse(dataJson);
        Assert.True(document.RootElement.GetProperty("ShowCompanionBrowseRequired_0").GetBoolean());
        Assert.Equal(
            CompanionAppCatalog.BrowseRequiredMessage,
            document.RootElement.GetProperty("CompanionBrowseRequiredMessage_0").GetString());
        Assert.False(document.RootElement.GetProperty("ShowCompanionExecutablePath_0").GetBoolean());
    }

    [Fact]
    public void BuildTemplate_AlwaysShowsBrowseAction()
    {
        var template = BuildDefaultTemplate();
        Assert.DoesNotContain("${ShowCompanionBrowseAction}", template, StringComparison.Ordinal);
        Assert.Contains("${ShowCompanionBrowseRequired_0}", template, StringComparison.Ordinal);
        Assert.Contains("browseCompanionApp", template, StringComparison.Ordinal);
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
            [
                ("npm run dev", TaskTypeCatalog.Frontend, "default", false),
                ("dotnet watch", TaskTypeCatalog.Api, "wt:pwsh", true),
            ]);

        using var document = JsonDocument.Parse(dataJson);
        Assert.Equal("npm run dev", document.RootElement.GetProperty("LaunchCommand_0").GetString());
        Assert.Equal("dotnet watch", document.RootElement.GetProperty("LaunchCommand_1").GetString());
        Assert.Equal("frontend", document.RootElement.GetProperty("LaunchType_0").GetString());
        Assert.Equal("api", document.RootElement.GetProperty("LaunchType_1").GetString());
        Assert.Equal("default", document.RootElement.GetProperty("LaunchTarget_0").GetString());
        Assert.Equal("wt:pwsh", document.RootElement.GetProperty("LaunchTarget_1").GetString());
        Assert.Equal("false", document.RootElement.GetProperty("LaunchRunAsAdmin_0").GetString());
        Assert.Equal("true", document.RootElement.GetProperty("LaunchRunAsAdmin_1").GetString());
        Assert.False(document.RootElement.GetProperty("ShowSuggestionPills").GetBoolean());
    }

    [Fact]
    public void BuildDataJson_ShowsSuggestionPillsWhenTypesAreAvailable()
    {
        var root = Path.Combine(Path.GetTempPath(), "quickshell-data-picker-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "docker-compose.yml"), "services: {}");

        try
        {
            var dataJson = ShortcutFormTemplateJson.BuildDataJson(
                new ShortcutFormTemplateJson.DataPayload
                {
                    Directory = root,
                });

            using var document = JsonDocument.Parse(dataJson);
            Assert.True(document.RootElement.GetProperty("ShowSuggestionPills").GetBoolean());
            Assert.True(document.RootElement.GetProperty("ShowPill_0").GetBoolean());
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
            }
        }
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
            commands.Select(command => (command, TaskTypeCatalog.None, "default", false)).ToList());
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
