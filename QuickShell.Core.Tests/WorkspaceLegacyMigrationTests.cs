using System.Text;
using System.Text.Json;
using QuickShell.Models;
using QuickShell.Services;

namespace QuickShell.Core.Tests;

public sealed class WorkspaceLegacyMigrationTests
{
    [Fact]
    public void TryReadLegacyWorkspaces_ConvertsRecordsToShortcuts()
    {
        using var directory = new TempDataDirectory();
        var shortcuts = new FakeShortcutRepository([]);

        var records = new List<WorkspaceDiskRecord>
        {
            new()
            {
                Id = "a1b2c3d4e5f6478990a1b2c3d4e5f678",
                Name = "Trackdub — Agents",
                Directory = @"C:\Projects\Trackdub",
                Entries =
                [
                    new WorkspaceEntry
                    {
                        Id = "b2c3d4e5f6478990a1b2c3d4e5f67890",
                        Label = "Claude",
                        Command = "claude",
                        IsEnabled = true,
                        Order = 0,
                    },
                ],
            },
        };

        var json = JsonSerializer.Serialize(records, QuickShellJsonContext.Default.ListWorkspaceDiskRecord);
        File.WriteAllBytes(Path.Combine(directory.Path, "workspaces.json"), Encoding.UTF8.GetBytes(json));

        Assert.True(WorkspaceLegacyMigration.TryReadLegacyWorkspaces(directory.Path, shortcuts, out var imported, out _));
        Assert.Single(imported);
        Assert.Equal("Trackdub — Agents", imported[0].Name);
        Assert.Single(imported[0].Launches);
        Assert.Equal("Claude", imported[0].Launches[0].Label);
    }

    [Fact]
    public void MigrationOutput_OmitsProjectShortcutId_AndWritesResolvedDirectory()
    {
        var shortcuts = new FakeShortcutRepository(
        [
            new TerminalShortcut
            {
                Id = "c1d2e3f4a5b6478990a1b2c3d4e5f601",
                Name = "Trackdub",
                Directory = @"C:\Projects\Trackdub",
            },
        ]);

        var records = new List<WorkspaceDiskRecord>
        {
            new()
            {
                Id = "a1b2c3d4e5f6478990a1b2c3d4e5f678",
                Name = "Trackdub — Agents",
                ProjectShortcutId = "c1d2e3f4a5b6478990a1b2c3d4e5f601",
                Entries =
                [
                    new WorkspaceEntry
                    {
                        Id = "b2c3d4e5f6478990a1b2c3d4e5f67890",
                        Label = "Claude",
                        Command = "claude",
                        IsEnabled = true,
                        Order = 0,
                    },
                ],
            },
        };

        using var directory = new TempDataDirectory();
        var json = JsonSerializer.Serialize(records, QuickShellJsonContext.Default.ListWorkspaceDiskRecord);
        File.WriteAllBytes(Path.Combine(directory.Path, "workspaces.json"), Encoding.UTF8.GetBytes(json));

        Assert.True(WorkspaceLegacyMigration.TryReadLegacyWorkspaces(directory.Path, shortcuts, out var imported, out _));
        Assert.Single(imported);
        Assert.Equal(@"C:\Projects\Trackdub", imported[0].Directory);
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
