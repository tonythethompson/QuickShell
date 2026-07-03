using QuickShell.Models;
using QuickShell.Services;
using System.Globalization;

namespace QuickShell.Core.Tests;

public sealed class ShortcutRepositoryPerformanceShapeTests
{
    [Fact]
    public void LookupIndexes_StayCurrent_AfterRenameDeleteAndUndo()
    {
        using var directory = new TempDataDirectory();
        using var repository = new ShortcutRepository(directory.Path);
        var workspaceDirectory = Path.Combine(directory.Path, "Alpha");
        Directory.CreateDirectory(workspaceDirectory);

        repository.Upsert(CreateShortcut("Alpha", workspaceDirectory));
        var saved = repository.GetByName("Alpha");
        Assert.NotNull(saved);
        var shortcutId = saved.Id;

        repository.Upsert(CreateShortcut("Beta", workspaceDirectory), originalName: "Alpha");

        Assert.Null(repository.GetByName("Alpha"));
        Assert.Equal("Beta", repository.GetByName("Beta")?.Name);
        Assert.Equal("Beta", repository.GetById(shortcutId)?.Name);

        Assert.True(repository.Delete("Beta"));
        Assert.Null(repository.GetById(shortcutId));

        Assert.True(repository.Undo());
        Assert.Equal("Beta", repository.GetById(shortcutId)?.Name);
    }

    [Fact]
    public void UndoHistory_CapsAtTwentyFiveEntries()
    {
        using var directory = new TempDataDirectory();
        using var repository = new ShortcutRepository(directory.Path);
        var workspaceDirectory = Path.Combine(directory.Path, "Workspaces");
        Directory.CreateDirectory(workspaceDirectory);

        for (var i = 0; i < 31; i++)
        {
            var projectDirectory = Path.Combine(workspaceDirectory, i.ToString(CultureInfo.InvariantCulture));
            Directory.CreateDirectory(projectDirectory);
            repository.Upsert(CreateShortcut($"Project {i}", projectDirectory));
        }

        var undoCount = 0;
        while (repository.Undo())
        {
            undoCount++;
        }

        Assert.Equal(25, undoCount);
    }

    [Fact]
    public void Search_ReturnsDefensiveCopies()
    {
        using var directory = new TempDataDirectory();
        using var repository = new ShortcutRepository(directory.Path);
        var workspaceDirectory = Path.Combine(directory.Path, "Alpha");
        Directory.CreateDirectory(workspaceDirectory);
        repository.Upsert(CreateShortcut("Alpha", workspaceDirectory));

        var result = repository.Search("Alpha").Single();
        result.Name = "Mutated";
        result.Directory = "C:\\Mutated";

        var saved = repository.GetByName("Alpha");
        Assert.NotNull(saved);
        Assert.Equal("Alpha", saved.Name);
        Assert.Equal(workspaceDirectory, saved.Directory);
        Assert.Null(repository.GetByName("Mutated"));
    }

    [Fact]
    public void Search_NoMatch_StaysUnderAllocationBudget()
    {
        using var directory = new TempDataDirectory();
        using var repository = new ShortcutRepository(directory.Path);
        var workspaceDirectory = Path.Combine(directory.Path, "Workspaces");
        Directory.CreateDirectory(workspaceDirectory);

        for (var i = 0; i < 25; i++)
        {
            var projectDirectory = Path.Combine(workspaceDirectory, i.ToString(CultureInfo.InvariantCulture));
            Directory.CreateDirectory(projectDirectory);
            repository.Upsert(CreateShortcut($"Project {i}", projectDirectory));
        }

        _ = repository.Search("definitely-not-present").ToArray();
        var before = GC.GetAllocatedBytesForCurrentThread();

        var results = repository.Search("definitely-not-present").ToArray();

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Empty(results);
        Assert.True(allocated <= 256, $"Expected no-match search to allocate <= 256 bytes, allocated {allocated} bytes.");
    }

    [Fact]
    public void Search_PaddedNoMatch_StaysUnderAllocationBudget()
    {
        using var directory = new TempDataDirectory();
        using var repository = new ShortcutRepository(directory.Path);
        var workspaceDirectory = Path.Combine(directory.Path, "Workspaces");
        Directory.CreateDirectory(workspaceDirectory);

        for (var i = 0; i < 25; i++)
        {
            var projectDirectory = Path.Combine(workspaceDirectory, i.ToString(CultureInfo.InvariantCulture));
            Directory.CreateDirectory(projectDirectory);
            repository.Upsert(CreateShortcut($"Project {i}", projectDirectory));
        }

        _ = repository.Search("   definitely-not-present   ").ToArray();
        var before = GC.GetAllocatedBytesForCurrentThread();

        var results = repository.Search("   definitely-not-present   ").ToArray();

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Empty(results);
        Assert.True(allocated <= 256, $"Expected padded no-match search to allocate <= 256 bytes, allocated {allocated} bytes.");
    }

