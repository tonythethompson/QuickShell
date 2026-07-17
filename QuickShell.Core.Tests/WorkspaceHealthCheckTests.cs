using QuickShell;
using QuickShell.Models;
using QuickShell.Services;

namespace QuickShell.Core.Tests;

[Collection(TerminalLauncherOverrideIsolation.Name)]
public sealed class WorkspaceHealthCheckTests : IDisposable
{
    private readonly string _root;

    public WorkspaceHealthCheckTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quickshell-health-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        WorkspaceHealthCheck.ExecutableExistsOverride = executable =>
            !executable.Equals("missing-tool", StringComparison.OrdinalIgnoreCase);
        WorkspaceHealthCheck.PortInUseOverride = _ => false;
        WorkspaceHealthCheck.ProcessNamesOverride = () => [];
        WorkspaceHealthCheck.WslDistroNamesOverride = () => ["Ubuntu"];
        WorkspaceHealthCheck.GitStatusOverride = _ => null;
        WorkspaceHealthCheck.GitCommandOverride = null;
    }

    [Fact]
    public void Check_MissingFolderIsBlocking()
    {
        var shortcut = BuildShortcut(@"C:\missing-quickshell-health-test");

        var health = WorkspaceHealthCheck.Check(
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

        var health = WorkspaceHealthCheck.Check(
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

        var health = WorkspaceHealthCheck.Check(
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

        var health = WorkspaceHealthCheck.Check(
            shortcut,
            TerminalHostIds.WindowsConsoleHost,
            "cmd");

        Assert.True(health.HasBlockingErrors);
        Assert.Contains(health.Findings, finding => finding.Kind == WorkspaceHealthFindingKind.MissingWslDistro);
    }

    [Fact]
    public void Check_FirstSameAsPreviousLaunch_ValidatesConfiguredDefaultTarget()
    {
        WorkspaceHealthCheck.ExecutableExistsOverride = executable =>
            !executable.Equals("cmd.exe", StringComparison.OrdinalIgnoreCase);
        var shortcut = BuildShortcut(_root);
        shortcut.Launches[0].Terminal = TerminalCatalog.SameAsPreviousLaunchTargetId;

        var health = WorkspaceHealthCheck.Check(
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

        var health = WorkspaceHealthCheck.Check(
            shortcut,
            TerminalHostIds.WindowsConsoleHost,
            "cmd");

        Assert.Contains(health.Findings, finding => finding.Kind == WorkspaceHealthFindingKind.MissingWslDistro);
        Assert.Equal(TerminalCatalog.SameAsPreviousLaunchTargetId, shortcut.Launches[1].Terminal);
    }

    [Fact]
    public void Check_SameAsPreviousIgnoresDisabledPriorLaunches()
    {
        WorkspaceHealthCheck.ExecutableExistsOverride = executable =>
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

        var health = WorkspaceHealthCheck.Check(
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
        WorkspaceHealthCheck.PortInUseOverride = port => port == 5173;
        var shortcut = BuildShortcut(_root);
        shortcut.DevServerUrl = "http://localhost:5173";
        shortcut.OpenDevServerOnLaunch = true;

        var health = WorkspaceHealthCheck.Check(
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
        WorkspaceHealthCheck.PortInUseOverride = port => port == 5173;
        var shortcut = BuildShortcut(_root);
        shortcut.DevServerUrl = "http://localhost:5173";
        shortcut.OpenDevServerOnLaunch = false;

        var health = WorkspaceHealthCheck.Check(
            shortcut,
            TerminalHostIds.WindowsConsoleHost,
            "cmd");

        Assert.DoesNotContain(health.Findings, finding => finding.Kind == WorkspaceHealthFindingKind.PortInUse);
        Assert.False(health.HasRunningSignal);
    }

    [Fact]
    public void Check_ExistingProcessCreatesRunningWarning()
    {
        WorkspaceHealthCheck.ProcessNamesOverride = () => ["node"];
        var shortcut = BuildShortcut(_root, $"node \"{Path.Combine(_root, "server.js")}\"");

        var health = WorkspaceHealthCheck.Check(
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
        WorkspaceHealthCheck.GitStatusOverride = _ => new WorkspaceGitStatus("main", IsDirty: true, IsDetached: false);
        var shortcut = BuildShortcut(_root);

        var health = WorkspaceHealthCheck.Check(
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
        WorkspaceHealthCheck.GitStatusOverride = null;
        WorkspaceHealthCheck.GitCommandOverride = (_, arguments) => arguments switch
        {
            "rev-parse --is-inside-work-tree" => "true",
            "rev-parse --abbrev-ref HEAD" => "feature/worktree",
            "status --porcelain" => " M app.cs",
            _ => null,
        };
        var shortcut = BuildShortcut(_root);

        var health = WorkspaceHealthCheck.Check(
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
        var started = false;
        TerminalLauncher.StartProcessOverride = _ =>
        {
            started = true;
            return true;
        };

        var result = ShortcutLaunchExecutor.Launch(
            shortcut,
            TerminalHostIds.WindowsConsoleHost,
            "cmd");

        Assert.False(result.Dismiss);
        Assert.False(started);
        Assert.Contains("missing-tool", result.StayOpenMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Launch_AllowsPortWarningAndReportsItAfterLaunch()
    {
        WorkspaceHealthCheck.PortInUseOverride = port => port == 5173;
        var shortcut = BuildShortcut(_root);
        shortcut.DevServerUrl = "http://localhost:5173";
        shortcut.OpenDevServerOnLaunch = true;
        TerminalLauncher.StartProcessOverride = _ => true;

        var result = ShortcutLaunchExecutor.Launch(
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
        WorkspaceHealthCheck.PortInUseOverride = port => port == 5173;
        var shortcut = BuildShortcut(_root, "npm run dev");
        shortcut.DevServerUrl = "http://localhost:5173";
        shortcut.OpenDevServerOnLaunch = true;
        var subtitle = ShortcutHealth.BuildListSubtitle(shortcut);
        WorkspaceStatusService.CaptureForList(shortcut, TerminalHostIds.WindowsConsoleHost, "cmd");

        var tags = ShortcutDisplayTags.BuildTags(
            shortcut,
            TerminalHostIds.WindowsConsoleHost,
            "cmd");

        Assert.NotNull(tags);
        var warningTag = Assert.Single(tags, tag => tag.ToolTip == "Workspace health warning");
        Assert.True(warningTag.Foreground.HasValue);
        Assert.Contains(tags, tag => tag.ToolTip == "Workspace appears to be running");
        Assert.Equal(subtitle, ShortcutHealth.BuildListSubtitle(shortcut));
    }

    [Fact]
    public void DisplayTags_NeverExceedsTwoEvenWithAdminFavoriteWarningAndRunning()
    {
        WorkspaceHealthCheck.PortInUseOverride = port => port == 5173;
        var shortcut = BuildShortcut(_root, "npm run dev");
        shortcut.DevServerUrl = "http://localhost:5173";
        shortcut.OpenDevServerOnLaunch = true;
        shortcut.RunAsAdmin = true;
        shortcut.IsPinned = true;
        WorkspaceStatusService.CaptureForList(shortcut, TerminalHostIds.WindowsConsoleHost, "cmd");

        var tags = ShortcutDisplayTags.BuildTags(
            shortcut,
            TerminalHostIds.WindowsConsoleHost,
            "cmd");

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

    public void Dispose()
    {
        TerminalLauncher.StartProcessOverride = null;
        WorkspaceHealthCheck.ExecutableExistsOverride = null;
        WorkspaceHealthCheck.PortInUseOverride = null;
        WorkspaceHealthCheck.ProcessNamesOverride = null;
        WorkspaceHealthCheck.WslDistroNamesOverride = null;
        WorkspaceHealthCheck.GitStatusOverride = null;
        WorkspaceHealthCheck.GitCommandOverride = null;
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
