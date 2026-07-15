using System.Diagnostics;
using QuickShell.Models;
using QuickShell.Services;

namespace QuickShell.Core.Tests;

// Shares a collection with TerminalLauncherTests (see TerminalLauncherOverrideIsolation)
// because both mutate the process-wide static TerminalLauncher.StartProcessOverride
// seam, which two test classes running in parallel could otherwise clobber.
[Collection(TerminalLauncherOverrideIsolation.Name)]
public sealed class ShortcutLaunchExecutorTests : IDisposable
{
    public ShortcutLaunchExecutorTests()
    {
        LaunchExecutorTestEnvironment.Apply();
    }

    public void Dispose()
    {
        LaunchExecutorTestEnvironment.Reset();
        TerminalLauncher.StartProcessOverride = null;
    }

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
                new WorkspaceEntry { Id = Guid.NewGuid().ToString("N"), Label = "API", Terminal = "wt", Command = "dotnet run", IsEnabled = true, Order = 0 },
                new WorkspaceEntry { Id = Guid.NewGuid().ToString("N"), Label = "Web", Terminal = "wt", Command = "npm run dev", IsEnabled = true, Order = 1 },
                new WorkspaceEntry { Id = Guid.NewGuid().ToString("N"), Label = "Worker", Terminal = "wt", Command = "npm run worker", IsEnabled = true, Order = 2 },
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
                new WorkspaceEntry { Id = Guid.NewGuid().ToString("N"), Label = "Admin", Terminal = "wt", Command = "cmd", RunAsAdmin = true, IsEnabled = true, Order = 0 },
                new WorkspaceEntry { Id = Guid.NewGuid().ToString("N"), Label = "Normal", Terminal = "wt", Command = "cmd", RunAsAdmin = false, IsEnabled = true, Order = 1 },
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
    public void LaunchAll_MixedWindowsTerminalAndStandaloneShells_OpenAsSingleProcessWithTabs()
    {
        var directory = Environment.CurrentDirectory;
        var shortcut = new TerminalShortcut
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Mixed hosts",
            Directory = directory,
            Launches =
            [
                new WorkspaceEntry { Id = Guid.NewGuid().ToString("N"), Label = "API", Terminal = "wt", Command = "cmd", IsEnabled = true, Order = 0 },
                new WorkspaceEntry { Id = Guid.NewGuid().ToString("N"), Label = "Web", Terminal = "wt", Command = "cmd", IsEnabled = true, Order = 1 },
                new WorkspaceEntry { Id = Guid.NewGuid().ToString("N"), Label = "Legacy", Terminal = "cmd", Command = "cmd", IsEnabled = true, Order = 2 },
            ],
        };

        var captured = new List<ProcessStartInfo>();
        TerminalLauncher.StartProcessOverride = info => { captured.Add(info); return true; };
        try
        {
            ShortcutLaunchExecutor.Launch(shortcut, "wt", "default");

            Assert.Single(captured);
            Assert.Equal("wt.exe", captured[0].FileName);
            var arguments = captured[0].Arguments ?? string.Empty;
            Assert.Equal(2, CountOccurrences(arguments, "; new-tab"));
            Assert.Contains("cmd.exe", arguments, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TerminalLauncher.StartProcessOverride = null;
        }
    }

    [Fact]
    public void LaunchAll_SeparateWindowsForMultiLaunch_OpensMultipleProcesses()
    {
        var directory = Environment.CurrentDirectory;
        var shortcut = new TerminalShortcut
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Separate windows",
            Directory = directory,
            Launches =
            [
                new WorkspaceEntry { Id = Guid.NewGuid().ToString("N"), Label = "API", Terminal = "wt", Command = "dotnet run", IsEnabled = true, Order = 0 },
                new WorkspaceEntry { Id = Guid.NewGuid().ToString("N"), Label = "Web", Terminal = "wt", Command = "npm run dev", IsEnabled = true, Order = 1 },
            ],
        };

        var captured = new List<ProcessStartInfo>();
        TerminalLauncher.StartProcessOverride = info => { captured.Add(info); return true; };
        try
        {
            ShortcutLaunchExecutor.Launch(
                shortcut,
                "wt",
                "default",
                new ShortcutLaunchOptions(SeparateWindowsForMultiLaunch: true));

            Assert.Equal(2, captured.Count);
            Assert.All(captured, c => Assert.Equal("wt.exe", c.FileName));
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
        Assert.NotNull(result.Diagnostics);
        Assert.Contains(
            result.Diagnostics.Entries,
            entry => entry.Kind == LaunchDiagnosticKind.HealthError
                && entry.Severity == LaunchDiagnosticSeverity.Error);
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

    [Fact]
    public void Launch_RecordsTerminalAndCommandDiagnostics()
    {
        var shortcut = new TerminalShortcut
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Diagnostics",
            Directory = Environment.CurrentDirectory,
            Launches =
            [
                new WorkspaceEntry
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Label = "Dev",
                    Command = "echo ready",
                    Terminal = "default",
                    IsEnabled = true,
                    Order = 0,
                },
            ],
        };

        TerminalLauncher.StartProcessOverride = _ => true;
        try
        {
            var result = ShortcutLaunchExecutor.Launch(
                shortcut,
                TerminalHostIds.WindowsConsoleHost,
                "cmd");

            Assert.True(result.Dismiss);
            Assert.NotNull(result.Diagnostics);
            Assert.Contains(
                result.Diagnostics.Entries,
                entry => entry.Kind == LaunchDiagnosticKind.TerminalLaunched
                    && entry.Title.Contains("Dev", StringComparison.Ordinal));
            Assert.Contains(
                result.Diagnostics.Entries,
                entry => entry.Kind == LaunchDiagnosticKind.CommandHandoff
                    && entry.Detail == "echo ready");
            Assert.Contains(
                "Command exit status is not monitored.",
                result.Diagnostics.ToClipboardText(),
                StringComparison.Ordinal);
        }
        finally
        {
            TerminalLauncher.StartProcessOverride = null;
        }
    }
}

[Collection(TerminalLauncherOverrideIsolation.Name)]
public sealed class WorkspaceDevServerActionsTests : IDisposable
{
    public WorkspaceDevServerActionsTests()
    {
        LaunchExecutorTestEnvironment.Apply();
    }

    public void Dispose()
    {
        LaunchExecutorTestEnvironment.Reset();
        TerminalLauncher.StartProcessOverride = null;
        WorkspaceDevServerActions.TryOpenOverride = null;
    }

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

        var devServerOpened = false;
        WorkspaceDevServerActions.TryOpenOverride = _ =>
        {
            devServerOpened = true;
            return true;
        };
        TerminalLauncher.StartProcessOverride = _ => true;
        try
        {
            var result = ShortcutLaunchExecutor.LaunchEntry(
                shortcut,
                shortcut.Launches[0],
                TerminalHostIds.WindowsConsoleHost,
                "cmd");

            Assert.True(result.Dismiss, result.StayOpenMessage);
            Assert.False(devServerOpened);
        }
        finally
        {
            TerminalLauncher.StartProcessOverride = null;
            WorkspaceDevServerActions.TryOpenOverride = null;
        }
    }
}
