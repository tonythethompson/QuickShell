using QuickShell.Abstractions;
using QuickShell.Models;
using QuickShell.Services;

namespace QuickShell.Core.Tests;

// Asserts exact entry counts on WorkspaceStatusService's process-wide cache, so it cannot run
// in parallel with anything else that captures workspace status (the perf harnesses do).
[Collection(StartupPerfIsolation.Name)]
public sealed class WorkspaceStatusSnapshotTests
{
    [Fact]
    public void Attention_BlockingHealthOutranksBranchMismatch()
    {
        var health = new WorkspaceHealthResult(
        [
            new WorkspaceHealthFinding(
                WorkspaceHealthSeverity.Error,
                WorkspaceHealthFindingKind.MissingFolder,
                "Workspace folder not found."),
        ]);
        var snapshot = new WorkspaceStatusSnapshot(
            health,
            new WorkspaceGitStatus("main", IsDirty: true, IsDetached: false),
            "feature/status",
            DateTimeOffset.UtcNow,
            IsStale: false);

        Assert.Equal(WorkspaceAttentionState.Blocking, snapshot.Attention);
        Assert.Equal("Workspace needs attention", snapshot.AttentionSummary);
    }

    [Fact]
    public void Attention_BranchMismatchIsShownWhenHealthIsClean()
    {
        var snapshot = new WorkspaceStatusSnapshot(
            new WorkspaceHealthResult([]),
            new WorkspaceGitStatus("main", IsDirty: false, IsDetached: false),
            "feature/status",
            DateTimeOffset.UtcNow,
            IsStale: false);

        Assert.True(snapshot.HasTargetMismatch);
        Assert.Equal(WorkspaceAttentionState.Branch, snapshot.Attention);
        Assert.Equal("Configured branch differs from HEAD", snapshot.AttentionSummary);
        Assert.Equal("Branch 'main' differs from configured target 'feature/status'.", snapshot.AttentionEvidence);
    }

    [Fact]
    public void AttentionEvidence_BlockingHealthShowsFindingDetail()
    {
        var health = new WorkspaceHealthResult(
        [
            new WorkspaceHealthFinding(
                WorkspaceHealthSeverity.Error,
                WorkspaceHealthFindingKind.MissingFolder,
                "Workspace folder not found.",
                @"C:\missing"),
        ]);
        var snapshot = new WorkspaceStatusSnapshot(
            health,
            Git: null,
            TargetBranch: null,
            DateTimeOffset.UtcNow,
            IsStale: false);

        Assert.Equal("Workspace folder not found. C:\\missing", snapshot.AttentionEvidence);
    }

    [Fact]
    public void AttentionEvidence_NoIssuesReportsNoCurrentIssues()
    {
        var snapshot = new WorkspaceStatusSnapshot(
            new WorkspaceHealthResult([]),
            Git: null,
            TargetBranch: null,
            DateTimeOffset.UtcNow,
            IsStale: false);

        Assert.Equal("No current issues", snapshot.AttentionEvidence);
    }

    [Fact]
    public void CaptureForList_IncludesGitBranchAttentionWhenHealthIsClean()
    {
        var root = Path.Combine(Path.GetTempPath(), "qs-status-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            WorkspaceStatusService.ResetCacheForTests();
            
            var git = LaunchTestServices.CreateGit(
                runGit: (_, args) => args switch
                {
                    ["rev-parse", "--is-inside-work-tree"] => GitSuccess("true"),
                    ["rev-parse", "--show-toplevel"] => GitSuccess(root),
                    ["status", "--porcelain=v2", "--branch"] => GitSuccess(
                        $"# branch.oid 0000000000000000000000000000000000000000{Environment.NewLine}# branch.head main"),
                    _ => GitFailure(),
                });
            var probe = LaunchTestServices.CreateHealthyProbe();
            var health = new WorkspaceHealthCheck(probe, git, new TerminalCatalog(new WtProfilesService()), new WtProfilesService());

            var shortcut = new TerminalShortcut
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = "Sample",
                Directory = root,
                Launches =
                [
                    new WorkspaceEntry
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        Label = "Main",
                        Terminal = "cmd",
                        Command = "npm run dev",
                        IsEnabled = true,
                        Order = 0,
                    },
                ],
            };

