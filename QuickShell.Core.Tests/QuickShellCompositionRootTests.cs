using Microsoft.Extensions.DependencyInjection;
using QuickShell.Composition;
using QuickShell.Services;

namespace QuickShell.Core.Tests;

public sealed class QuickShellCompositionRootTests : IDisposable
{
    private readonly string _configDirectory;
    private readonly ServiceProvider _services;

    public QuickShellCompositionRootTests()
    {
        _configDirectory = Path.Combine(
            Path.GetTempPath(),
            "quickshell-composition-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_configDirectory);

        var collection = new ServiceCollection();
        collection.AddQuickShellCore(_configDirectory);
        _services = collection.BuildServiceProvider();
    }

    [Fact]
    public void AddQuickShellCore_resolves_same_singleton_repository()
    {
        var first = _services.GetRequiredService<IShortcutRepository>();
        var second = _services.GetRequiredService<IShortcutRepository>();

        Assert.Same(first, second);
        Assert.IsType<ShortcutRepository>(first);
        Assert.Equal(_configDirectory, first.ConfigDirectory);
    }

    [Fact]
    public void AddQuickShellCore_pairs_draft_store_with_same_repository_singleton()
    {
        var repository = _services.GetRequiredService<IShortcutRepository>();
        var drafts = _services.GetRequiredService<IDraftStore>();
        var draftsAgain = _services.GetRequiredService<IDraftStore>();

        Assert.Same(drafts, draftsAgain);
        Assert.IsType<ShortcutDraftStore>(drafts);
        Assert.Equal(
            Path.Combine(_configDirectory, "shortcut-edit-draft.json"),
            drafts.DraftPath);
        Assert.Same(repository, _services.GetRequiredService<IShortcutRepository>());
    }

    public void Dispose()
    {
        _services.Dispose();

        try
        {
            if (Directory.Exists(_configDirectory))
            {
                Directory.Delete(_configDirectory, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup.
        }
    }
}
