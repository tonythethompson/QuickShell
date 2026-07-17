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
    public void BuildDataFields_EmptyDirectory_StillShowsOpenToDirectoryPill()
    {
        _suggestions.ResetForTests();

        // Agent-CLI suggestions (claude, codex, etc. on PATH) are directory-content-independent,
        // so an empty directory doesn't guarantee zero ranked pills on every machine - only that
        // Open directory only is always appended after whatever ranked pills exist.
        var rankedCount = _suggestions.GetPills(_root, [], _projectAnalysis).Count;

        var fields = SuggestionPillPresentation.BuildDataFields(
            _root,
            [],
            _projectAnalysis,
            _suggestions,
            expandSuggestionPills: false);

        Assert.Equal("true", fields["ShowSuggestionPills"]);
        Assert.Equal("true", fields[$"ShowPill_{rankedCount}"]);
        Assert.Equal("Open directory only", fields[$"PillTitle_{rankedCount}"]);
        Assert.Equal(string.Empty, fields[$"PillCommand_{rankedCount}"]);
        Assert.Equal(TaskTypeCatalog.None, fields[$"PillTaskType_{rankedCount}"]);
    }

    [Fact]
    public void BuildDataFields_WithRealSuggestions_AppendsOpenToDirectoryAfterThem()
    {
        File.WriteAllText(Path.Join(_root, "docker-compose.yml"), "services: {}");
        _suggestions.ResetForTests();

        var fields = SuggestionPillPresentation.BuildDataFields(
            _root,
            [],
            _projectAnalysis,
            _suggestions,
            expandSuggestionPills: false);

        var rankedCount = _suggestions.GetPills(_root, [], _projectAnalysis).Count;
        Assert.True(rankedCount > 0);
        Assert.Equal("Open directory only", fields[$"PillTitle_{rankedCount}"]);
    }

    [Fact]
    public void TryFindPill_MatchesOpenToDirectoryPillByBlankCommand()
    {
        var pills = new[] { SuggestionPillPresentation.OpenToDirectoryPill };

        var found = _suggestions.TryFindPill(pills, string.Empty, TaskTypeCatalog.None);

        Assert.NotNull(found);
        Assert.Equal("Open directory only", found.DisplayTitle);
    }

    [Fact]
    public void BuildSelectablePills_IncludesOpenToDirectory_AndBlankCommandResolves()
    {
        File.WriteAllText(Path.Join(_root, "docker-compose.yml"), "services: {}");
        _suggestions.ResetForTests();

        var pills = SuggestionPillPresentation.BuildSelectablePills(
            _root,
            [],
            _projectAnalysis,
            _suggestions);

        Assert.Contains(pills, pill => ReferenceEquals(pill, SuggestionPillPresentation.OpenToDirectoryPill));
        Assert.Equal(
            SuggestionPillPresentation.OpenToDirectoryPill.DisplayTitle,
            pills[^1].DisplayTitle);

        // Form apply path: same list + blank command from the Adaptive Card template.
        var found = _suggestions.TryFindPill(pills, string.Empty, TaskTypeCatalog.None);
        Assert.NotNull(found);
        Assert.True(ReferenceEquals(found, SuggestionPillPresentation.OpenToDirectoryPill));
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
}
