using Microsoft.Extensions.DependencyInjection;
using QuickShell.Abstractions;
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
    private readonly IProjectClassificationCache _classificationCache;

    public SuggestionPillPresentationTests()
    {
        _root = Path.Join(Path.GetTempPath(), "quickshell-pill-presentation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _provider = new ServiceCollection().AddQuickShellCore().BuildServiceProvider();
        _projectAnalysis = _provider.GetRequiredService<IProjectAnalysisService>();
        _classificationCache = _provider.GetRequiredService<IProjectClassificationCache>();
    }

    public void Dispose()
    {
        _provider.Dispose();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void BuildDataFields_EmptyDirectory_StillShowsOpenToDirectoryPill()
    {
        CommandSuggestionService.ClearResultCache();

        // Agent-CLI suggestions (claude, codex, etc. on PATH) are directory-content-independent,
        // so an empty directory doesn't guarantee zero ranked pills on every machine — only that
        // Open directory only is always appended after whatever ranked pills exist.
        var rankedCount = CommandSuggestionService.GetPills(_root, [], _projectAnalysis, _classificationCache).Count;

        var fields = SuggestionPillPresentation.BuildDataFields(
            _root,
            [],
            _projectAnalysis,
            _classificationCache,
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
        CommandSuggestionService.ClearResultCache();

        var fields = SuggestionPillPresentation.BuildDataFields(
            _root,
            [],
            _projectAnalysis,
            _classificationCache,
            expandSuggestionPills: false);

        var rankedCount = CommandSuggestionService.GetPills(_root, [], _projectAnalysis, _classificationCache).Count;
        Assert.True(rankedCount > 0);
        Assert.Equal("Open directory only", fields[$"PillTitle_{rankedCount}"]);
    }

    [Fact]
    public void TryFindPill_MatchesOpenToDirectoryPillByBlankCommand()
    {
        var pills = new[] { SuggestionPillPresentation.OpenToDirectoryPill };

        var found = CommandSuggestionService.TryFindPill(pills, string.Empty, TaskTypeCatalog.None);

        Assert.NotNull(found);
        Assert.Equal("Open directory only", found.DisplayTitle);
    }

    [Fact]
    public void BuildSelectablePills_IncludesOpenToDirectory_AndBlankCommandResolves()
    {
        File.WriteAllText(Path.Join(_root, "docker-compose.yml"), "services: {}");
        CommandSuggestionService.ClearResultCache();

        var pills = SuggestionPillPresentation.BuildSelectablePills(
            _root,
            [],
            _projectAnalysis,
            _classificationCache);

        Assert.Contains(pills, pill => ReferenceEquals(pill, SuggestionPillPresentation.OpenToDirectoryPill));
        Assert.Equal(
            SuggestionPillPresentation.OpenToDirectoryPill.DisplayTitle,
            pills[^1].DisplayTitle);

        // Form apply path: same list + blank command from the Adaptive Card template.
        var found = CommandSuggestionService.TryFindPill(pills, string.Empty, TaskTypeCatalog.None);
        Assert.NotNull(found);
        Assert.True(ReferenceEquals(found, SuggestionPillPresentation.OpenToDirectoryPill));
    }

    [Fact]
    public void BuildSelectablePills_MatchesBuildDataFieldsSlotOrder()
    {
        File.WriteAllText(Path.Join(_root, "docker-compose.yml"), "services: {}");
        CommandSuggestionService.ClearResultCache();

        var pills = SuggestionPillPresentation.BuildSelectablePills(
            _root,
            [],
            _projectAnalysis,
            _classificationCache);
        var fields = SuggestionPillPresentation.BuildDataFields(
            _root,
            [],
            _projectAnalysis,
            _classificationCache,
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
        Assert.DoesNotContain('·', title);
    }

    [Fact]
    public void FormatTooltip_IncludesCategoryCommandAndProductName()
    {
        Assert.Equal(
            "Test · npm test",
            SuggestionPillPresentation.FormatTooltip("Test", "npm test"));

        Assert.Equal(
            "Agent · Claude Code — Claude Code detected on PATH. Adds `claude` as a launch command.",
            SuggestionPillPresentation.FormatTooltip(
                "Agent",
                "claude",
                productName: "Claude Code",
                detail: "Claude Code detected on PATH. Adds `claude` as a launch command."));
    }
}
