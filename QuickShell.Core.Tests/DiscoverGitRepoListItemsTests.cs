using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Commands;
using QuickShell.Services;

namespace QuickShell.Core.Tests;

public sealed class DiscoverGitRepoListItemsTests : IDisposable
{
    private readonly string _root;
    private readonly IQuickShellServices _services;
    private readonly QuickShellPageContext _context;

    public DiscoverGitRepoListItemsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quickshell-discover-items-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        var repository = new FakeShortcutRepository([], _root);
        _services = TestQuickShellServicesFactory.Create(
            repository,
            new ShortcutDraftStore(repository),
            new QuickShellSettingsManager(),
            new FakeProjectAnalysisService(),
            new QuickShellLifetime());
        _context = new QuickShellPageContext(
            new QuickShellHostServices(_services),
            new CreateShortcutCommand(() => { }, _services),
            () => { });
    }

    [Fact]
    public void CreateNew_UnchangedCandidate_ReusesCachedItem()
    {
        var candidate = new GitRepoCandidate
        {
            Directory = _root,
            Name = "Sample",
            Classification = ProjectClassification.Empty,
        };
        var cache = new Dictionary<string, ListItem>(StringComparer.OrdinalIgnoreCase);

        var first = DiscoverGitRepoListItems.CreateNew(_context, candidate, () => { }, itemCache: cache);
        var second = DiscoverGitRepoListItems.CreateNew(_context, candidate, () => { }, itemCache: cache);

        Assert.Same(first, second);
    }

    [Fact]
    public void CreateNew_ClassificationChanges_ReplacesCachedItem()
    {
        var cache = new Dictionary<string, ListItem>(StringComparer.OrdinalIgnoreCase);
        var initial = new GitRepoCandidate
        {
            Directory = _root,
            Name = "Sample",
            Classification = new ProjectClassification { Stacks = ProjectStack.Node, Labels = ["Node"] },
        };
        var refreshed = new GitRepoCandidate
        {
            Directory = _root,
            Name = "Sample",
            Classification = new ProjectClassification { Stacks = ProjectStack.Python, Labels = ["Python"] },
        };

        var first = DiscoverGitRepoListItems.CreateNew(_context, initial, () => { }, itemCache: cache);
        var second = DiscoverGitRepoListItems.CreateNew(_context, refreshed, () => { }, itemCache: cache);

        Assert.NotSame(first, second);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // Best-effort temp cleanup.
        }
    }
}
