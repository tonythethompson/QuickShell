using System.Diagnostics;
using QuickShell.Models;
using QuickShell.Services;

namespace QuickShell.Core.Tests;

/// <summary>
/// Serializes tests that mutate the shared static <see cref="TerminalLauncher.StartProcessOverride"/>
/// hook so they can't race with each other under xUnit's default cross-class parallelization.
/// </summary>
[CollectionDefinition("TerminalLauncher StartProcessOverride", DisableParallelization = true)]
public sealed class TerminalLauncherOverrideCollection
{
}

[Collection("TerminalLauncher StartProcessOverride")]
public sealed class ShortcutLaunchExecutorTests
{
    [Fact]
    public void LaunchAll_ThreeWindowsTerminalEntries_OpenAsSingleProcessWithTabs()
    {
        var directory = Environment.CurrentDirectory;
        var shortcut = new TerminalShortcut
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Full stack",
            Directory = directory,
            Launches =
            [
                new WorkspaceEntry { Id = Guid.NewGuid().ToString("N"), Label = "API", Terminal = "wt", WtProfile = "Profile1", Command = "dotnet run", IsEnabled = true, Order = 0 },
                new WorkspaceEntry { Id = Guid.NewGuid().ToString("N"), Label = "Web", Terminal = "wt", WtProfile = "Profile2", Command = "npm run dev", IsEnabled = true, Order = 1 },
                new WorkspaceEntry { Id = Guid.NewGuid().ToString("N"), Label = "Worker", Terminal = "wt", WtProfile = "Profile3", Command = "npm run worker", IsEnabled = true, Order = 2 },
            ],
        };

        var captured = new List<ProcessStartInfo>();
        TerminalLauncher.StartProcessOverride = info => { captured.Add(info); return true; };
        try
        {
            ShortcutLaunchExecutor.Launch(shortcut, "wt", "default");

            Assert.Single(captured);
            Assert.Equal("wt.exe", captured[0].FileName);
            Assert.Equal(2, CountOccurrences(captured[0].Arguments ?? string.Empty, "; new-tab"));
        }
        finally
        {
            TerminalLauncher.StartProcessOverride = null;
        }
    }

    [Fact]
    public void LaunchAll_MixedElevation_OpensTwoProcesses()
    {
        var directory = Environment.CurrentDirectory;
        var shortcut = new TerminalShortcut
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Mixed elevation",
            Directory = directory,
            Launches =
            [
                new WorkspaceEntry { Id = Guid.NewGuid().ToString("N"), Label = "Admin", Terminal = "wt", WtProfile = "Profile1", Command = "cmd", RunAsAdmin = true, IsEnabled = true, Order = 0 },
                new WorkspaceEntry { Id = Guid.NewGuid().ToString("N"), Label = "Normal", Terminal = "wt", WtProfile = "Profile2", Command = "cmd", RunAsAdmin = false, IsEnabled = true, Order = 1 },
            ],
        };

        var captured = new List<ProcessStartInfo>();
        TerminalLauncher.StartProcessOverride = info => { captured.Add(info); return true; };
        try
        {
            ShortcutLaunchExecutor.Launch(shortcut, "wt", "default");

            Assert.Equal(2, captured.Count);
            Assert.Contains(captured, c => c.Verb == "runas");
            Assert.Contains(captured, c => string.IsNullOrEmpty(c.Verb));
        }
        finally
        {
            TerminalLauncher.StartProcessOverride = null;
        }
    }

    [Fact]
    public void LaunchAll_NonWindowsTerminalFallbackMixedWithWt_OpensTwoProcesses()
    {
        var directory = Environment.CurrentDirectory;
        var shortcut = new TerminalShortcut
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Mixed hosts",
            Directory = directory,
            Launches =
            [
                new WorkspaceEntry { Id = Guid.NewGuid().ToString("N"), Label = "API", Terminal = "wt", WtProfile = "Profile1", Command = "cmd", IsEnabled = true, Order = 0 },
                new WorkspaceEntry { Id = Guid.NewGuid().ToString("N"), Label = "Web", Terminal = "wt", WtProfile = "Profile2", Command = "cmd", IsEnabled = true, Order = 1 },
                new WorkspaceEntry { Id = Guid.NewGuid().ToString("N"), Label = "Legacy", Terminal = "cmd", Command = "cmd", IsEnabled = true, Order = 2 },
            ],
        };

        var captured = new List<ProcessStartInfo>();
        TerminalLauncher.StartProcessOverride = info => { captured.Add(info); return true; };
        try
        {
            ShortcutLaunchExecutor.Launch(shortcut, "wt", "default");

            Assert.Equal(2, captured.Count);
            Assert.Contains(captured, c => c.FileName == "wt.exe" && (c.Arguments ?? string.Empty).Contains("; new-tab", StringComparison.Ordinal));
            Assert.Contains(captured, c => c.FileName == "cmd.exe");
        }
        finally
        {
            TerminalLauncher.StartProcessOverride = null;
        }
    }

    private static int CountOccurrences(string haystack, string needle) =>
        needle.Length == 0 ? 0 : (haystack.Length - haystack.Replace(needle, string.Empty).Length) / needle.Length;

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

[Collection("TerminalLauncher StartProcessOverride")]
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

        TerminalLauncher.StartProcessOverride = _ => true;
        try
        {
            var result = ShortcutLaunchExecutor.LaunchEntry(
                shortcut,
                shortcut.Launches[0],
                "wt",
                "default");

            Assert.True(result.Dismiss);
        }
        finally
        {
            TerminalLauncher.StartProcessOverride = null;
        }
    }
}
