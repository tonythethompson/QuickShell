using QuickShell;
using QuickShell.Models;
using QuickShell.Services;

namespace QuickShell.Core.Tests;

[Collection(TerminalLauncherOverrideCollection.Name)]
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
    public void Check_PortInUseCreatesRunningWarning()
    {
        WorkspaceHealthCheck.PortInUseOverride = port => port == 5173;
        var shortcut = BuildShortcut(_root);
        shortcut.DevServerUrl = "http://localhost:5173";

        var health = WorkspaceHealthCheck.Check(
            shortcut,
            TerminalHostIds.WindowsConsoleHost,
            "cmd");

        Assert.False(health.HasBlockingErrors);
        Assert.True(health.HasRunningSignal);
        Assert.Contains(health.Findings, finding => finding.Kind == WorkspaceHealthFindingKind.PortInUse);
    }

    [Fact]
    public void Check_ExistingProcessCreatesRunningWarning()
    {
        WorkspaceHealthCheck.ProcessNamesOverride = () => ["node"];
        var shortcut = BuildShortcut(_root, "node server.js");

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
        WorkspaceHealthCheck.GitStatusOverride = _ => new WorkspaceGitStatus("main", IsDirty: true);
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
        var shortcut = BuildShortcut(_root, "missing-tool run");
        shortcut.DevServerUrl = "http://localhost:5173";
        var subtitle = ShortcutHealth.BuildListSubtitle(shortcut);

        var tags = ShortcutDisplayTags.BuildTags(
            shortcut,
            TerminalHostIds.WindowsConsoleHost,
            "cmd");

        Assert.NotNull(tags);
        Assert.Contains(tags, tag => tag.ToolTip == "Workspace health warning");
        Assert.Contains(tags, tag => tag.ToolTip == "Workspace appears to be running");
        Assert.Equal(subtitle, ShortcutHealth.BuildListSubtitle(shortcut));
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
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
        }
    }
}
