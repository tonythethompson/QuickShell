using System.Text;
using QuickShell.Abstractions;
using QuickShell.Services;

namespace QuickShell.Core.Tests;

/// <summary>
/// Per-test factories for launch/health/git services. No process-wide mutable service state.
/// Terminal discovery stubs (WtProfilesService) remain process-scoped and must stay in a
/// non-parallel collection with other launch tests.
/// </summary>
internal static class LaunchTestServices
{
    private static string? _settingsDirectory;

    /// <summary>
    /// Stubs Windows Terminal profile discovery so resolve paths do not depend on the host machine.
    /// </summary>
    public static void ApplyTerminalDiscoveryStubs()
    {
        ResetTerminalDiscoveryStubs();

        _settingsDirectory = Path.Join(
            Path.GetTempPath(),
            "qs-launch-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_settingsDirectory);

        var settingsPath = Path.Join(_settingsDirectory, "settings.json");
        File.WriteAllText(
            settingsPath,
            """
            {
              "profiles": {
                "list": [
                  {
                    "name": "Test",
                    "guid": "{11111111-1111-1111-1111-111111111111}"
                  }
                ]
              }
            }
            """,
            Encoding.UTF8);

        WtProfilesService.InvalidateCache();
        TerminalCatalog.InvalidateCache();
        WtProfilesService.TestLocationsOverride =
        [
            new TerminalSettingsLocation
            {
                SettingsPath = settingsPath,
                Source = TerminalSettingsSource.WindowsTerminal,
                HostExecutable = "wt.exe",
                IdPrefix = "wt",
                DisplayPrefix = "Windows Terminal",
            },
        ];

        // Ignore on-disk branch targets so a dirty worktree + saved target cannot block launches.
        WorktreeBranchTargetStore.GetTargetOverride = _ => null;
    }

    public static void ResetTerminalDiscoveryStubs()
    {
        WtProfilesService.TestLocationsOverride = null;
        WtProfilesService.InvalidateCache();
        TerminalCatalog.InvalidateCache();
        WorktreeBranchTargetStore.GetTargetOverride = null;
        CompanionAppPreference.ReadLastUsedOverride = null;
        CompanionAppPreference.WriteLastUsedOverride = null;

        if (_settingsDirectory is null)
        {
            return;
        }

        try
        {
            if (Directory.Exists(_settingsDirectory))
            {
                Directory.Delete(_settingsDirectory, recursive: true);
            }
        }
        catch
        {
            // Best effort.
        }

        _settingsDirectory = null;
    }

    /// <summary>Backward-compatible alias used by existing test fixtures.</summary>
    public static void Apply() => ApplyTerminalDiscoveryStubs();

    /// <summary>Backward-compatible alias used by existing test fixtures.</summary>
    public static void Reset() => ResetTerminalDiscoveryStubs();

    public static FakeProcessStarter CreateProcessStarter(bool succeed = true) =>
        new() { Succeed = succeed };

    public static FakeWorkspaceEnvironmentProbe CreateHealthyProbe() =>
        FakeWorkspaceEnvironmentProbe.Healthy();

    public static WorkspaceGitOperations CreateGit(
        Func<string, IReadOnlyList<string>, GitCommandResult>? runGit = null,
        Func<string, WorkspaceGitStatus?>? getStatus = null) =>
        new(
            runGit ?? ((_, _) => GitCommandResult.Failed),
            getStatus);

    public static LaunchTestBundle CreateBundle(
        FakeProcessStarter? processStarter = null,
        IWorkspaceEnvironmentProbe? probe = null,
        WorkspaceGitOperations? git = null)
    {
        var starter = processStarter ?? CreateProcessStarter();
        var gitOps = git ?? CreateGit();
        var healthProbe = probe ?? CreateHealthyProbe();
        var health = new WorkspaceHealthCheck(healthProbe, gitOps);
        var terminal = new TerminalLauncher(starter);
        var companion = new CompanionAppLauncher(starter);
        var gate = new WorkspaceGitLaunchGate(gitOps);
        var executor = new ShortcutLaunchExecutor(terminal, health, companion, gate);
        return new LaunchTestBundle(starter, healthProbe, gitOps, health, terminal, companion, gate, executor);
    }
}

internal sealed record LaunchTestBundle(
    FakeProcessStarter ProcessStarter,
    IWorkspaceEnvironmentProbe Probe,
    WorkspaceGitOperations Git,
    WorkspaceHealthCheck Health,
    TerminalLauncher Terminal,
    CompanionAppLauncher Companion,
    WorkspaceGitLaunchGate GitGate,
    ShortcutLaunchExecutor Executor);