    [Fact]
    public void Search_MatchesAllSupportedFields()
    {
        using var directory = new TempDataDirectory();
        using var repository = new ShortcutRepository(directory.Path);
        var workspaceDirectory = Path.Combine(directory.Path, "Alpha");
        Directory.CreateDirectory(workspaceDirectory);
        repository.Upsert(new TerminalShortcut
        {
            Name = "Alpha",
            Directory = workspaceDirectory,
            Abbreviation = "api",
            Command = "npm run api",
            WtProfile = "PowerShell",
            Launches =
            [
                new WorkspaceEntry
                {
                    Id = "main",
                    Label = "Backend",
                    Command = "dotnet watch",
                    Terminal = "wt",
                    WtProfile = "PowerShell",
                    IsEnabled = true,
                    Order = 0,
                },
            ],
        });

        Assert.Single(repository.Search("Alpha"));
        Assert.Single(repository.Search("api"));
        Assert.Single(repository.Search(workspaceDirectory));
        Assert.Single(repository.Search("Backend"));
        Assert.Single(repository.Search("dotnet watch"));
        Assert.Single(repository.Search("PowerShell"));
    }

    [Fact]
    public void SearchForRootPalette_PrefersAbbreviationMatchesAndKeepsOrder()
    {
        using var directory = new TempDataDirectory();
        using var repository = new ShortcutRepository(directory.Path);
        var workspaceDirectory = Path.Combine(directory.Path, "Workspaces");
        Directory.CreateDirectory(workspaceDirectory);
        var exactDirectory = Path.Combine(workspaceDirectory, "Exact");
        var prefixDirectory = Path.Combine(workspaceDirectory, "Prefix");
        var nameOnlyDirectory = Path.Combine(workspaceDirectory, "NameOnly");
        Directory.CreateDirectory(exactDirectory);
        Directory.CreateDirectory(prefixDirectory);
        Directory.CreateDirectory(nameOnlyDirectory);
        repository.Upsert(new TerminalShortcut { Name = "Zeta", Directory = exactDirectory, Abbreviation = "api" });
        repository.Upsert(new TerminalShortcut { Name = "Beta", Directory = prefixDirectory, Abbreviation = "api-beta" });
        repository.Upsert(new TerminalShortcut { Name = "Api Name Only", Directory = nameOnlyDirectory });

        var results = repository.SearchForRootPalette("api").ToArray();

        Assert.Collection(
            results,
            shortcut => Assert.Equal("Zeta", shortcut.Name),
            shortcut => Assert.Equal("Beta", shortcut.Name));
    }

    [Fact]
    public void CountImportNameConflicts_IgnoresMissingImportedNames()
    {
        using var directory = new TempDataDirectory();
        using var repository = new ShortcutRepository(directory.Path);
        var workspaceDirectory = Path.Combine(directory.Path, "Alpha");
        Directory.CreateDirectory(workspaceDirectory);
        repository.Upsert(CreateShortcut("Alpha", workspaceDirectory));

        var conflicts = repository.CountImportNameConflicts(
        [
            new TerminalShortcut { Name = null!, Directory = workspaceDirectory },
            CreateShortcut("Alpha", workspaceDirectory),
        ]);

        Assert.Equal(1, conflicts);
    }

    [Fact]
    public async Task PreloadAsync_LoadsExistingShortcutFile()
    {
        using var directory = new TempDataDirectory();
        var workspaceDirectory = Path.Combine(directory.Path, "Alpha");
        Directory.CreateDirectory(workspaceDirectory);
        File.WriteAllText(
            Path.Combine(directory.Path, "shortcuts.json"),
            $$"""
            [
              {
                "Name": "Alpha",
                "Directory": "{{workspaceDirectory.Replace("\\", "\\\\")}}"
              }
            ]
            """);

        using var repository = new ShortcutRepository(directory.Path);
        await repository.PreloadAsync();

        var shortcut = repository.GetByName("Alpha");
        Assert.NotNull(shortcut);
        Assert.Equal(workspaceDirectory, shortcut.Directory);
    }

    [Fact]
    public async Task ReloadAsync_RefreshesChangedShortcutFile()
    {
        using var directory = new TempDataDirectory();
        var firstDirectory = Path.Combine(directory.Path, "Alpha");
        var secondDirectory = Path.Combine(directory.Path, "Beta");
        Directory.CreateDirectory(firstDirectory);
        Directory.CreateDirectory(secondDirectory);
        var path = Path.Combine(directory.Path, "shortcuts.json");
        File.WriteAllText(
            path,
            $$"""
            [
              {
                "Name": "Alpha",
                "Directory": "{{firstDirectory.Replace("\\", "\\\\")}}"
              }
            ]
            """);

        using var repository = new ShortcutRepository(directory.Path);
        await repository.PreloadAsync();
        File.WriteAllText(
            path,
            $$"""
            [
              {
                "Name": "Beta",
                "Directory": "{{secondDirectory.Replace("\\", "\\\\")}}"
              }
            ]
            """);

        await repository.ReloadAsync();

        Assert.Null(repository.GetByName("Alpha"));
        var shortcut = repository.GetByName("Beta");
        Assert.NotNull(shortcut);
        Assert.Equal(secondDirectory, shortcut.Directory);
    }

    private static TerminalShortcut CreateShortcut(string name, string directory) => new()
    {
        Name = name,
        Directory = directory,
    };

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
