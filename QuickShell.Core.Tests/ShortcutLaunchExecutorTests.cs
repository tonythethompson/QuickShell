using System.Diagnostics;
using QuickShell.Models;
using QuickShell.Services;

namespace QuickShell.Core.Tests;

[Collection(TerminalLauncherOverrideIsolation.Name)]
public sealed class ShortcutLaunchExecutorTests : IDisposable
{
    public ShortcutLaunchExecutorTests()
    {
        LaunchExecutorTestEnvironment.Apply();
        CompanionAppPreference.WriteLastUsedOverride = _ => { };
    }

    public void Dispose()
    {
        CompanionAppPreference.WriteLastUsedOverride = null;
        LaunchExecutorTestEnvironment.Reset();
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

        var bundle = LaunchTestServices.CreateBundle();
        bundle.Executor.Launch(shortcut, "wt", "default");

        Assert.Single(bundle.ProcessStarter.Started);
        Assert.Equal("wt.exe", bundle.ProcessStarter.Started[0].FileName);
        Assert.Equal(2, CountOccurrences(bundle.ProcessStarter.Started[0].Arguments ?? string.Empty, "; new-tab"));
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

        var bundle = LaunchTestServices.CreateBundle();
        bundle.Executor.Launch(shortcut, "wt", "default");

        Assert.Equal(2, bundle.ProcessStarter.Started.Count);
        Assert.Contains(bundle.ProcessStarter.Started, c => c.Verb == "runas");
        Assert.Contains(bundle.ProcessStarter.Started, c => string.IsNullOrEmpty(c.Verb));
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

        var bundle = LaunchTestServices.CreateBundle();
        bundle.Executor.Launch(shortcut, "wt", "default");

        Assert.Single(bundle.ProcessStarter.Started);
        Assert.Equal("wt.exe", bundle.ProcessStarter.Started[0].FileName);
        var arguments = bundle.ProcessStarter.Started[0].Arguments ?? string.Empty;
        Assert.Equal(2, CountOccurrences(arguments, "; new-tab"));
        Assert.Contains("cmd.exe", arguments, StringComparison.OrdinalIgnoreCase);
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

        var bundle = LaunchTestServices.CreateBundle();
        bundle.Executor.Launch(
            shortcut,
            "wt",
            "default",
            new ShortcutLaunchOptions(SeparateWindowsForMultiLaunch: true));

        Assert.Equal(2, bundle.ProcessStarter.Started.Count);
        Assert.All(bundle.ProcessStarter.Started, c => Assert.Equal("wt.exe", c.FileName));
    }

    [Fact]
    public void Launch_OpensTerminalBeforeCompanion()
    {
        var events = new List<string>();
        var starter = new FakeProcessStarter
        {
            ShouldSucceed = startInfo =>
            {
                events.Add(Path.GetFileName(startInfo.FileName));
                return true;
            },
        };
        var bundle = LaunchTestServices.CreateBundle(processStarter: starter);

        var result = bundle.Executor.Launch(
            BuildShortcutWithCompanion(),
            "wt",
            "default",
            new ShortcutLaunchOptions(IncludeCompanionApp: true));

        Assert.True(result.Dismiss, result.StayOpenMessage);
        Assert.Equal(["wt.exe", "explorer.exe"], events);
    }

    [Fact]
    public void Launch_TerminalFailure_SkipsCompanion()
    {
        var starter = new FakeProcessStarter
        {
            ShouldSucceed = startInfo =>
                !Path.GetFileName(startInfo.FileName).Equals("wt.exe", StringComparison.OrdinalIgnoreCase),
        };
        var bundle = LaunchTestServices.CreateBundle(processStarter: starter);

        var result = bundle.Executor.Launch(
            BuildShortcutWithCompanion(),
            "wt",
            "default",
            new ShortcutLaunchOptions(IncludeCompanionApp: true));

        Assert.False(result.Dismiss);
        Assert.False(bundle.Companion.LastLaunchAttempted);
        Assert.Single(starter.Started);
        Assert.Equal("wt.exe", Path.GetFileName(starter.Started[0].FileName), ignoreCase: true);
    }

