using QuickShell.Models;
using QuickShell.Services;

namespace QuickShell.Core.Tests;

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
            WorkspaceHealthCheck.PortInUseOverride = _ => false;
            WorktreeBranchTargetStore.ResetForTests();
            WorktreeBranchTargetStore.GetTargetOverride = _ => "feature/status";
            WorkspaceGitOperations.GitRunOverride = (_, args) => args switch
            {
                ["rev-parse", "--is-inside-work-tree"] => GitSuccess("true"),
                ["rev-parse", "--show-toplevel"] => GitSuccess(root),
                ["rev-parse", "--abbrev-ref", "HEAD"] => GitSuccess("main"),
                ["status", "--porcelain"] => GitSuccess(string.Empty),
                _ => GitFailure(),
            };

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

            var snapshot = WorkspaceStatusService.CaptureForList(
                shortcut,
                TerminalHostIds.WindowsConsoleHost,
                "cmd");

            Assert.Equal(WorkspaceAttentionState.Branch, snapshot.Attention);
            Assert.Equal("Configured branch differs from HEAD", snapshot.AttentionSummary);
            Assert.False(snapshot.IsStale);
        }
        finally
        {
            WorkspaceStatusService.ResetCacheForTests();
            WorkspaceHealthCheck.PortInUseOverride = null;
            WorktreeBranchTargetStore.ResetForTests();
            WorktreeBranchTargetStore.GetTargetOverride = null;
            WorkspaceGitOperations.GitRunOverride = null;
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
            WorkspaceHealthCheck.PortInUseOverride = port => port == 5173;

            var shortcut = new TerminalShortcut
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = "Sample",
                Directory = root,
                DevServerUrl = "http://localhost:5173",
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

            var snapshot = WorkspaceStatusService.CaptureForList(
                shortcut,
                TerminalHostIds.WindowsConsoleHost,
                "cmd");

            Assert.Equal(WorkspaceActivityState.Running, snapshot.Activity);
            Assert.Equal("Workspace appears to be running", WorkspaceStatusLabels.RunningBadgeSummary);
            Assert.False(snapshot.IsStale);
        }
        finally
        {
            WorkspaceStatusService.ResetCacheForTests();
            WorkspaceHealthCheck.PortInUseOverride = null;
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

    private static GitCommandResult GitSuccess(string output) =>
        new(0, output, string.Empty, TimedOut: false);

    private static GitCommandResult GitFailure() =>
        new(1, string.Empty, "failed", TimedOut: false);
}
