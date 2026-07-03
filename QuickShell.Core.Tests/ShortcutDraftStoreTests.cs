using QuickShell.Models;
using QuickShell.Services;

namespace QuickShell.Core.Tests;

public sealed class ShortcutDraftStoreTests : IDisposable
{
    private readonly string _configDirectory;

    public ShortcutDraftStoreTests()
    {
        _configDirectory = Path.Combine(Path.GetTempPath(), "quickshell-draft-store-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_configDirectory);
    }

    [Fact]
    public void Clear_removes_pending_and_deletes_draft_file()
    {
        var shortcut = CreateSavedShortcut();
        var repository = new FakeShortcutRepository([shortcut], _configDirectory);
        var store = new ShortcutDraftStore(repository);

        store.SaveIfDirty(
            shortcut.Name,
            CreateDirtyDraft(shortcut.Name),
            CreateBaseline(shortcut),
            nameCustomized: false,
            autoFilledName: null);
        WaitForDraftFile(store);

        store.Clear();

        Assert.False(store.HasPending);
        Assert.False(File.Exists(store.DraftPath));
    }

    [Fact]
    public void Clear_after_save_prevents_stale_persist_from_recreating_draft_file()
    {
        var shortcut = CreateSavedShortcut();
        var repository = new FakeShortcutRepository([shortcut], _configDirectory);
        var store = new ShortcutDraftStore(repository);

        store.SaveIfDirty(
            shortcut.Name,
            CreateDirtyDraft(shortcut.Name),
            CreateBaseline(shortcut),
            nameCustomized: false,
            autoFilledName: null);

        store.Clear();
        store.Dispose();

        Assert.False(File.Exists(store.DraftPath));
    }

    [Fact]
    public void Reload_after_clear_does_not_restore_discarded_draft()
    {
        var shortcut = CreateSavedShortcut();
        var repository = new FakeShortcutRepository([shortcut], _configDirectory);

        using (var store = new ShortcutDraftStore(repository))
        {
            store.SaveIfDirty(
                shortcut.Name,
                CreateDirtyDraft(shortcut.Name),
                CreateBaseline(shortcut),
                nameCustomized: false,
                autoFilledName: null);
            WaitForDraftFile(store);
            store.Clear();
        }

        using var reloaded = new ShortcutDraftStore(repository);
        Assert.False(reloaded.HasPending);
        Assert.False(reloaded.TryGetForRestore(shortcut.Name, out _));
    }

    [Fact]
    public void Clear_raises_Cleared_with_original_name()
    {
        var shortcut = CreateSavedShortcut();
        var repository = new FakeShortcutRepository([shortcut], _configDirectory);
        var store = new ShortcutDraftStore(repository);
        string? clearedName = null;
        store.Cleared += name => clearedName = name;

        store.SaveIfDirty(
            shortcut.Name,
            CreateDirtyDraft(shortcut.Name),
            CreateBaseline(shortcut),
            nameCustomized: false,
            autoFilledName: null);

        store.Clear();

        Assert.Equal(shortcut.Name, clearedName);
    }

    [Fact]
    public void Clear_without_pending_does_not_raise_Cleared()
    {
        var shortcut = CreateSavedShortcut();
        var repository = new FakeShortcutRepository([shortcut], _configDirectory);
        var store = new ShortcutDraftStore(repository);
        var raised = false;
        store.Cleared += _ => raised = true;

        store.Clear();

        Assert.False(raised);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_configDirectory))
            {
                Directory.Delete(_configDirectory, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup for temp test data.
        }
    }

    private static TerminalShortcut CreateSavedShortcut() => new()
    {
        Id = "draft-store-test",
        Name = "MyProject",
        Directory = @"C:\Projects\MyProject",
        Command = "npm start",
        Terminal = "pwsh",
    };

    private static ShortcutFormDraftData CreateBaseline(TerminalShortcut shortcut) => new()
    {
        OriginalName = shortcut.Name,
        Name = shortcut.Name,
        Directory = shortcut.Directory,
        Command = shortcut.Command ?? string.Empty,
        LaunchTarget = TerminalCatalog.EncodeLaunchTargetId(shortcut),
    };

    private static ShortcutFormDraftData CreateDirtyDraft(string originalName) => new()
    {
        OriginalName = originalName,
        Name = "MyProject",
        Directory = @"C:\Projects\Changed",
        Command = "npm run dev",
        LaunchTarget = "default",
    };

    private static void WaitForDraftFile(ShortcutDraftStore store)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            if (File.Exists(store.DraftPath))
            {
                return;
            }

            Thread.Sleep(20);
        }

        throw new InvalidOperationException("Draft file was not written in time.");
    }
}
