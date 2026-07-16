using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Services;

namespace QuickShell.Core.Tests;

public sealed class DiscoverGitRepoListItemsTests : IDisposable
{
    private readonly string _root;
    private readonly IQuickShellServices _services;

    public DiscoverGitRepoListItemsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quickshell-discover-items-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        var repository = new FakeShortcutRepository([], _root);
        _services = new QuickShellServices(
            repository,
            new ShortcutDraftStore(repository),
            new QuickShellSettingsManager(),
            new FakeProjectAnalysisService(),
            new QuickShellLifetime());
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

        var first = DiscoverGitRepoListItems.CreateNew(candidate, () => { }, itemCache: cache, services: _services);
        var second = DiscoverGitRepoListItems.CreateNew(candidate, () => { }, itemCache: cache, services: _services);

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

        var first = DiscoverGitRepoListItems.CreateNew(initial, () => { }, itemCache: cache, services: _services);
        var second = DiscoverGitRepoListItems.CreateNew(refreshed, () => { }, itemCache: cache, services: _services);

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
