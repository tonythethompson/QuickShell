using QuickShell.Models;
using QuickShell.Services;

namespace QuickShell.Core.Tests;

public sealed class ShortcutLaunchExecutorTests
{
    [Fact]
    public void Launch_ReturnsErrorWhenDirectoryMissing()
    {
        var shortcut = new TerminalShortcut
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Missing",
            Directory = @"C:\does-not-exist-quickshell-test",
            Launches =
            [
                new WorkspaceEntry
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Label = "Main",
                    Terminal = "default",
                    IsEnabled = true,
                    Order = 0,
                },
            ],
        };

        var result = ShortcutLaunchExecutor.Launch(shortcut, "wt", "default");

        Assert.False(result.Dismiss);
        Assert.Contains("folder not found", result.StayOpenMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Launch_ReturnsErrorWhenNoEnabledLaunches()
    {
        var shortcut = new TerminalShortcut
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Disabled",
            Directory = Environment.CurrentDirectory,
            Launches =
            [
                new WorkspaceEntry
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Label = "Off",
                    Terminal = "default",
                    IsEnabled = false,
                    Order = 0,
                },
            ],
        };

        var result = ShortcutLaunchExecutor.Launch(shortcut, "wt", "default");

        Assert.False(result.Dismiss);
        Assert.Contains("no enabled launch", result.StayOpenMessage, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class WorkspaceDevServerActionsTests
{
    [Fact]
    public void ShouldOpenOnWorkspaceLaunch_RequiresExplicitOptIn()
    {
        var shortcut = new TerminalShortcut
        {
            DevServerUrl = "http://localhost:5173",
        };

        Assert.False(WorkspaceDevServerActions.ShouldOpenOnWorkspaceLaunch(shortcut));
    }

    [Fact]
    public void ShouldOpenOnWorkspaceLaunch_ReturnsTrueWhenOptedIn()
    {
        var shortcut = new TerminalShortcut
        {
            DevServerUrl = "http://localhost:5173",
            OpenDevServerOnLaunch = true,
        };

        Assert.True(WorkspaceDevServerActions.ShouldOpenOnWorkspaceLaunch(shortcut));
    }

    [Fact]
    public void LaunchEntry_DoesNotOpenDevServerWhenOptedIn()
    {
        var directory = Environment.CurrentDirectory;
        var shortcut = new TerminalShortcut
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Dev",
            Directory = directory,
            DevServerUrl = "http://localhost:5173",
            OpenDevServerOnLaunch = true,
            Launches =
            [
                new WorkspaceEntry
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Label = "Main",
                    Terminal = "default",
                    IsEnabled = true,
                    Order = 0,
                },
            ],
        };

        var result = ShortcutLaunchExecutor.LaunchEntry(
            shortcut,
            shortcut.Launches[0],
            "wt",
            "default");

        Assert.True(result.Dismiss);
    }
}
