using Microsoft.Extensions.DependencyInjection;
using QuickShell.Abstractions;
using QuickShell.Abstractions.Classification;
using QuickShell.Classification;
using QuickShell.Composition;
using QuickShell.Core.Services;
using QuickShell.Services;
using System.Text.Json;

namespace QuickShell.Core.Tests;

public sealed class ShortcutFormTemplateJsonTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly IProjectAnalysisService _projectAnalysis;
    private readonly ICommandSuggestionService _commandSuggestions;

    public ShortcutFormTemplateJsonTests()
    {
        _provider = new ServiceCollection().AddQuickShellCore().BuildServiceProvider();
        _projectAnalysis = _provider.GetRequiredService<IProjectAnalysisService>();
        _commandSuggestions = _provider.GetRequiredService<ICommandSuggestionService>();
    }

    [Fact]
    public void BuildDataJson_RoundTripsLaunchKindLabelAndEnabledState()
    {
        var json = ShortcutFormTemplateJson.BuildDataJson(
            new ShortcutFormTemplateJson.DataPayload(),
            _projectAnalysis,
            _commandSuggestions,
            [new LaunchRowDraft
            {
                Kind = LaunchRowKind.OpenInTerminal,
                Label = "Terminal",
                IsEnabled = false,
                LaunchTarget = "default",
            }]);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal("OpenInTerminal", root.GetProperty("LaunchKind_0").GetString());
        Assert.Equal("Terminal", root.GetProperty("LaunchLabel_0").GetString());
        Assert.Equal("false", root.GetProperty("LaunchIsEnabled_0").GetString());
        Assert.False(root.TryGetProperty("ShowAddOpenInTerminal", out _));
    }

    [Fact]
    public void BuildTemplate_AlwaysIncludesAddTerminalAction()
    {
        var withTerminal = ShortcutFormTemplateJson.BuildTemplate(
            "[]",
            "[]",
            [new() { Kind = LaunchRowKind.OpenInTerminal, Label = "Shell" }],
            LaunchEditorText.EnglishDefaults);

        Assert.Contains("addOpenInTerminalRow", withTerminal);
        Assert.Contains("Add terminal", withTerminal);
        Assert.DoesNotContain("ShowAddOpenInTerminal", withTerminal);
    }

    [Fact]
    public void BuildTemplate_ZeroLaunches_IsValidAndHasNoSyntheticCommandInput()
    {
        var json = ShortcutFormTemplateJson.BuildTemplate("[]", "[]", [], LaunchEditorText.EnglishDefaults);

        using var document = JsonDocument.Parse(json);
        Assert.Contains("No launches yet", json);
        Assert.DoesNotContain("LaunchCommand_0", json);
    }

    [Fact]
    public void BuildTemplate_MixedLaunchKinds_IsValid()
    {
        var json = ShortcutFormTemplateJson.BuildTemplate(
            "[]",
            "[]",
            [
                new() { Kind = LaunchRowKind.Command, Command = "npm start" },
                new() { Kind = LaunchRowKind.OpenInTerminal, Label = "Shell" },
            ],
            LaunchEditorText.EnglishDefaults);

        using var document = JsonDocument.Parse(json);
        Assert.Contains("LaunchCommand_0", json);
        Assert.DoesNotContain("LaunchCommand_1", json);
        Assert.Contains("removeLaunch", json);
    }

    [Fact]
    public void BuildDataJson_WithPrecomputedPills_FillsPillCommandsWithoutDirectoryScan()
    {
        var pills = new[]
        {
            new CommandSuggestionPill(
                Command: "npm test",
                TaskType: TaskTypeCatalog.Test,
                TypeTitle: "Test",
                DisplayTitle: "npm test",
                Tooltip: "Test · npm test",
                Score: 90,
                Source: "fixture"),
            new CommandSuggestionPill(
                Command: "docker compose up",
                TaskType: TaskTypeCatalog.Services,
                TypeTitle: "Services",
                DisplayTitle: "docker compose up",
                Tooltip: "Services · docker compose up",
                Score: 80,
                Source: "fixture"),
        };

        var json = ShortcutFormTemplateJson.BuildDataJson(
            new ShortcutFormTemplateJson.DataPayload
            {
                Directory = Path.Join(Path.GetTempPath(), "qs-missing-dir-" + Guid.NewGuid().ToString("N")),
                ExpandSuggestionPills = true,
                SuggestionPills = pills,
            },
            _projectAnalysis,
            _commandSuggestions);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.True(root.GetProperty("ShowSuggestionPills").GetBoolean());
        Assert.Equal("npm test", root.GetProperty("PillCommand_0").GetString());
        Assert.Equal("docker compose up", root.GetProperty("PillCommand_1").GetString());
        Assert.True(root.GetProperty("ShowPill_0").GetBoolean());
        Assert.True(root.GetProperty("ShowPill_1").GetBoolean());
    }

    public void Dispose() => _provider.Dispose();
}
