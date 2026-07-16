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

    [Fact]
    public void SaveIfDirty_TaskTypeOnlyChange_IsTreatedAsDirtyAndPersisted()
    {
        var shortcut = CreateSavedShortcut();
        var repository = new FakeShortcutRepository([shortcut], _configDirectory);
        var store = new ShortcutDraftStore(repository);

        var baseline = CreateLaunchBaseline(shortcut, TaskTypeCatalog.None);
        var dirty = CreateLaunchBaseline(shortcut, TaskTypeCatalog.Database);

        store.SaveIfDirty(shortcut.Name, dirty, baseline, nameCustomized: false, autoFilledName: null);

        Assert.True(store.HasPending);
        Assert.True(store.TryGetForRestore(shortcut.Name, out var restored));
        Assert.Equal(TaskTypeCatalog.Database, restored.Launches[0].TaskType);
    }

    [Fact]
    public void TryGetForRestore_SecondaryCompanionChange_RemainsPending()
    {
        var shortcut = CreateSavedShortcut();
        var primaryPath = Path.Combine(_configDirectory, "Code.exe");
        var secondaryPath = Path.Combine(_configDirectory, "Fork.exe");
        File.WriteAllText(primaryPath, string.Empty);
        File.WriteAllText(secondaryPath, string.Empty);
        shortcut.CompanionApps =
        [
            new CompanionAppEntry { Id = "primary", Path = primaryPath, Arguments = ".", OpenOnLaunch = true, Order = 0 },
            new CompanionAppEntry { Id = "secondary", Path = secondaryPath, Arguments = "{folder}", OpenOnLaunch = true, Order = 1 },
        ];
        CompanionAppNormalization.NormalizeCompanions(shortcut);
        var repository = new FakeShortcutRepository([shortcut], _configDirectory);
        using var store = new ShortcutDraftStore(repository);
        var baseline = CreateLaunchBaseline(shortcut, TaskTypeCatalog.None);
        ApplyPrimaryCompanionScalars(baseline, shortcut.CompanionApps[0]);
        baseline.Companions = CreateCompanions(_configDirectory, openSecondary: true);
        var dirty = CreateLaunchBaseline(shortcut, TaskTypeCatalog.None);
        ApplyPrimaryCompanionScalars(dirty, shortcut.CompanionApps[0]);
        dirty.Companions = CreateCompanions(_configDirectory, openSecondary: false);

        store.SaveIfDirty(shortcut.Name, dirty, baseline, nameCustomized: false, autoFilledName: null);

        Assert.True(store.TryGetForRestore(shortcut.Name, out var restored));
        Assert.False(restored.Companions[1].OpenOnLaunch);
    }

    [Fact]
    public void TryCommitPending_PreservesAllCompanionRows()
    {
        var shortcut = CreateSavedShortcut();
        shortcut.Directory = _configDirectory;
        var repository = new FakeShortcutRepository([shortcut], _configDirectory);
        using var store = new ShortcutDraftStore(repository);
        var baseline = CreateLaunchBaseline(shortcut, TaskTypeCatalog.None);
        var dirty = CreateLaunchBaseline(shortcut, TaskTypeCatalog.None);
        dirty.Companions = CreateCompanions(_configDirectory, openSecondary: false);

        store.SaveIfDirty(shortcut.Name, dirty, baseline, nameCustomized: false, autoFilledName: null);

        var result = store.TryCommitPending(onSaved: null);
        Assert.True(result.Success, result.Message);
        var saved = Assert.IsType<TerminalShortcut>(repository.GetByName(shortcut.Name));
        Assert.Equal(2, saved.CompanionApps.Count);
        Assert.False(saved.CompanionApps[1].OpenOnLaunch);
    }

    private static ShortcutFormDraftData CreateLaunchBaseline(TerminalShortcut shortcut, string taskType) => new()
    {
        OriginalName = shortcut.Name,
        Name = shortcut.Name,
        Directory = shortcut.Directory,
        Command = shortcut.Command ?? string.Empty,
        LaunchTarget = TerminalCatalog.EncodeLaunchTargetId(shortcut),
        Launches =
        [
            new ShortcutFormLaunchDraftData
            {
                Id = "launch-1",
                Label = "Main",
                Command = shortcut.Command ?? string.Empty,
                LaunchTarget = TerminalCatalog.EncodeLaunchTargetId(shortcut),
                IsEnabled = true,
                TaskType = taskType,
            },
        ],
    };

    private static List<ShortcutFormCompanionDraftData> CreateCompanions(string root, bool openSecondary)
    {
        var primary = Path.Combine(root, "Code.exe");
        var secondary = Path.Combine(root, "Fork.exe");
        File.WriteAllText(primary, string.Empty);
        File.WriteAllText(secondary, string.Empty);
        return
        [
            new() { Id = "primary", Preset = CompanionAppCatalog.PresetCustom, Path = primary, Arguments = ".", OpenOnLaunch = true },
            new() { Id = "secondary", Preset = CompanionAppCatalog.PresetCustom, Path = secondary, Arguments = "{folder}", OpenOnLaunch = openSecondary },
        ];
    }

    private static void ApplyPrimaryCompanionScalars(ShortcutFormDraftData draft, CompanionAppEntry primary)
    {
        draft.OpenCompanionAppOnLaunch = primary.OpenOnLaunch;
        draft.CompanionAppPath = primary.Path ?? string.Empty;
        draft.CompanionAppArguments = primary.Arguments ?? string.Empty;
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
        store.FlushPendingFileIoForTests();

        if (!File.Exists(store.DraftPath))
        {
            throw new InvalidOperationException("Draft file was not written.");
        }
    }
}
