using QuickShell.Abstractions;
using QuickShell.Models;
using QuickShell.Services;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;

namespace QuickShell.Core.Tests;

public sealed class ShortcutLaunchExecutorCacheTests : IDisposable
{
    public ShortcutLaunchExecutorCacheTests()
    {
        LaunchExecutorTestEnvironment.Apply();
    }

    public void Dispose()
    {
        LaunchExecutorTestEnvironment.Reset();
    }

    private static (ShortcutLaunchExecutor Executor, FakeShortcutRepository Repository, FakeProcessStarter ProcessStarter) CreateExecutor(
        TerminalShortcut shortcut,
        long version = 1,
        FakeProcessStarter? processStarter = null,
        FakeWorkspaceEnvironmentProbe? probe = null,
        WorkspaceGitOperations? git = null,
        IWorktreeBranchTargetStore? targetStore = null)
    {
        var repo = new FakeShortcutRepository([shortcut]) { Version = version };
        var bundle = LaunchTestServices.CreateBundle(
            processStarter: processStarter,
            probe: probe,
            git: git,
            repository: repo,
            targetStore: targetStore);
        return (bundle.Executor, repo, bundle.ProcessStarter);
    }

    private static TerminalShortcut CreateShortcut(Action<TerminalShortcut>? configure = null)
    {
        var shortcut = new TerminalShortcut
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Test workspace",
            Directory = Environment.CurrentDirectory,
            Launches =
            [
                new WorkspaceEntry
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Label = "Main",
                    Terminal = "wt",
                    Command = "echo ready",
                    IsEnabled = true,
                    Order = 0,
                },
            ],
        };
        configure?.Invoke(shortcut);
        return shortcut;
    }

    private static int CacheBuildCount(ShortcutLaunchResult result) =>
        result.Diagnostics?.Entries.Count(e => e.Kind == LaunchDiagnosticKind.PlanCacheBuild) ?? 0;

    private static int CacheHitCount(ShortcutLaunchResult result) =>
        result.Diagnostics?.Entries.Count(e => e.Kind == LaunchDiagnosticKind.PlanCacheHit) ?? 0;

    [Fact]
    public void RepeatedLaunch_SameKey_BuildsPlanOnce()
    {
        var shortcut = CreateShortcut();
        var (executor, _, starter) = CreateExecutor(shortcut);

        var first = executor.Launch(shortcut, "wt", TerminalHostIds.DefaultProfile);
        var second = executor.Launch(shortcut, "wt", TerminalHostIds.DefaultProfile);

        Assert.Equal(1, CacheBuildCount(first));
        Assert.Equal(1, CacheBuildCount(second) + CacheHitCount(second));
        Assert.Equal(0, CacheBuildCount(second));
        Assert.Equal(2, starter.Started.Count);
        Assert.True(first.Dismiss);
        Assert.True(second.Dismiss);
    }

    [Fact]
    public void StructuralRepositoryVersionChange_CausesCacheMiss()
    {
        var shortcut = CreateShortcut();
        var (executor, repo, starter) = CreateExecutor(shortcut, version: 1);

        var first = executor.Launch(shortcut, "wt", TerminalHostIds.DefaultProfile);
        repo.BumpVersion();
        var second = executor.Launch(shortcut, "wt", TerminalHostIds.DefaultProfile);

        Assert.Equal(1, CacheBuildCount(first));
        Assert.Equal(1, CacheBuildCount(second));
        Assert.Equal(0, CacheHitCount(second));
        Assert.Equal(2, starter.Started.Count);
    }

    [Fact]
    public void UsageOnlyVersionChange_DoesNotCauseCacheMiss()
    {
        // Regression test: MarkUsed (called after every successful launch) used to bump
        // the same repository version the launch plan cache keyed on, so a repeat launch
        // more than ~2s after the previous one always missed the cache. The cache must key
        // on structural changes only and ignore usage-only version bumps.
        var shortcut = CreateShortcut();
        var (executor, repo, starter) = CreateExecutor(shortcut, version: 1);

        var first = executor.Launch(shortcut, "wt", TerminalHostIds.DefaultProfile);
        repo.BumpUsageOnlyVersion();
        var second = executor.Launch(shortcut, "wt", TerminalHostIds.DefaultProfile);

        Assert.Equal(1, CacheBuildCount(first));
        Assert.Equal(0, CacheBuildCount(second));
        Assert.Equal(1, CacheHitCount(second));
        Assert.Equal(2, starter.Started.Count);
    }

    [Fact]
    public void WorkspaceDeletion_CannotLaunchCachedPlan()
    {
        var shortcut = CreateShortcut();
        var (executor, repo, starter) = CreateExecutor(shortcut);

        var first = executor.Launch(shortcut, "wt", TerminalHostIds.DefaultProfile);
        repo.Clear();
        repo.BumpVersion();
        var second = executor.Launch(shortcut, "wt", TerminalHostIds.DefaultProfile);

        Assert.True(first.Dismiss);
        Assert.False(second.Dismiss);
        Assert.Contains("not found", second.StayOpenMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Single(starter.Started);
    }

    [Fact]
    public void WorkspaceEdit_InvalidatesOldPlan()
    {
        var shortcut = CreateShortcut();
        var (executor, repo, starter) = CreateExecutor(shortcut);

        var first = executor.Launch(shortcut, "wt", TerminalHostIds.DefaultProfile);
        shortcut.Launches[0].Command = "npm run dev";
        repo.Upsert(shortcut);
        repo.BumpVersion();
        var second = executor.Launch(shortcut, "wt", TerminalHostIds.DefaultProfile);

        Assert.True(first.Dismiss);
        Assert.True(second.Dismiss);
        Assert.Equal(2, CacheBuildCount(first) + CacheBuildCount(second));
        Assert.Equal(2, starter.Started.Count);
        Assert.Contains("npm", starter.Started[1].Arguments, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TerminalApplicationSettingChange_InvalidatesPlan()
    {
        var shortcut = CreateShortcut(s => s.Launches[0].Terminal = "default");
        var (executor, _, starter) = CreateExecutor(shortcut);

        var wtResult = executor.Launch(shortcut, "wt", TerminalHostIds.DefaultProfile);
        var conhostResult = executor.Launch(shortcut, "conhost", TerminalHostIds.DefaultProfile);

        Assert.Equal(1, CacheBuildCount(wtResult));
        Assert.Equal(1, CacheBuildCount(conhostResult));
        Assert.Equal(2, starter.Started.Count);
        Assert.Contains("wt.exe", starter.Started[0].FileName, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("wt.exe", starter.Started[1].FileName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DefaultProfileChange_InvalidatesPlan()
    {
        var shortcut = CreateShortcut(s => s.Launches[0].Terminal = "default");
        var (executor, _, starter) = CreateExecutor(shortcut);

        var defaultProfile = executor.Launch(shortcut, "wt", TerminalHostIds.DefaultProfile);
        var cmdProfile = executor.Launch(shortcut, "wt", "cmd");

        Assert.Equal(1, CacheBuildCount(defaultProfile));
        Assert.Equal(1, CacheBuildCount(cmdProfile));
        Assert.Equal(2, starter.Started.Count);
    }

    [Fact]
    public void LaunchEntryId_SelectsCorrectCacheEntry()
    {
        var shortcut = new TerminalShortcut
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Multi",
            Directory = Environment.CurrentDirectory,
            Launches =
            [
                new WorkspaceEntry { Id = "api", Label = "API", Terminal = "wt", Command = "echo api", IsEnabled = true, Order = 0 },
                new WorkspaceEntry { Id = "web", Label = "Web", Terminal = "wt", Command = "echo web", IsEnabled = true, Order = 1 },
            ],
        };

        var (executor, _, starter) = CreateExecutor(shortcut);

        var apiEntry = shortcut.Launches[0];
        var webEntry = shortcut.Launches[1];

        var apiResult = executor.LaunchEntry(shortcut, apiEntry, "wt", TerminalHostIds.DefaultProfile);
        var webResult = executor.LaunchEntry(shortcut, webEntry, "wt", TerminalHostIds.DefaultProfile);
        var apiAgain = executor.LaunchEntry(shortcut, apiEntry, "wt", TerminalHostIds.DefaultProfile);

        Assert.Equal(1, CacheBuildCount(apiResult));
        Assert.Equal(1, CacheBuildCount(webResult));
        Assert.Equal(0, CacheBuildCount(apiAgain));
        Assert.Equal(1, CacheHitCount(apiAgain));
        Assert.Equal(3, starter.Started.Count);
    }

    [Fact]
    public void RunAsAdminAndRunAsStandard_UseSeparateKeys()
    {
        var shortcut = CreateShortcut();
        var (executor, _, starter) = CreateExecutor(shortcut);

        var adminResult = executor.Launch(shortcut, "wt", TerminalHostIds.DefaultProfile, new ShortcutLaunchOptions(RunAsAdmin: true));
        var standardResult = executor.Launch(shortcut, "wt", TerminalHostIds.DefaultProfile, new ShortcutLaunchOptions(RunAsStandard: true));

        Assert.Equal(1, CacheBuildCount(adminResult));
        Assert.Equal(1, CacheBuildCount(standardResult));
        Assert.Equal(2, starter.Started.Count);
        Assert.Contains(starter.Started, s => s.Verb == "runas");
        Assert.Contains(starter.Started, s => string.IsNullOrEmpty(s.Verb));
    }

    [Fact]
    public void HealthReevaluatedOnEveryLaunch()
    {
        var shortcut = CreateShortcut(s => s.Launches[0].Command = "dotnet run");
        var probe = LaunchTestServices.CreateHealthyProbe();
        var (executor, _, starter) = CreateExecutor(shortcut, probe: probe);

        var first = executor.Launch(shortcut, "wt", TerminalHostIds.DefaultProfile);

        probe.ExecutableExistsHandler = _ => false;
        var second = executor.Launch(shortcut, "wt", TerminalHostIds.DefaultProfile);

        Assert.True(first.Dismiss);
        Assert.False(second.Dismiss);
        Assert.Single(starter.Started);
    }

    [Fact]
    public void DirectoryExistence_CheckedOnEveryLaunch()
    {
        var shortcut = CreateShortcut();
        var (executor, repo, starter) = CreateExecutor(shortcut);

        var first = executor.Launch(shortcut, "wt", TerminalHostIds.DefaultProfile);

        shortcut.Directory = @"C:\does-not-exist-quickshell-test";
        repo.Upsert(shortcut);
        var second = executor.Launch(shortcut, "wt", TerminalHostIds.DefaultProfile);

        Assert.True(first.Dismiss);
        Assert.False(second.Dismiss);
        Assert.Contains("folder not found", second.StayOpenMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Single(starter.Started);
    }

    [Fact]
    public void CachedPlan_UsesNormalizedLaunchDirectory()
    {
        var root = Path.Join(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var child = Path.Join(root, "child");
        Directory.CreateDirectory(child);

        try
        {
            var rawDirectory = Path.Join(child, "..");
            var normalizedDirectory = Path.GetFullPath(rawDirectory);
            var shortcut = CreateShortcut(s => s.Directory = rawDirectory);
            var (executor, _, starter) = CreateExecutor(shortcut);

            var first = executor.Launch(shortcut, "wt", TerminalHostIds.DefaultProfile);
            var second = executor.Launch(shortcut, "wt", TerminalHostIds.DefaultProfile);

            Assert.True(first.Dismiss, first.StayOpenMessage);
            Assert.True(second.Dismiss, second.StayOpenMessage);
            Assert.Equal(1, CacheHitCount(second));
            Assert.Equal(2, starter.Started.Count);
            Assert.All(starter.Started, start =>
            {
                Assert.Contains(normalizedDirectory, start.Arguments, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("..", start.Arguments, StringComparison.Ordinal);
            });
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void GitState_NotCached()
    {
        var shortcut = CreateShortcut();
        var gitStatus = new ConcurrentDictionary<string, WorkspaceGitStatus>();
        gitStatus[shortcut.Directory] = new WorkspaceGitStatus("develop", true, false);
        var git = LaunchTestServices.CreateGit(
            runGit: (_, args) => args switch
            {
                ["rev-parse", "--is-inside-work-tree"] => new GitCommandResult(0, "true", string.Empty, false),
                ["rev-parse", "--show-toplevel"] => new GitCommandResult(0, shortcut.Directory, string.Empty, false),
                _ => GitCommandResult.Failed,
            },
            getStatus: dir => gitStatus.TryGetValue(dir, out var status) ? status : null);
        var (executor, _, starter) = CreateExecutor(
            shortcut,
            git: git,
            targetStore: new FakeWorktreeBranchTargetStore(_ => "main"));

        var first = executor.Launch(shortcut, "wt", TerminalHostIds.DefaultProfile);

        gitStatus[shortcut.Directory] = new WorkspaceGitStatus("main", false, false);
        var second = executor.Launch(shortcut, "wt", TerminalHostIds.DefaultProfile);

        Assert.False(first.Dismiss);
        Assert.Contains("uncommitted", first.StayOpenMessage, StringComparison.OrdinalIgnoreCase);
        Assert.True(second.Dismiss);
        Assert.Single(starter.Started);
    }

    [Fact]
    public void CompanionExecutableAvailability_CheckedOnEveryLaunch()
    {
        var notepadPath = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.System), "notepad.exe");
        var shortcut = CreateShortcut(s =>
        {
            s.CompanionApps =
            [
                new CompanionAppEntry { Id = "editor", Path = notepadPath, Arguments = ".", OpenOnLaunch = true },
            ];
        });

        var starter = LaunchTestServices.CreateProcessStarter();
        var companionAttempts = 0;
        starter.ShouldSucceed = startInfo =>
        {
            if (startInfo.FileName.Contains("notepad", StringComparison.OrdinalIgnoreCase))
            {
                return Interlocked.Increment(ref companionAttempts) > 1;
            }

            return true;
        };

        var (executor, _, _) = CreateExecutor(shortcut, processStarter: starter);

        var first = executor.Launch(shortcut, "wt", TerminalHostIds.DefaultProfile);
        var second = executor.Launch(shortcut, "wt", TerminalHostIds.DefaultProfile);

        Assert.False(first.Dismiss);
        Assert.Contains("Companion app", first.StayOpenMessage, StringComparison.OrdinalIgnoreCase);
        Assert.True(first.Diagnostics?.Entries.Any(e => e.Kind == LaunchDiagnosticKind.CompanionAppUnavailable));
        Assert.True(second.Dismiss);
        Assert.True(second.Diagnostics?.Entries.Any(e => e.Kind == LaunchDiagnosticKind.CompanionAppLaunched));
        Assert.Equal(2, starter.Started.Count(s => s.FileName.Contains("notepad", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void MultiTabGrouping_BehaviorallyIdenticalAcrossCacheHit()
    {
        var shortcut = new TerminalShortcut
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Tabs",
            Directory = Environment.CurrentDirectory,
            Launches =
            [
                new WorkspaceEntry { Id = "1", Label = "One", Terminal = "wt", Command = "echo 1", IsEnabled = true, Order = 0 },
                new WorkspaceEntry { Id = "2", Label = "Two", Terminal = "wt", Command = "echo 2", IsEnabled = true, Order = 1 },
                new WorkspaceEntry { Id = "3", Label = "Three", Terminal = "wt", Command = "echo 3", IsEnabled = true, Order = 2 },
                new WorkspaceEntry { Id = "4", Label = "Four", Terminal = "wt", Command = "echo 4", IsEnabled = true, Order = 3 },
                new WorkspaceEntry { Id = "5", Label = "Five", Terminal = "wt", Command = "echo 5", IsEnabled = true, Order = 4 },
            ],
        };

        var (executor, _, starter) = CreateExecutor(shortcut);

        var first = executor.Launch(shortcut, "wt", TerminalHostIds.DefaultProfile);
        var second = executor.Launch(shortcut, "wt", TerminalHostIds.DefaultProfile);

        Assert.True(first.Dismiss);
        Assert.True(second.Dismiss);
        Assert.Equal(2, starter.Started.Count);
        Assert.Equal("wt.exe", starter.Started[0].FileName);
        Assert.Equal(4, CountOccurrences(starter.Started[0].Arguments ?? string.Empty, "; new-tab"));
        Assert.Equal("wt.exe", starter.Started[1].FileName);
        Assert.Equal(4, CountOccurrences(starter.Started[1].Arguments ?? string.Empty, "; new-tab"));
    }

    [Fact]
    public void FailedProcessStart_DoesNotPoisonCachedPlan()
    {
        var shortcut = CreateShortcut();
        var starter = LaunchTestServices.CreateProcessStarter();
        var wtStarts = 0;
        starter.ShouldSucceed = startInfo =>
        {
            if (startInfo.FileName.Contains("wt", StringComparison.OrdinalIgnoreCase))
            {
                return Interlocked.Increment(ref wtStarts) > 1;
            }

            return true;
        };

        var (executor, _, _) = CreateExecutor(shortcut, processStarter: starter);

        var first = executor.Launch(shortcut, "wt", TerminalHostIds.DefaultProfile);
        var second = executor.Launch(shortcut, "wt", TerminalHostIds.DefaultProfile);

        Assert.False(first.Dismiss);
        Assert.True(second.Dismiss);
    }

    [Fact]
    public void ConcurrentSameKey_RequestsAreSafe()
    {
        var shortcut = CreateShortcut();
        var (executor, _, starter) = CreateExecutor(shortcut);

        var results = new List<ShortcutLaunchResult>();
        var lockObj = new object();
        Parallel.For(0, 10, _ =>
        {
            var result = executor.Launch(shortcut, "wt", TerminalHostIds.DefaultProfile);
            lock (lockObj)
            {
                results.Add(result);
            }
        });

        Assert.Equal(10, starter.Started.Count);
        Assert.All(results, r => Assert.True(r.Dismiss));
        Assert.Equal(1, results.Sum(r => CacheBuildCount(r)));
    }

    private static int CountOccurrences(string haystack, string needle) =>
        needle.Length == 0 ? 0 : (haystack.Length - haystack.Replace(needle, string.Empty).Length) / needle.Length;
}