            var snapshot = WorkspaceStatusService.CaptureForList(shortcut,
                TerminalHostIds.WindowsConsoleHost,
                "cmd",
                health,
                git,
                new FakeWorktreeBranchTargetStore(_ => "feature/status"));

            Assert.Equal(WorkspaceAttentionState.Branch, snapshot.Attention);
            Assert.Equal("Configured branch differs from HEAD", snapshot.AttentionSummary);
            Assert.False(snapshot.IsStale);
        }
        finally
        {
            WorkspaceStatusService.ResetCacheForTests();
                        try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
                // Best effort.
            }
        }
    }

    [Fact]
    public void CaptureForList_IncludesRunningActivitySignal()
    {
        var root = Path.Combine(Path.GetTempPath(), "qs-status-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            WorkspaceStatusService.ResetCacheForTests();
            var probe = LaunchTestServices.CreateHealthyProbe();
            probe.PortInUseHandler = port => port == 5173;
            var git = LaunchTestServices.CreateGit();
            var health = new WorkspaceHealthCheck(probe, git, new TerminalCatalog(new WtProfilesService()), new WtProfilesService());

            var shortcut = new TerminalShortcut
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = "Sample",
                Directory = root,
                DevServerUrl = "http://localhost:5173",
                OpenDevServerOnLaunch = true,
                Launches =
                [
                    new WorkspaceEntry
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        Label = "Main",
                        Terminal = "cmd",
                        Command = "npm run dev",
                        IsEnabled = true,
                        Order = 0,
                    },
                ],
            };

            var snapshot = WorkspaceStatusService.CaptureForList(shortcut,
                TerminalHostIds.WindowsConsoleHost,
                "cmd",
                health,
                git,
                new NullWorktreeBranchTargetStore());

            Assert.Equal(WorkspaceActivityState.Running, snapshot.Activity);
            Assert.Equal("Workspace appears to be running", WorkspaceStatusLabels.RunningBadgeSummary);
            Assert.False(snapshot.IsStale);
        }
        finally
        {
            WorkspaceStatusService.ResetCacheForTests();
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
                // Best effort.
            }
        }
    }

    [Fact]
    public void FormatBranchContextLabel_MatchesContextMenuStatusRow()
    {
        var status = new WorkspaceGitStatus("main", IsDirty: true, IsDetached: false);
        var label = WorkspaceGitOperations.FormatBranchContextLabel(status, "feature/foo");
        Assert.Equal("Branch: main → feature/foo · dirty", label);
    }

    [Fact]
    public void Capture_EvictsWhenOverCapacity()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "qs-status-cap-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(baseDir);

        try
        {
            WorkspaceStatusService.ResetCacheForTests();
            var probe = LaunchTestServices.CreateHealthyProbe();
            var git = LaunchTestServices.CreateGit(runGit: (_, _) => GitFailure());
            var health = new WorkspaceHealthCheck(probe, git, new TerminalCatalog(new WtProfilesService()), new WtProfilesService());

            var extra = 12;
            var total = WorkspaceStatusService.MaxCacheEntries + extra;
            for (var i = 0; i < total; i++)
            {
                var root = Path.Combine(baseDir, i.ToString("D3", System.Globalization.CultureInfo.InvariantCulture));
                Directory.CreateDirectory(root);
                _ = WorkspaceStatusService.CaptureForList(CreateMinimalShortcut(root),
                    TerminalHostIds.WindowsConsoleHost,
                    "cmd",
                    health,
                    git,
                    new NullWorktreeBranchTargetStore());
            }

            Assert.True(
                WorkspaceStatusService.CacheCountForTests <= WorkspaceStatusService.MaxCacheEntries,
                $"Expected cache count <= {WorkspaceStatusService.MaxCacheEntries}, was {WorkspaceStatusService.CacheCountForTests}.");
        }
        finally
        {
            WorkspaceStatusService.ResetCacheForTests();
            try
            {
                Directory.Delete(baseDir, recursive: true);
            }
            catch
            {
                // Best effort.
            }
        }
    }

    [Fact]
    public void Capture_PrunesStaleEntriesOnInsert()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "qs-status-stale-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(baseDir);
        var t0 = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        try
        {
            WorkspaceStatusService.ResetCacheForTests();
            var probe = LaunchTestServices.CreateHealthyProbe();
            var git = LaunchTestServices.CreateGit(runGit: (_, _) => GitFailure());
            var health = new WorkspaceHealthCheck(probe, git, new TerminalCatalog(new WtProfilesService()), new WtProfilesService());
            WorkspaceStatusService.UtcNowOverride = () => t0;

            for (var i = 0; i < 5; i++)
            {
                var root = Path.Combine(baseDir, "old-" + i);
                Directory.CreateDirectory(root);
                _ = WorkspaceStatusService.CaptureForList(CreateMinimalShortcut(root),
                    TerminalHostIds.WindowsConsoleHost,
                    "cmd",
                    health,
                    git,
                    new NullWorktreeBranchTargetStore());
            }

            Assert.Equal(5, WorkspaceStatusService.CacheCountForTests);

            WorkspaceStatusService.UtcNowOverride = () => t0.AddSeconds(15);
            var freshRoot = Path.Combine(baseDir, "fresh");
            Directory.CreateDirectory(freshRoot);
            _ = WorkspaceStatusService.CaptureForList(CreateMinimalShortcut(freshRoot),
                TerminalHostIds.WindowsConsoleHost,
                "cmd",
                health,
                git,
                new NullWorktreeBranchTargetStore());

            Assert.Equal(1, WorkspaceStatusService.CacheCountForTests);
            Assert.True(
                WorkspaceStatusService.TryGetCached(
                    CreateMinimalShortcut(freshRoot),
                    TerminalHostIds.WindowsConsoleHost,
                    "cmd",
                    health,
                    git,
                    out _));
        }
        finally
        {
            WorkspaceStatusService.ResetCacheForTests();
            try
            {
                Directory.Delete(baseDir, recursive: true);
            }
            catch
            {
                // Best effort.
            }
        }
    }

    [Fact]
    public void TryGetCached_DropsExpiredEntry()
    {
        var root = Path.Combine(Path.GetTempPath(), "qs-status-exp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var t0 = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        try
        {
            WorkspaceStatusService.ResetCacheForTests();
            var probe = LaunchTestServices.CreateHealthyProbe();
            var git = LaunchTestServices.CreateGit(runGit: (_, _) => GitFailure());
            var health = new WorkspaceHealthCheck(probe, git, new TerminalCatalog(new WtProfilesService()), new WtProfilesService());
            WorkspaceStatusService.UtcNowOverride = () => t0;

            var shortcut = CreateMinimalShortcut(root);
            _ = WorkspaceStatusService.CaptureForList(shortcut,
                TerminalHostIds.WindowsConsoleHost,
                "cmd",
                health,
                git,
                new NullWorktreeBranchTargetStore());
            Assert.Equal(1, WorkspaceStatusService.CacheCountForTests);

            WorkspaceStatusService.UtcNowOverride = () => t0.AddSeconds(11);
            Assert.False(
                WorkspaceStatusService.TryGetCached(
                    shortcut,
                    TerminalHostIds.WindowsConsoleHost,
                    "cmd",
                    health,
                    git,
                    out _));
            Assert.Equal(0, WorkspaceStatusService.CacheCountForTests);
        }
        finally
        {
            WorkspaceStatusService.ResetCacheForTests();
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
                // Best effort.
            }
        }
    }

    private static TerminalShortcut CreateMinimalShortcut(string directory) =>
        new()
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = Path.GetFileName(directory),
            Directory = directory,
            Launches =
            [
                new WorkspaceEntry
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Label = "Main",
                    Terminal = "cmd",
                    Command = "echo",
                    IsEnabled = true,
                    Order = 0,
                },
            ],
        };

    private static GitCommandResult GitSuccess(string output) =>
        new(0, output, string.Empty, TimedOut: false);

    private static GitCommandResult GitFailure() =>
        new(1, string.Empty, "failed", TimedOut: false);
}
