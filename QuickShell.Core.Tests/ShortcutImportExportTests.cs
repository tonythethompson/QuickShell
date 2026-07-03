using QuickShell.Models;
using QuickShell.Services;

namespace QuickShell.Core.Tests;

public sealed class ShortcutImportExportTests
{
    [Fact]
    public void TryExportToFile_RoundTripsLayout()
    {
        using var directory = new TempDataDirectory();
        using var repository = new ShortcutRepository(directory.Path);
        var workspaceDirectory = Path.Combine(directory.Path, "Alpha");
        Directory.CreateDirectory(workspaceDirectory);
        repository.Upsert(CreateShortcut("Alpha", workspaceDirectory));

        var exportPath = Path.Combine(directory.Path, "export.json");
        Assert.True(repository.TryExportToFile(exportPath, out _));

        using var fresh = new ShortcutRepository(directory.Path);
        fresh.ResetAll();
        Assert.Empty(fresh.GetShortcuts());

        var result = fresh.ImportReplace(exportPath);
        Assert.True(result.Success);
        Assert.Single(fresh.GetShortcuts());
        Assert.Equal("Alpha", fresh.GetShortcuts()[0].Name);
    }

    [Fact]
    public void ImportMerge_RenamesConflictingNames()
    {
        using var directory = new TempDataDirectory();
        using var repository = new ShortcutRepository(directory.Path);
        var folder = Path.Combine(directory.Path, "Existing");
        Directory.CreateDirectory(folder);
        repository.Upsert(CreateShortcut("Alpha", folder));

        var importPath = Path.Combine(directory.Path, "incoming.json");
        File.WriteAllText(importPath, """
            [
              {
                "Name": "Alpha",
                "Directory": "C:\\\\Other"
              },
              {
                "Name": "Beta",
                "Directory": "C:\\\\Other2"
              }
            ]
            """);

        var result = repository.ImportMerge(importPath);

        Assert.True(result.Success);
        Assert.Equal(2, result.Imported);
        Assert.Equal(1, result.Renamed);
        Assert.Contains(repository.GetShortcuts(), s => s.Name.Equals("Alpha Copy", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(repository.GetShortcuts(), s => s.Name.Equals("Beta", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ImportReplace_ReplacesAllShortcuts()
    {
        using var directory = new TempDataDirectory();
        using var repository = new ShortcutRepository(directory.Path);
        var folder = Path.Combine(directory.Path, "Old");
        Directory.CreateDirectory(folder);
        repository.Upsert(CreateShortcut("Old", folder));

        var importPath = Path.Combine(directory.Path, "incoming.json");
        File.WriteAllText(importPath, """
            [
              {
                "Name": "NewOnly",
                "Directory": "C:\\\\New"
              }
            ]
            """);

        var result = repository.ImportReplace(importPath);

        Assert.True(result.Success);
        Assert.Single(repository.GetShortcuts());
        Assert.Equal("NewOnly", repository.GetShortcuts()[0].Name);
    }

    [Fact]
    public void CountImportNameConflicts_CountsOverlappingNames()
    {
        using var directory = new TempDataDirectory();
        using var repository = new ShortcutRepository(directory.Path);
        var folder = Path.Combine(directory.Path, "Alpha");
        Directory.CreateDirectory(folder);
        repository.Upsert(CreateShortcut("Alpha", folder));

        var imported =
            new[]
            {
                CreateShortcut("Alpha", folder),
                CreateShortcut("Beta", folder),
            };

        Assert.Equal(1, repository.CountImportNameConflicts(imported));
    }

    [Fact]
    public void TryReadImportFile_RejectsMissingFile()
    {
        using var directory = new TempDataDirectory();
        using var repository = new ShortcutRepository(directory.Path);

        Assert.False(repository.TryReadImportFile(
            Path.Combine(directory.Path, "missing.json"),
            out _,
            out var error));

        Assert.Contains("not found", error, StringComparison.OrdinalIgnoreCase);
    }

    private static TerminalShortcut CreateShortcut(string name, string directory) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
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
