using Microsoft.Extensions.DependencyInjection;
using QuickShell.Abstractions.Classification;
using QuickShell.Composition;
using QuickShell.Services;

namespace QuickShell.Core.Tests;

[Collection(AgentCliCatalogIsolation.Name)]
public sealed class SuggestionPillPresentationTests : IDisposable
{
    private readonly string _root;
    private readonly ServiceProvider _provider;
    private readonly IProjectAnalysisService _projectAnalysis;
    private readonly ICommandSuggestionService _suggestions;

    public SuggestionPillPresentationTests()
    {
        _root = Path.Join(Path.GetTempPath(), "quickshell-pill-presentation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _provider = new ServiceCollection().AddQuickShellCore().BuildServiceProvider();
        _projectAnalysis = _provider.GetRequiredService<IProjectAnalysisService>();
        _suggestions = _provider.GetRequiredService<ICommandSuggestionService>();
    }

    public void Dispose()
    {
        _suggestions.ResetForTests();
        _provider.Dispose();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void BuildSelectablePills_MatchesBuildDataFieldsSlotOrder()
    {
        File.WriteAllText(Path.Join(_root, "docker-compose.yml"), "services: {}");
        _suggestions.ResetForTests();

        var pills = SuggestionPillPresentation.BuildSelectablePills(
            _root,
            [],
            _projectAnalysis,
            _suggestions);
        var fields = SuggestionPillPresentation.BuildDataFields(
            _root,
            [],
            _projectAnalysis,
            _suggestions,
            expandSuggestionPills: true);

        for (var i = 0; i < pills.Count && i < SuggestionPillPresentation.MaxSlots; i++)
        {
            Assert.Equal("true", fields[$"ShowPill_{i}"]);
            Assert.Equal(pills[i].DisplayTitle, fields[$"PillTitle_{i}"]);
            Assert.Equal(pills[i].Command, fields[$"PillCommand_{i}"]);
            Assert.Equal(pills[i].TaskType, fields[$"PillTaskType_{i}"]);
        }
    }

    [Fact]
    public void BuildSelectablePills_ContainsOnlyRealCommands()
    {
        var pills = SuggestionPillPresentation.BuildSelectablePills(
            _root,
            [],
            _projectAnalysis,
            _suggestions);

        Assert.DoesNotContain(pills, pill => string.IsNullOrWhiteSpace(pill.Command));
        Assert.DoesNotContain(pills, pill => pill.DisplayTitle == "Open directory only");
    }

    [Fact]
    public void FormatDisplayTitle_UsesCommandOnlyAndTruncates()
    {
        Assert.Equal("npm test", SuggestionPillPresentation.FormatDisplayTitle("npm test"));
        Assert.Equal(
            "claude",
            SuggestionPillPresentation.FormatDisplayTitle("claude"));

        var longCommand = new string('x', SuggestionPillPresentation.DisplayTitleMaxLength + 5);
        var title = SuggestionPillPresentation.FormatDisplayTitle(longCommand);
        Assert.Equal(SuggestionPillPresentation.DisplayTitleMaxLength, title.Length);
        Assert.EndsWith("…", title, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildSuggestionPillsBlock_EmitsExactVisibleActionsWithoutWhen()
    {
        const int visible = 10;
        var json = ShortcutLaunchFormJson.BuildSuggestionPillsBlock(visible);

        // 10 pills / 4 per row = 3 ActionSets + expand + collapse.
        Assert.Equal(5, CountOccurrences(json, "\"type\": \"ActionSet\""));
        Assert.Equal(visible, CountOccurrences(json, "\"pillIndex\":"));
        Assert.DoesNotContain("ShowPill_", json, StringComparison.Ordinal);
        Assert.Contains("\"pillIndex\": 0", json, StringComparison.Ordinal);
        Assert.Contains("\"pillIndex\": 9", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"pillIndex\": 10", json, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildSuggestionPillsBlock_EmptyWhenNoVisiblePills()
    {
        var json = ShortcutLaunchFormJson.BuildSuggestionPillsBlock(0);
        Assert.DoesNotContain("pillIndex", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Suggested commands", json, StringComparison.Ordinal);
    }

    [Fact]
    public void GetVisiblePillCount_RespectsCollapseExpandAndCap()
    {
        Assert.Equal(0, SuggestionPillPresentation.GetVisiblePillCount(20, expandSuggestionPills: false, isScanningSuggestions: true));
        Assert.Equal(
            SuggestionPillPresentation.DefaultVisibleSlots,
            SuggestionPillPresentation.GetVisiblePillCount(40, expandSuggestionPills: false, isScanningSuggestions: false));
        Assert.Equal(
            SuggestionPillPresentation.MaxSlots,
            SuggestionPillPresentation.GetVisiblePillCount(40, expandSuggestionPills: true, isScanningSuggestions: false));
        Assert.Equal(5, SuggestionPillPresentation.GetVisiblePillCount(5, expandSuggestionPills: false, isScanningSuggestions: false));
    }

    [Fact]
    public void DefaultVisibleSlots_CoversThreeRows()
    {
        Assert.Equal(
            SuggestionPillPresentation.PillsPerRow * SuggestionPillPresentation.DefaultVisibleRows,
            SuggestionPillPresentation.DefaultVisibleSlots);
        Assert.Equal(3, SuggestionPillPresentation.DefaultVisibleRows);
        Assert.Equal(4, SuggestionPillPresentation.PillsPerRow);
        Assert.True(SuggestionPillPresentation.MaxSlots > SuggestionPillPresentation.DefaultVisibleSlots);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        for (var index = 0; (index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0; index += needle.Length)
        {
            count++;
        }

        return count;
    }
}
