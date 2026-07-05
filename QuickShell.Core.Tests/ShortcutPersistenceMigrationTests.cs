using QuickShell.Models;
using QuickShell.Services;

namespace QuickShell.Core.Tests;

/// <summary>
/// End-to-end coverage for old on-disk shortcuts.json shapes: every shape that
/// shipped in a real release must still load, normalize into the current
/// multi-launch model, survive a save, and reload with intent preserved.
/// Unlike <see cref="ShortcutLaunchNormalizationTests"/> (which calls the
/// normalization helpers directly on in-memory objects), these tests drive
/// the real file-backed <see cref="ShortcutRepository"/> so a regression in
/// the load/normalize/save pipeline itself — not just the helper functions —
/// gets caught.
/// </summary>
public sealed class ShortcutPersistenceMigrationTests
{
    [Fact]
    public async Task LegacySingleCommandShape_SynthesizesSingleLaunchOnLoad()
    {
        using var directory = new TempDataDirectory();
        var workspaceDirectory = Path.Combine(directory.Path, "Alpha");
        Directory.CreateDirectory(workspaceDirectory);

        // Shape from before multi-launch existed: no "Launches" array at all.
        WriteShortcutsJson(directory.Path, $$"""
        [
          {
            "Name": "Alpha",
            "Directory": "{{Escape(workspaceDirectory)}}",
            "Command": "npm start",
            "Terminal": "wt",
            "WtProfile": "PowerShell",
            "RunAsAdmin": true
          }
        ]
        """);

        using var repository = new ShortcutRepository(directory.Path);
        await repository.PreloadAsync();

        var shortcut = repository.GetByName("Alpha");
        Assert.NotNull(shortcut);
        Assert.Single(shortcut.Launches);
        Assert.Equal("npm start", shortcut.Launches[0].Command);
        Assert.Equal("wt", shortcut.Launches[0].Terminal);
        Assert.Equal("PowerShell", shortcut.Launches[0].WtProfile);
        Assert.True(shortcut.Launches[0].RunAsAdmin);
        Assert.True(shortcut.Launches[0].IsEnabled);

        // Legacy top-level fields must still mirror the synthesized launch —
        // any code path still reading Command/Terminal directly (e.g. the
        // PowerToys Run plugin's older cache) must keep working.
        Assert.Equal("npm start", shortcut.Command);
        Assert.Equal("wt", shortcut.Terminal);
    }

    [Fact]
    public async Task LegacyShapeWithoutTerminalOrProfile_DefaultsCleanly()
    {
        using var directory = new TempDataDirectory();
        var workspaceDirectory = Path.Combine(directory.Path, "Bare");
        Directory.CreateDirectory(workspaceDirectory);

        // Oldest possible shape: just Name + Directory, nothing else.
        WriteShortcutsJson(directory.Path, $$"""
        [
          { "Name": "Bare", "Directory": "{{Escape(workspaceDirectory)}}" }
        ]
        """);

        using var repository = new ShortcutRepository(directory.Path);
        await repository.PreloadAsync();

        var shortcut = repository.GetByName("Bare");
        Assert.NotNull(shortcut);
        Assert.Single(shortcut.Launches);
        Assert.Equal("default", shortcut.Launches[0].Terminal);
        Assert.Null(shortcut.Launches[0].Command);
        Assert.False(shortcut.Launches[0].RunAsAdmin);
        Assert.True(shortcut.Launches[0].IsEnabled);
    }

    [Fact]
    public async Task OutOfOrderLaunchIndices_NormalizeToSequentialOrderPreservingRelativeSequence()
    {
        using var directory = new TempDataDirectory();
        var workspaceDirectory = Path.Combine(directory.Path, "Multi");
        Directory.CreateDirectory(workspaceDirectory);

        WriteShortcutsJson(directory.Path, $$"""
        [
          {
            "Name": "Multi",
            "Directory": "{{Escape(workspaceDirectory)}}",
            "Launches": [
              { "Id": "b", "Label": "Second", "Command": "second", "IsEnabled": true, "Order": 5 },
              { "Id": "a", "Label": "First", "Command": "first", "IsEnabled": true, "Order": 2 }
            ]
          }
        ]
        """);

        using var repository = new ShortcutRepository(directory.Path);
        await repository.PreloadAsync();

        var shortcut = repository.GetByName("Multi");
        Assert.NotNull(shortcut);
        Assert.Equal(2, shortcut.Launches.Count);
        // Order field must be resequenced to 0..n-1, but the relative order
        // implied by the original (out-of-range) values must survive.
        Assert.Equal("First", shortcut.Launches[0].Label);
        Assert.Equal(0, shortcut.Launches[0].Order);
        Assert.Equal("Second", shortcut.Launches[1].Label);
        Assert.Equal(1, shortcut.Launches[1].Order);
    }

