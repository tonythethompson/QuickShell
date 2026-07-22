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
        Assert.False(root.GetProperty("ShowAddOpenInTerminal").GetBoolean());
    }

    [Fact]
    public void BuildDataJson_WithoutTerminalOnlyLaunch_ShowsAddAction()
    {
        var json = ShortcutFormTemplateJson.BuildDataJson(
            new ShortcutFormTemplateJson.DataPayload(),
            _projectAnalysis,
            _commandSuggestions,
            []);

        using var document = JsonDocument.Parse(json);
        Assert.True(document.RootElement.GetProperty("ShowAddOpenInTerminal").GetBoolean());
    }

    [Fact]
    public void BuildTemplate_ZeroLaunches_IsValidAndHasNoSyntheticCommandInput()
    {
        var text = new LaunchEditorText("Add command", "Open in terminal", "Remove launch", "No launches yet", "Add at least one command or terminal launch.", "Add at least one launch.", "Add a command or open the folder in a terminal.");
        var json = ShortcutFormTemplateJson.BuildTemplate("[]", "[]", [], launchText: text);

        using var document = JsonDocument.Parse(json);
        Assert.Contains("No launches yet", json);
        Assert.DoesNotContain("LaunchCommand_0", json);
    }

    [Fact]
    public void BuildTemplate_MixedLaunchKinds_IsValid()
    {
        var text = new LaunchEditorText("Add command", "Open in terminal", "Remove launch", "No launches yet", "Add at least one command or terminal launch.", "Add at least one launch.", "Add a command or open the folder in a terminal.");
        var json = ShortcutFormTemplateJson.BuildTemplate(
            "[]",
            "[]",
            [
                new() { Kind = LaunchRowKind.Command, Command = "npm start" },
                new() { Kind = LaunchRowKind.OpenInTerminal, Label = "Shell" },
            ],
            launchText: text);

        using var document = JsonDocument.Parse(json);
        Assert.Contains("LaunchCommand_0", json);
        Assert.DoesNotContain("LaunchCommand_1", json);
        Assert.Contains("removeLaunch", json);
    }

    public void Dispose() => _provider.Dispose();
}
