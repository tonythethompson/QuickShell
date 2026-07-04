using QuickShell.Models;
using QuickShell.Services;

namespace QuickShell.Core.Tests;

/// <summary>
/// Documents exactly how <see cref="ShortcutRepository"/> behaves when
/// shortcuts.json is corrupted, oversized, or truncated. There are two
/// distinct recovery layers, both covered here: <c>EnsureConfigExists()</c>
/// runs once per process and, if the current file has no valid shortcut
/// content, automatically falls back to <c>shortcuts.json.bak</c> and then
/// the legacy <c>%LOCALAPPDATA%\TerminalShortcutsCmdPal\shortcuts.json</c>
/// path — this is why the README's "shortcuts disappeared" guidance to check
/// the <c>.bak</c> file works even without a manual restore. Separately,
/// <c>RestoreLastGoodLayout()</c> falls back to the in-memory layout this
/// same process already validated, for corruption that happens mid-session
/// after a successful load. Note: the legacy-path fallback reads a real
/// %LOCALAPPDATA% path, not a per-test temp directory, so it's the one seam
/// in this file that isn't fully test-isolated (it depends on whether the
/// machine running the tests ever had a real TerminalShortcutsCmdPal install).
/// </summary>
public sealed class ShortcutCorruptionRecoveryTests
{
    [Fact]
    public async Task TruncatedJson_OnFirstLoad_DoesNotThrow_FallsBackToEmpty()
    {
        using var directory = new TempDataDirectory();
        // Mid-write truncation: an object that never closes.
        File.WriteAllText(Path.Combine(directory.Path, "shortcuts.json"), """[{"Name":"Alpha","Directory":"C:\\Pro""");

        using var repository = new ShortcutRepository(directory.Path);
        await repository.PreloadAsync();

        Assert.Empty(repository.GetShortcuts());
    }

    [Fact]
    public async Task ValidJsonButNotAnArray_OnFirstLoad_DoesNotThrow_FallsBackToEmpty()
    {
        using var directory = new TempDataDirectory();
        File.WriteAllText(Path.Combine(directory.Path, "shortcuts.json"), """{"unexpected":"shape"}""");

        using var repository = new ShortcutRepository(directory.Path);
        await repository.PreloadAsync();

        Assert.Empty(repository.GetShortcuts());
    }

    [Fact]
    public async Task OversizedFile_ExceedingMaxConfigBytes_DoesNotThrow_FallsBackToEmpty()
    {
        using var directory = new TempDataDirectory();
        // 2 MB is ShortcutRepository's MaxConfigBytes cap; go one entry past it.
        var oversized = "[" + string.Join(',', Enumerable.Repeat("""{"Name":"x","Directory":"C:\\x"}""", 70_000)) + "]";
        Assert.True(oversized.Length > 2 * 1024 * 1024, "Fixture must actually exceed the 2 MB cap to exercise this path.");
        File.WriteAllText(Path.Combine(directory.Path, "shortcuts.json"), oversized);

        using var repository = new ShortcutRepository(directory.Path);
        await repository.PreloadAsync();

        Assert.Empty(repository.GetShortcuts());
    }

