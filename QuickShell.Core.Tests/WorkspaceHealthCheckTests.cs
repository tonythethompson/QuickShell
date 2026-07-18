using QuickShell;
using QuickShell.Models;
using QuickShell.Services;

namespace QuickShell.Core.Tests;

[Collection(TerminalLauncherOverrideIsolation.Name)]
public sealed class WorkspaceHealthCheckTests : IDisposable
{
    private readonly string _root;
    private readonly FakeWorkspaceEnvironmentProbe _probe = FakeWorkspaceEnvironmentProbe.Healthy();
    private WorkspaceGitOperations _git = LaunchTestServices.CreateGit(getStatus: _ => null);

    public WorkspaceHealthCheckTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quickshell-health-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _probe.ExecutableExistsHandler = executable =>
            !executable.Equals("missing-tool", StringComparison.OrdinalIgnoreCase);
    }

    private WorkspaceHealthCheck CreateHealth() => new(_probe, _git);

    [Fact]
    public void Check_MissingFolderIsBlocking()
    {
        var shortcut = BuildShortcut(@"C:\missing-quickshell-health-test");

        var health = CreateHealth().Check(
            shortcut,
            TerminalHostIds.WindowsConsoleHost,
            "cmd");

        Assert.True(health.HasBlockingErrors);
        Assert.Contains(health.Findings, finding => finding.Kind == WorkspaceHealthFindingKind.MissingFolder);
        Assert.Contains("folder not found", WorkspaceHealthCheck.FormatBlockingSummary(health), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Check_NoEnabledLaunchesIsBlocking()
    {
        var shortcut = BuildShortcut(_root);
        shortcut.Launches[0].IsEnabled = false;

        var health = CreateHealth().Check(
            shortcut,
            TerminalHostIds.WindowsConsoleHost,
            "cmd");

        Assert.True(health.HasBlockingErrors);
        Assert.Contains("no enabled launch", WorkspaceHealthCheck.FormatBlockingSummary(health), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Check_MissingCommandExecutableIsBlocking()
    {
        var shortcut = BuildShortcut(_root, "missing-tool run");

        var health = CreateHealth().Check(
            shortcut,
            TerminalHostIds.WindowsConsoleHost,
            "cmd");

        Assert.True(health.HasBlockingErrors);
        Assert.Contains(health.Findings, finding => finding.Kind == WorkspaceHealthFindingKind.MissingExecutable);
        Assert.Contains("missing-tool", WorkspaceHealthCheck.FormatBlockingSummary(health), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Check_MissingWslDistroIsBlocking()
    {
        var shortcut = BuildShortcut(_root);
        shortcut.Launches[0].Terminal = "wsl";
        shortcut.Launches[0].WtProfile = "Debian";

        var health = CreateHealth().Check(
            shortcut,
            TerminalHostIds.WindowsConsoleHost,
            "cmd");

        Assert.True(health.HasBlockingErrors);
        Assert.Contains(health.Findings, finding => finding.Kind == WorkspaceHealthFindingKind.MissingWslDistro);
    }

    [Fact]
    public void Check_FirstSameAsPreviousLaunch_ValidatesConfiguredDefaultTarget()
    {
        _probe.ExecutableExistsHandler = executable =>
            !executable.Equals("cmd.exe", StringComparison.OrdinalIgnoreCase);
        var shortcut = BuildShortcut(_root);
        shortcut.Launches[0].Terminal = TerminalCatalog.SameAsPreviousLaunchTargetId;

        var health = CreateHealth().Check(
            shortcut,
            TerminalHostIds.WindowsConsoleHost,
            "cmd");

        Assert.Contains(health.Findings, finding =>
            finding.Kind == WorkspaceHealthFindingKind.MissingTerminal
            && finding.Title.Contains("Command Prompt", StringComparison.Ordinal));
        Assert.Equal(TerminalCatalog.SameAsPreviousLaunchTargetId, shortcut.Launches[0].Terminal);
    }

    [Fact]
    public void Check_ChainedSameAsPreviousLaunch_ValidatesInheritedWslDistro()
    {
        var shortcut = BuildShortcut(_root);
        shortcut.Launches =
        [
            new WorkspaceEntry
            {
                Id = Guid.NewGuid().ToString("N"),
                Label = "First",
                Terminal = "wsl",
                WtProfile = "Debian",
                IsEnabled = true,
                Order = 0,
            },
            new WorkspaceEntry
            {
                Id = Guid.NewGuid().ToString("N"),
                Label = "Inherited",
                Terminal = TerminalCatalog.SameAsPreviousLaunchTargetId,
                IsEnabled = true,
                Order = 1,
            },
        ];

        var health = CreateHealth().Check(
            shortcut,
            TerminalHostIds.WindowsConsoleHost,
            "cmd");

        Assert.Contains(health.Findings, finding => finding.Kind == WorkspaceHealthFindingKind.MissingWslDistro);
        Assert.Equal(TerminalCatalog.SameAsPreviousLaunchTargetId, shortcut.Launches[1].Terminal);
    }

    [Fact]
    public void Check_SameAsPreviousIgnoresDisabledPriorLaunches()
    {
        _probe.ExecutableExistsHandler = executable =>
            !executable.Equals("cmd.exe", StringComparison.OrdinalIgnoreCase);
        var shortcut = BuildShortcut(_root);
        shortcut.Launches =
        [
            new WorkspaceEntry
            {
                Id = Guid.NewGuid().ToString("N"),
                Label = "Disabled",
                Terminal = "wsl",
                WtProfile = "Ubuntu",
                IsEnabled = false,
                Order = 0,
            },
            new WorkspaceEntry
            {
                Id = Guid.NewGuid().ToString("N"),
                Label = "Inherited default",
                Terminal = TerminalCatalog.SameAsPreviousLaunchTargetId,
                IsEnabled = true,
                Order = 1,
            },
        ];

        var health = CreateHealth().Check(
            shortcut,
            TerminalHostIds.WindowsConsoleHost,
            "cmd");

        Assert.Contains(health.Findings, finding =>
            finding.Kind == WorkspaceHealthFindingKind.MissingTerminal
            && finding.Title.Contains("Command Prompt", StringComparison.Ordinal));
        Assert.DoesNotContain(health.Findings, finding => finding.Kind == WorkspaceHealthFindingKind.MissingWslDistro);
    }

    [Fact]
    public void Check_PortInUseCreatesRunningWarning()
    {
        _probe.PortInUseHandler = port => port == 5173;
        var shortcut = BuildShortcut(_root);
        shortcut.DevServerUrl = "http://localhost:5173";
        shortcut.OpenDevServerOnLaunch = true;

        var health = CreateHealth().Check(
            shortcut,
            TerminalHostIds.WindowsConsoleHost,
            "cmd");

        Assert.False(health.HasBlockingErrors);
        Assert.True(health.HasRunningSignal);
        Assert.Contains(health.Findings, finding => finding.Kind == WorkspaceHealthFindingKind.PortInUse);
    }

    [Fact]
    public void Check_PortInUseWithoutOpenDevServer_DoesNotCreateRunningWarning()
    {
        _probe.PortInUseHandler = port => port == 5173;
        var shortcut = BuildShortcut(_root);
        shortcut.DevServerUrl = "http://localhost:5173";
        shortcut.OpenDevServerOnLaunch = false;

        var health = CreateHealth().Check(
            shortcut,
            TerminalHostIds.WindowsConsoleHost,
            "cmd");

        Assert.DoesNotContain(health.Findings, finding => finding.Kind == WorkspaceHealthFindingKind.PortInUse);
        Assert.False(health.HasRunningSignal);
    }

    [Fact]
    public void Check_ExistingProcessCreatesRunningWarning()
    {
        _probe.ProcessNamesHandler = () => ["node"];
        var shortcut = BuildShortcut(_root, $"node \"{Path.Combine(_root, "server.js")}\"");

        var health = CreateHealth().Check(
            shortcut,
            TerminalHostIds.WindowsConsoleHost,
            "cmd");

        Assert.False(health.HasBlockingErrors);
        Assert.True(health.HasRunningSignal);
        Assert.Contains(health.Findings, finding => finding.Kind == WorkspaceHealthFindingKind.ExistingProcess);
    }

    [Fact]
    public void Check_GitStatusIsInformational()
    {
        _git = LaunchTestServices.CreateGit(
            getStatus: _ => new WorkspaceGitStatus("main", IsDirty: true, IsDetached: false));
        var shortcut = BuildShortcut(_root);

        var health = CreateHealth().Check(
            shortcut,
            TerminalHostIds.WindowsConsoleHost,
            "cmd");

        Assert.False(health.HasBlockingErrors);
        var gitFinding = Assert.Single(
            health.Findings,
            finding => finding.Kind == WorkspaceHealthFindingKind.GitState);
        Assert.Equal(WorkspaceHealthSeverity.Info, gitFinding.Severity);
        Assert.Contains("main", gitFinding.Title, StringComparison.Ordinal);
    }

    [Fact]
    public void Check_GitStatusSupportsWorktreesWithoutGitDirectory()
    {
        _git = LaunchTestServices.CreateGit(
            runGit: (_, arguments) => arguments switch
            {
                ["status", "--porcelain=v2", "--branch"] => GitSuccess(
                    $"# branch.oid 0000000000000000000000000000000000000000{Environment.NewLine}# branch.head feature/worktree{Environment.NewLine}1 M. N... 100644 100644 100644 e69de29 e69de29 app.cs"),
                _ => GitCommandResult.Failed,
            });
        var shortcut = BuildShortcut(_root);

        var health = CreateHealth().Check(
            shortcut,
            TerminalHostIds.WindowsConsoleHost,
            "cmd");

        Assert.False(Directory.Exists(Path.Combine(_root, ".git")));
        var gitFinding = Assert.Single(
            health.Findings,
            finding => finding.Kind == WorkspaceHealthFindingKind.GitState);
        Assert.Contains("feature/worktree", gitFinding.Title, StringComparison.Ordinal);
        Assert.Contains("uncommitted changes", gitFinding.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Launch_BlocksBeforeStartingProcessWhenExecutableMissing()
    {
        var shortcut = BuildShortcut(_root, "missing-tool run");
        var bundle = LaunchTestServices.CreateBundle(probe: _probe, git: _git);

        var result = bundle.Executor.Launch(
            shortcut,
            TerminalHostIds.WindowsConsoleHost,
            "cmd");

        Assert.False(result.Dismiss);
        Assert.Empty(bundle.ProcessStarter.Started);
        Assert.Contains("missing-tool", result.StayOpenMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Launch_AllowsPortWarningAndReportsItAfterLaunch()
    {
        _probe.PortInUseHandler = port => port == 5173;
        var shortcut = BuildShortcut(_root);
        shortcut.DevServerUrl = "http://localhost:5173";
        shortcut.OpenDevServerOnLaunch = true;
        var bundle = LaunchTestServices.CreateBundle(probe: _probe, git: _git);

        var result = bundle.Executor.Launch(
            shortcut,
            TerminalHostIds.WindowsConsoleHost,
            "cmd");

        Assert.False(result.Dismiss);
        Assert.True(result.MarkUsed);
        Assert.Contains("Port 5173", result.StayOpenMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void DisplayTags_AddsRunningAndWarningWithoutSubtitleChanges()
    {
        _probe.PortInUseHandler = port => port == 5173;
        var shortcut = BuildShortcut(_root, "npm run dev");
        shortcut.DevServerUrl = "http://localhost:5173";
        shortcut.OpenDevServerOnLaunch = true;
        var subtitle = ShortcutHealth.BuildListSubtitle(shortcut);
        var health = CreateHealth();
        WorkspaceStatusService.CaptureForList(
            shortcut,
            TerminalHostIds.WindowsConsoleHost,
            "cmd",
            health,
            _git);

        var tags = ShortcutDisplayTags.BuildTags(
            shortcut,
            TerminalHostIds.WindowsConsoleHost,
            "cmd",
            health,
            _git);

        Assert.NotNull(tags);
        var warningTag = Assert.Single(tags, tag => tag.ToolTip == "Workspace health warning");
        Assert.True(warningTag.Foreground.HasValue);
        Assert.Contains(tags, tag => tag.ToolTip == "Workspace appears to be running");
        Assert.Equal(subtitle, ShortcutHealth.BuildListSubtitle(shortcut));
    }

    [Fact]
    public void DisplayTags_NeverExceedsTwoEvenWithAdminFavoriteWarningAndRunning()
    {
        _probe.PortInUseHandler = port => port == 5173;
        var shortcut = BuildShortcut(_root, "npm run dev");
        shortcut.DevServerUrl = "http://localhost:5173";
        shortcut.OpenDevServerOnLaunch = true;
        shortcut.RunAsAdmin = true;
        shortcut.IsPinned = true;
        var health = CreateHealth();
        WorkspaceStatusService.CaptureForList(
            shortcut,
            TerminalHostIds.WindowsConsoleHost,
            "cmd",
            health,
            _git);

        var tags = ShortcutDisplayTags.BuildTags(
            shortcut,
            TerminalHostIds.WindowsConsoleHost,
            "cmd",
            health,
            _git);

        Assert.NotNull(tags);
        Assert.True(tags.Length <= 2, $"Expected at most 2 tags, found {tags.Length}.");
        Assert.DoesNotContain(tags, tag => tag.ToolTip == "Favorite");
        Assert.DoesNotContain(tags, tag => tag.ToolTip == "Always run as administrator");
    }

    private static TerminalShortcut BuildShortcut(string directory, string? command = null) =>
        new()
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Sample",
            Directory = directory,
            Launches =
            [
                new WorkspaceEntry
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Label = "Main",
                    Terminal = "cmd",
                    Command = command,
                    IsEnabled = true,
                    Order = 0,
                },
            ],
        };

    private static GitCommandResult GitSuccess(string output) =>
        new(0, output, string.Empty, TimedOut: false);

    public void Dispose()
    {
        WorkspaceStatusService.ResetCacheForTests();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
        }
    }
}
