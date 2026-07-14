using QuickShell.Models;
using QuickShell.Services;

namespace QuickShell.Core.Tests;

public sealed class ShortcutRepositoryWorkspacesChangedTests
{
    [Fact]
    public void Upsert_RaisesWorkspacesChanged()
    {
        using var directory = new TempDataDirectory();
        using var repository = new ShortcutRepository(directory.Path);
        var workspaceDirectory = Path.Join(directory.Path, "Alpha");
        Directory.CreateDirectory(workspaceDirectory);

        var raised = 0;
        repository.WorkspacesChanged += (_, _) => raised++;

        repository.Upsert(CreateShortcut("Alpha", workspaceDirectory));

        Assert.Equal(1, raised);
    }

    [Fact]
    public void Delete_RaisesWorkspacesChanged()
    {
        using var directory = new TempDataDirectory();
        using var repository = new ShortcutRepository(directory.Path);
        var workspaceDirectory = Path.Join(directory.Path, "Alpha");
        Directory.CreateDirectory(workspaceDirectory);
        repository.Upsert(CreateShortcut("Alpha", workspaceDirectory));

        var raised = 0;
        repository.WorkspacesChanged += (_, _) => raised++;

        Assert.True(repository.Delete("Alpha"));
        Assert.Equal(1, raised);
    }

    [Fact]
    public void ImportMerge_RaisesWorkspacesChanged()
    {
        using var directory = new TempDataDirectory();
        using var repository = new ShortcutRepository(directory.Path);
        var importPath = Path.Join(directory.Path, "incoming.json");
        File.WriteAllText(importPath, """
            [
              { "Name": "Beta", "Directory": "C:\\\\Other" }
            ]
            """);

        var raised = 0;
        repository.WorkspacesChanged += (_, _) => raised++;

        var result = repository.ImportMerge(importPath);

        Assert.True(result.Success);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void ResetAll_RaisesWorkspacesChanged()
    {
        using var directory = new TempDataDirectory();
        using var repository = new ShortcutRepository(directory.Path);
        var workspaceDirectory = Path.Join(directory.Path, "Alpha");
        Directory.CreateDirectory(workspaceDirectory);
        repository.Upsert(CreateShortcut("Alpha", workspaceDirectory));

        var raised = 0;
        repository.WorkspacesChanged += (_, _) => raised++;

        var result = repository.ResetAll();

        Assert.True(result.Success);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void MarkUsed_DoesNotRaiseWorkspacesChanged()
    {
        using var directory = new TempDataDirectory();
        using var repository = new ShortcutRepository(directory.Path);
        var workspaceDirectory = Path.Join(directory.Path, "Alpha");
        Directory.CreateDirectory(workspaceDirectory);
        var shortcut = CreateShortcut("Alpha", workspaceDirectory);
        repository.Upsert(shortcut);

        var raised = 0;
        repository.WorkspacesChanged += (_, _) => raised++;

        repository.MarkUsed(shortcut.Id);
        repository.FlushPendingWrites();

        Assert.Equal(0, raised);
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
            Path = System.IO.Path.Join(System.IO.Path.GetTempPath(), "quickshell-tests", Guid.NewGuid().ToString("N"));
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