    [Fact]
    public async Task CorruptionAfterProcessRestart_AutomaticallyRestoresFromBakFile()
    {
        using var directory = new TempDataDirectory();
        var workspaceDirectory = Path.Combine(directory.Path, "Alpha");
        Directory.CreateDirectory(workspaceDirectory);

        using (var repository = new ShortcutRepository(directory.Path))
        {
            await repository.PreloadAsync();
            repository.Upsert(new TerminalShortcut { Name = "Alpha", Directory = workspaceDirectory });
            // A second save is required to actually produce a .bak file —
            // WriteLayoutAtomic uses File.Move for the very first write
            // (nothing to back up yet) and File.Replace(..., backupPath)
            // from the second write onward.
            repository.Upsert(new TerminalShortcut { Name = "Alpha", Directory = workspaceDirectory, Abbreviation = "a" }, originalName: "Alpha");
        }

        var configPath = Path.Combine(directory.Path, "shortcuts.json");
        var backupPath = configPath + ".bak";
        Assert.True(File.Exists(backupPath), "Expected a .bak file to exist after a second save.");

        // Simulate the file getting corrupted (e.g. a crash mid-write on a
        // future save) between process runs.
        File.WriteAllText(configPath, "not json at all");

        // Simulate an app restart: a brand-new ShortcutRepository instance
        // has no in-memory _lastGoodLayout, but its first EnsureConfigExists()
        // call detects the current file has no valid shortcut content and
        // automatically falls back to reading shortcuts.json.bak instead.
        using var reloaded = new ShortcutRepository(directory.Path);
        await reloaded.PreloadAsync();

        // .bak holds the state from just *before* the second save (File.Replace
        // moves the pre-write ConfigPath contents into backupPath), so the
        // recovered shortcut is the first save's content — no Abbreviation yet.
        var recovered = reloaded.GetByName("Alpha");
        Assert.NotNull(recovered);
        Assert.Null(recovered.Abbreviation);

        // The recovered content is written back out as the new shortcuts.json,
        // so a subsequent normal load doesn't need the .bak file again.
        Assert.False(HasCorruptMarker(configPath));

        static bool HasCorruptMarker(string path) =>
            File.ReadAllText(path).Contains("not json at all", StringComparison.Ordinal);
    }

    [Fact]
    public async Task CorruptionWithinSameProcess_FallsBackToLastKnownGoodLayoutInMemory()
    {
        using var directory = new TempDataDirectory();
        var workspaceDirectory = Path.Combine(directory.Path, "Alpha");
        Directory.CreateDirectory(workspaceDirectory);

        using var repository = new ShortcutRepository(directory.Path);
        await repository.PreloadAsync();
        repository.Upsert(new TerminalShortcut { Name = "Alpha", Directory = workspaceDirectory });

        Assert.NotNull(repository.GetByName("Alpha"));

        // Simulate external corruption of the file (a different process,
        // or a manual edit gone wrong) while this repository instance is
        // still alive and already holds a good in-memory layout.
        File.WriteAllText(Path.Combine(directory.Path, "shortcuts.json"), "not json at all");
        repository.Reload();

        // Unlike the cross-restart case above, this recovers automatically:
        // RestoreLastGoodLayout() falls back to the in-memory snapshot this
        // same process already validated and loaded successfully.
        var recovered = repository.GetByName("Alpha");
        Assert.NotNull(recovered);
        Assert.Equal(workspaceDirectory, recovered.Directory);
    }

    [Fact]
    public void ImportInterruption_TruncatedImportFile_LeavesExistingShortcutsUntouchedAndReturnsError()
    {
        using var directory = new TempDataDirectory();
        var existingDirectory = Path.Combine(directory.Path, "Existing");
        Directory.CreateDirectory(existingDirectory);

        using var repository = new ShortcutRepository(directory.Path);
        repository.Upsert(new TerminalShortcut { Name = "Existing", Directory = existingDirectory });

        var importPath = Path.Combine(directory.Path, "broken-import.json");
        File.WriteAllText(importPath, """[{"Name":"Imported","Directory":"C:\\Pro""");

        var result = repository.ImportMerge(importPath);

        Assert.False(result.Success);
        Assert.NotEmpty(result.Message);

        // The existing store must be completely unaffected by the failed import.
        var shortcuts = repository.GetShortcuts();
        Assert.Single(shortcuts);
        Assert.Equal("Existing", shortcuts[0].Name);
        Assert.Null(repository.GetByName("Imported"));
    }

    [Fact]
    public void ImportInterruption_ImportFileNotFound_ReturnsErrorWithoutTouchingExistingShortcuts()
    {
        using var directory = new TempDataDirectory();
        var existingDirectory = Path.Combine(directory.Path, "Existing");
        Directory.CreateDirectory(existingDirectory);

        using var repository = new ShortcutRepository(directory.Path);
        repository.Upsert(new TerminalShortcut { Name = "Existing", Directory = existingDirectory });

        var result = repository.ImportMerge(Path.Combine(directory.Path, "does-not-exist.json"));

        Assert.False(result.Success);
        Assert.Single(repository.GetShortcuts());
    }

    private sealed class TempDataDirectory : IDisposable
    {
        public TempDataDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "quickshell-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch
            {
            }
        }
    }
}