    [Fact]
    public async Task SeparatorsAndShortcuts_SurviveLoadNormalizeSaveAndFreshReload()
    {
        using var directory = new TempDataDirectory();
        var firstDirectory = Path.Combine(directory.Path, "First");
        var secondDirectory = Path.Combine(directory.Path, "Second");
        Directory.CreateDirectory(firstDirectory);
        Directory.CreateDirectory(secondDirectory);

        WriteShortcutsJson(directory.Path, $$"""
        [
          { "Type": "separator", "Title": "Work" },
          { "Name": "First", "Directory": "{{Escape(firstDirectory)}}", "Command": "npm start" },
          { "Type": "separator" }
        ]
        """);

        using (var repository = new ShortcutRepository(directory.Path))
        {
            await repository.PreloadAsync();

            // Force a save so the migrated (Launches-synthesized) layout is
            // actually written back to disk, not just held in memory.
            repository.Upsert(new TerminalShortcut { Name = "Second", Directory = secondDirectory, Command = "npm test" });
        }

        // Simulate an app restart: brand-new repository instance over the same directory.
        using var reloaded = new ShortcutRepository(directory.Path);
        await reloaded.PreloadAsync();

        var layout = reloaded.GetLayout();
        Assert.Equal(4, layout.Count);
        Assert.Equal(ShortcutLayoutEntryKind.Separator, layout[0].Kind);
        Assert.Equal("Work", layout[0].SeparatorTitle);
        Assert.Equal(ShortcutLayoutEntryKind.Shortcut, layout[1].Kind);
        Assert.Equal("First", layout[1].Shortcut?.Name);
        Assert.Equal(ShortcutLayoutEntryKind.Separator, layout[2].Kind);
        Assert.Equal(ShortcutLayoutEntryKind.Shortcut, layout[3].Kind);
        Assert.Equal("Second", layout[3].Shortcut?.Name);

        var first = reloaded.GetByName("First");
        Assert.NotNull(first);
        Assert.Single(first.Launches);
        Assert.Equal("npm start", first.Launches[0].Command);
    }

    [Fact]
    public async Task UnknownExtraProperties_AreIgnoredNotFatal()
    {
        using var directory = new TempDataDirectory();
        var workspaceDirectory = Path.Combine(directory.Path, "Forward");
        Directory.CreateDirectory(workspaceDirectory);

        // A future release's field this test binary doesn't know about yet —
        // must not break loading of everything else in the file.
        WriteShortcutsJson(directory.Path, $$"""
        [
          {
            "Name": "Forward",
            "Directory": "{{Escape(workspaceDirectory)}}",
            "Command": "npm start",
            "SomeFieldFromANewerVersion": { "nested": true },
            "AnotherUnknownField": 42
          }
        ]
        """);

        using var repository = new ShortcutRepository(directory.Path);
        await repository.PreloadAsync();

        var shortcut = repository.GetByName("Forward");
        Assert.NotNull(shortcut);
        Assert.Equal("npm start", shortcut.Command);
    }

    [Fact]
    public async Task EntryMissingRequiredNameOrDirectory_IsSkippedWithoutFailingTheWholeFile()
    {
        using var directory = new TempDataDirectory();
        var validDirectory = Path.Combine(directory.Path, "Valid");
        Directory.CreateDirectory(validDirectory);

        WriteShortcutsJson(directory.Path, $$"""
        [
          { "Directory": "{{Escape(validDirectory)}}" },
          { "Name": "OnlyName" },
          { "Name": "Valid", "Directory": "{{Escape(validDirectory)}}" }
        ]
        """);

        using var repository = new ShortcutRepository(directory.Path);
        await repository.PreloadAsync();

        var shortcuts = repository.GetShortcuts();
        Assert.Single(shortcuts);
        Assert.Equal("Valid", shortcuts[0].Name);
    }

    [Fact]
    public async Task CurrentMultiLaunchShape_RoundTripsThroughSaveAndFreshReload()
    {
        using var directory = new TempDataDirectory();
        var workspaceDirectory = Path.Combine(directory.Path, "Full");
        Directory.CreateDirectory(workspaceDirectory);

        var shortcut = new TerminalShortcut
        {
            Name = "Full",
            Directory = workspaceDirectory,
            Abbreviation = "fl",
            DevServerUrl = "http://localhost:3000",
            OpenDevServerOnLaunch = true,
            Launches =
            [
                new WorkspaceEntry { Id = "a", Label = "Backend", Command = "dotnet watch", Terminal = "wt", IsEnabled = true, Order = 0 },
                new WorkspaceEntry { Id = "b", Label = "Frontend", Command = "npm run dev", Terminal = "wt", IsEnabled = false, Order = 1 },
            ],
        };

        using (var repository = new ShortcutRepository(directory.Path))
        {
            await repository.PreloadAsync();
            repository.Upsert(shortcut);
        }

        using var reloaded = new ShortcutRepository(directory.Path);
        await reloaded.PreloadAsync();

        var saved = reloaded.GetByName("Full");
        Assert.NotNull(saved);
        Assert.Equal("fl", saved.Abbreviation);
        // Upsert runs URL validation, which normalizes a bare-authority URL
        // to include the trailing slash (standard Uri normalization) — this
        // is existing, expected behavior, not something this test changes.
        Assert.Equal("http://localhost:3000/", saved.DevServerUrl);
        Assert.True(saved.OpenDevServerOnLaunch);
        Assert.Equal(2, saved.Launches.Count);
        Assert.Equal("Backend", saved.Launches[0].Label);
        Assert.True(saved.Launches[0].IsEnabled);
        Assert.Equal("Frontend", saved.Launches[1].Label);
        Assert.False(saved.Launches[1].IsEnabled);
    }

    private static void WriteShortcutsJson(string directoryPath, string json) =>
        File.WriteAllText(Path.Combine(directoryPath, "shortcuts.json"), json);

    private static string Escape(string path) => path.Replace("\\", "\\\\");

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
