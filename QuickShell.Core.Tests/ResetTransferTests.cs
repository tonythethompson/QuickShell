using QuickShell.Models;
using QuickShell.Services;

namespace QuickShell.Core.Tests;

public sealed class ResetTransferTests
{
    [Fact]
    public void ResetAll_EmptyWorkspaces_ReturnsNoOpMessage()
    {
        using var directory = new TempDataDirectory();
        using var repository = new ShortcutRepository(directory.Path);

        var result = repository.ResetAll();

        Assert.True(result.Success);
        Assert.Contains("No workspaces", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(repository.GetShortcuts());
    }

    [Fact]
    public void ResetAll_ClearsWorkspaces_AndUndoRestores()
    {
        using var directory = new TempDataDirectory();
        using var repository = new ShortcutRepository(directory.Path);
        var workspaceDirectory = Path.Combine(directory.Path, "Alpha");
        Directory.CreateDirectory(workspaceDirectory);

        repository.Upsert(CreateShortcut("Alpha", workspaceDirectory));
        Assert.Single(repository.GetShortcuts());

        var result = repository.ResetAll();

        Assert.True(result.Success);
        Assert.Empty(repository.GetShortcuts());
        Assert.True(File.Exists(Path.Combine(directory.Path, "shortcuts.json")));
        Assert.Equal("[]", File.ReadAllText(Path.Combine(directory.Path, "shortcuts.json")).Trim());

        Assert.True(repository.CanUndo);
        Assert.True(repository.Undo());
        Assert.Single(repository.GetShortcuts());
        Assert.Equal("Alpha", repository.GetShortcuts()[0].Name);
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