    [Fact]
    public void Launch_CompanionFailure_RemainsSoftAndDoesNotRecordProcessStart()
    {
        var starter = new FakeProcessStarter
        {
            ShouldSucceed = startInfo =>
                !Path.GetFileName(startInfo.FileName).Equals("explorer.exe", StringComparison.OrdinalIgnoreCase),
        };
        var bundle = LaunchTestServices.CreateBundle(processStarter: starter);

        var result = bundle.Executor.Launch(
            BuildShortcutWithCompanion(),
            "wt",
            "default",
            new ShortcutLaunchOptions(IncludeCompanionApp: true));

        Assert.False(result.Dismiss);
        Assert.True(result.MarkUsed);
        Assert.NotNull(result.Diagnostics);
        Assert.True(result.Diagnostics.ProcessStartCounts.ContainsKey("wt.exe"));
        Assert.False(result.Diagnostics.ProcessStartCounts.ContainsKey("explorer.exe"));
        Assert.Contains(
            result.Diagnostics.Entries,
            entry => entry.Kind == LaunchDiagnosticKind.CompanionAppUnavailable);
    }

    [Fact]
    public void LaunchAll_OpensTerminalGroupBeforeCompanion()
    {
        var events = new List<string>();
        var starter = new FakeProcessStarter
        {
            ShouldSucceed = startInfo =>
            {
                events.Add(Path.GetFileName(startInfo.FileName));
                return true;
            },
        };
        var bundle = LaunchTestServices.CreateBundle(processStarter: starter);

        var result = bundle.Executor.Launch(
            BuildShortcutWithCompanion(launchCount: 2),
            "wt",
            "default",
            new ShortcutLaunchOptions(IncludeCompanionApp: true));

        Assert.True(result.Dismiss, result.StayOpenMessage);
        Assert.Equal(["wt.exe", "explorer.exe"], events);
    }

    [Fact]
    public void Launch_MultipleCompanions_RecordsEachActualExecutable()
    {
        var bundle = LaunchTestServices.CreateBundle();
        var shortcut = BuildShortcutWithCompanion(
            companionPaths: ["explorer.exe", "notepad.exe"]);

        var result = bundle.Executor.Launch(
            shortcut,
            "wt",
            "default",
            new ShortcutLaunchOptions(IncludeCompanionApp: true));

        Assert.NotNull(result.Diagnostics);
        Assert.Equal(1, result.Diagnostics.ProcessStartCounts["wt.exe"]);
        Assert.Equal(1, result.Diagnostics.ProcessStartCounts["explorer.exe"]);
        Assert.Equal(1, result.Diagnostics.ProcessStartCounts["notepad.exe"]);
    }

    private static TerminalShortcut BuildShortcutWithCompanion(
        int launchCount = 1,
        IReadOnlyList<string>? companionPaths = null)
    {
        companionPaths ??= ["explorer.exe"];
        var launches = Enumerable.Range(0, launchCount)
            .Select(index => new WorkspaceEntry
            {
                Id = Guid.NewGuid().ToString("N"),
                Label = $"Launch {index + 1}",
                Terminal = "wt",
                Command = $"echo {index + 1}",
                IsEnabled = true,
                Order = index,
            })
            .ToList();

        return new TerminalShortcut
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Companion ordering",
            Directory = Environment.CurrentDirectory,
            Launches = launches,
            CompanionApps = companionPaths
                .Select((path, index) => new CompanionAppEntry
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Path = path,
                    OpenOnLaunch = true,
                    Order = index,
                })
                .ToList(),
        };
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

        var bundle = LaunchTestServices.CreateBundle();
        var result = bundle.Executor.Launch(shortcut, "wt", "default");

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

        var bundle = LaunchTestServices.CreateBundle();
        var result = bundle.Executor.Launch(shortcut, "wt", "default");

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

        var bundle = LaunchTestServices.CreateBundle();
        var result = bundle.Executor.Launch(
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

        var bundle = LaunchTestServices.CreateBundle();
        var result = bundle.Executor.LaunchEntry(
            shortcut,
            shortcut.Launches[0],
            TerminalHostIds.WindowsConsoleHost,
            "cmd");

        Assert.True(result.Dismiss, result.StayOpenMessage);
        Assert.False(devServerOpened);
    }
}
