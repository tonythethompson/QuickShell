using System.Text;
using QuickShell.Abstractions;
using QuickShell.Models;
using QuickShell.Services;

namespace QuickShell.Core.Tests;

/// <summary>
/// Per-test factories for launch/health/git services. No process-wide mutable service state.
/// </summary>
internal static class LaunchTestServices
{
    /// <summary>
    /// Creates a WtProfilesService backed by a temp settings.json with a single Test profile.
    /// Each call uses an isolated directory so parallel tests cannot delete each other's stubs.
    /// </summary>
    public static WtProfilesService CreateStubbedProfilesService(Action? onParse = null)
    {
        var settingsDirectory = Path.Join(
            Path.GetTempPath(),
            "qs-launch-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(settingsDirectory);

        var settingsPath = Path.Join(settingsDirectory, "settings.json");
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

        return new WtProfilesService(
            [
                new TerminalSettingsLocation
                {
                    SettingsPath = settingsPath,
                    Source = TerminalSettingsSource.WindowsTerminal,
                    HostExecutable = "wt.exe",
                    IdPrefix = "wt",
                    DisplayPrefix = "Windows Terminal",
                },
            ],
            onParse);
    }

    /// <summary>
    /// Legacy no-op kept for call sites that previously mutated process-wide discovery stubs.
    /// </summary>
    public static void ApplyTerminalDiscoveryStubs()
    {
        // Discovery is now per-instance via CreateStubbedProfilesService / CreateBundle.
    }

    /// <summary>
    /// Legacy no-op: stub settings dirs are per-call and cleaned by the OS temp cleaner.
    /// </summary>
    public static void ResetTerminalDiscoveryStubs()
    {
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

    public static NullWorktreeBranchTargetStore CreateNullTargetStore() => new();

    public static LaunchTestBundle CreateBundle(
        FakeProcessStarter? processStarter = null,
        IWorkspaceEnvironmentProbe? probe = null,
        WorkspaceGitOperations? git = null,
        TerminalShortcut? shortcut = null,
        IShortcutRepository? repository = null,
        IWorktreeBranchTargetStore? targetStore = null)
    {
        var starter = processStarter ?? CreateProcessStarter();
        var gitOps = git ?? CreateGit();
        var healthProbe = probe ?? CreateHealthyProbe();
        var profiles = CreateStubbedProfilesService();
        var catalog = new TerminalCatalog(profiles);
        var health = new WorkspaceHealthCheck(healthProbe, gitOps, catalog, profiles);
        var terminal = new TerminalLauncher(starter, catalog);
        var companion = new CompanionAppLauncher(starter);
        var targets = targetStore ?? CreateNullTargetStore();
        var gate = new WorkspaceGitLaunchGate(gitOps, targets);
        var repo = repository ?? (shortcut is null ? null : new FakeShortcutRepository([shortcut]));
        var executor = new ShortcutLaunchExecutor(terminal, health, companion, gate, repo, catalog);
        return new LaunchTestBundle(
            starter,
            healthProbe,
            gitOps,
            health,
            terminal,
            companion,
            gate,
            executor,
            repo,
            catalog,
            profiles,
            targets);
    }
}

internal sealed class NullWorktreeBranchTargetStore : IWorktreeBranchTargetStore
{
    public string? GetTarget(string worktreeKey) => null;

    public string? GetTargetForDirectory(string directory, IWorkspaceGitOperations git) => null;

    public void SetTarget(string worktreeKey, string? branch)
    {
    }

    public bool TrySetTargetForDirectory(
        string directory,
        string? branch,
        IWorkspaceGitOperations git,
        out string? error)
    {
        error = null;
        return true;
    }

    public void ClearTargetForDirectory(string directory, IWorkspaceGitOperations git)
    {
    }
}

/// <summary>In-memory branch target store for tests that need a configured target.</summary>
internal sealed class FakeWorktreeBranchTargetStore : IWorktreeBranchTargetStore
{
    private readonly Dictionary<string, string> _byKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<string, string?>? _getTarget;

    public FakeWorktreeBranchTargetStore(Func<string, string?>? getTarget = null)
    {
        _getTarget = getTarget;
    }

    public string? GetTarget(string worktreeKey) =>
        _getTarget?.Invoke(worktreeKey)
        ?? (_byKey.TryGetValue(worktreeKey, out var target) ? target : null);

    public string? GetTargetForDirectory(string directory, IWorkspaceGitOperations git)
    {
        if (!git.TryResolveWorktreeKey(directory, out var key))
        {
            return _getTarget?.Invoke(directory);
        }

        return GetTarget(key);
    }

    public void SetTarget(string worktreeKey, string? branch)
    {
        if (string.IsNullOrWhiteSpace(branch))
        {
            _byKey.Remove(worktreeKey);
        }
        else
        {
            _byKey[worktreeKey] = branch.Trim();
        }
    }

    public bool TrySetTargetForDirectory(
        string directory,
        string? branch,
        IWorkspaceGitOperations git,
        out string? error)
    {
        error = null;
        if (!git.TryResolveWorktreeKey(directory, out var key))
        {
            error = "This folder is not a git repository.";
            return false;
        }

        SetTarget(key, branch);
        return true;
    }

    public void ClearTargetForDirectory(string directory, IWorkspaceGitOperations git)
    {
        if (git.TryResolveWorktreeKey(directory, out var key))
        {
            SetTarget(key, null);
        }
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
    ShortcutLaunchExecutor Executor,
    IShortcutRepository? Repository,
    ITerminalCatalog Catalog,
    IWtProfilesService Profiles,
    IWorktreeBranchTargetStore TargetStore);
