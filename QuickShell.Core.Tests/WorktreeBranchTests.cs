using QuickShell.Models;
using QuickShell.Services;
using System.Text.Json;
using QuickShell;

namespace QuickShell.Core.Tests;

[Collection(TerminalLauncherOverrideCollection.Name)]
public sealed class WorktreeBranchTests : IDisposable
{
    private readonly string _root;
    private readonly Dictionary<string, string> _branchTargets = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, GitRepoState> _repos = new(StringComparer.OrdinalIgnoreCase);
    private int _switchCalls;

    public WorktreeBranchTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "qs-worktree-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        ResetSeams();
        LaunchExecutorTestEnvironment.Apply();
    }

    public void Dispose()
    {
        ResetSeams();
        LaunchExecutorTestEnvironment.Reset();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // Best effort.
        }
    }

    [Fact]
    public void ResolveWorktreeKey_RootAndNestedDirectory_ShareSameKey()
    {
        var repoRoot = Path.Combine(_root, "repo");
        var nested = Path.Combine(repoRoot, "src");
        Directory.CreateDirectory(nested);
        ConfigureRepo(repoRoot, currentBranch: "main", topLevel: repoRoot);

        Assert.True(WorkspaceGitOperations.TryResolveWorktreeKey(repoRoot, out var rootKey));
        Assert.True(WorkspaceGitOperations.TryResolveWorktreeKey(nested, out var nestedKey));
        Assert.Equal(rootKey, nestedKey, ignoreCase: true);
    }

    [Fact]
    public void WorktreeTargets_LinkedWorktrees_KeepIndependentTargets()
    {
        var mainWorktree = Path.Combine(_root, "main");
        var featureWorktree = Path.Combine(_root, "feature-wt");
        Directory.CreateDirectory(mainWorktree);
        Directory.CreateDirectory(featureWorktree);
        ConfigureRepo(mainWorktree, currentBranch: "main", topLevel: mainWorktree);
        ConfigureRepo(featureWorktree, currentBranch: "feature/x", topLevel: featureWorktree);

        Assert.True(WorkspaceGitOperations.TryResolveWorktreeKey(mainWorktree, out var mainKey));
        Assert.True(WorkspaceGitOperations.TryResolveWorktreeKey(featureWorktree, out var featureKey));
        Assert.NotEqual(mainKey, featureKey, StringComparer.OrdinalIgnoreCase);

        _branchTargets[mainKey] = "main";
        _branchTargets[featureKey] = "feature/x";

        Assert.Equal("main", WorktreeBranchTargetStore.GetTarget(mainKey));
        Assert.Equal("feature/x", WorktreeBranchTargetStore.GetTarget(featureKey));
    }

    [Fact]
    public void WorktreeTargets_PersistAndReloadFromDisk()
    {
        var repoRoot = Path.Join(_root, "repo");
        Directory.CreateDirectory(repoRoot);
        ConfigureRepo(repoRoot, currentBranch: "main", topLevel: repoRoot);
        Assert.True(WorkspaceGitOperations.TryResolveWorktreeKey(repoRoot, out var worktreeKey));

        var targetsPath = Path.Join(_root, "worktree-branch-targets.json");
        WorktreeBranchTargetStore.GetTargetOverride = null;
        WorktreeBranchTargetStore.SetTargetOverride = null;
        WorktreeBranchTargetStore.FilePathOverride = targetsPath;
        WorktreeBranchTargetStore.ResetForTests();

        WorktreeBranchTargetStore.SetTarget(worktreeKey, "feature/persisted");
        Assert.True(File.Exists(targetsPath));

        WorktreeBranchTargetStore.ResetForTests();
        WorktreeBranchTargetStore.FilePathOverride = targetsPath;

        Assert.Equal("feature/persisted", WorktreeBranchTargetStore.GetTarget(worktreeKey));
    }

    [Fact]
    public void WorktreeTargets_LoadsLegacyLowercaseTargetsProperty()
    {
        var repoRoot = Path.Combine(_root, "repo");
        Directory.CreateDirectory(repoRoot);
        ConfigureRepo(repoRoot, currentBranch: "main", topLevel: repoRoot);
        Assert.True(WorkspaceGitOperations.TryResolveWorktreeKey(repoRoot, out var worktreeKey));

        var targetsPath = Path.Join(_root, "worktree-branch-targets.json");
        var json = JsonSerializer.Serialize(
            new WorktreeBranchTargetsDocument
            {
                Targets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [worktreeKey] = "feature/legacy",
                },
            },
            QuickShellJsonContext.Default.WorktreeBranchTargetsDocument);
        File.WriteAllText(
            targetsPath,
            json.Replace("\"Targets\"", "\"targets\"", StringComparison.Ordinal));

        WorktreeBranchTargetStore.GetTargetOverride = null;
        WorktreeBranchTargetStore.SetTargetOverride = null;
        WorktreeBranchTargetStore.FilePathOverride = targetsPath;
        WorktreeBranchTargetStore.ResetForTests();

        Assert.Equal("feature/legacy", WorktreeBranchTargetStore.GetTarget(worktreeKey));
    }

    [Fact]
    public void WorktreeTargets_IgnoresUnreadableTargetFile()
    {
        var repoRoot = Path.Combine(_root, "repo");
        Directory.CreateDirectory(repoRoot);
        ConfigureRepo(repoRoot, currentBranch: "main", topLevel: repoRoot);

        var targetsPath = Path.Join(_root, "worktree-branch-targets.json");
        File.WriteAllText(targetsPath, "not-json");

        WorktreeBranchTargetStore.GetTargetOverride = null;
        WorktreeBranchTargetStore.SetTargetOverride = null;
        WorktreeBranchTargetStore.FilePathOverride = targetsPath;
        WorktreeBranchTargetStore.ResetForTests();

        Assert.Null(WorktreeBranchTargetStore.GetTargetForDirectory(repoRoot));
    }

    [Fact]
    public void Launch_DirtyMismatchWithBlockOn_PreventsAllSideEffects()
    {
        var repoRoot = Path.Combine(_root, "repo");
        Directory.CreateDirectory(repoRoot);
        ConfigureRepo(repoRoot, currentBranch: "main", topLevel: repoRoot, isDirty: true);
        SetTarget(repoRoot, "feature/foo");

        var shortcut = BuildLaunchShortcut(repoRoot, includeCompanion: true, includeDevServer: true);
        TerminalLauncher.StartProcessOverride = _ => true;
        CompanionAppLauncher.TryLaunchOverride = (_, _) => true;
        WorkspaceDevServerActions.TryOpenOverride = _ => true;

        try
        {
            var result = ShortcutLaunchExecutor.Launch(
                shortcut,
                TerminalHostIds.WindowsTerminal,
                TerminalHostIds.DefaultProfile,
                new ShortcutLaunchOptions(BlockDirtyBranchSwitch: true));

            Assert.False(result.Dismiss);
            Assert.Contains("uncommitted changes", result.StayOpenMessage, StringComparison.OrdinalIgnoreCase);
            Assert.False(CompanionAppLauncher.LastLaunchAttempted);
            Assert.False(WorkspaceDevServerActions.LastOpenAttempted);
            Assert.Equal(0, _switchCalls);
        }
        finally
        {
            TerminalLauncher.StartProcessOverride = null;
            CompanionAppLauncher.TryLaunchOverride = null;
            WorkspaceDevServerActions.TryOpenOverride = null;
        }
    }

    [Fact]
    public void Launch_DirtyMismatchWithBlockOff_RunsSingleSwitchBeforeTerminalBatch()
    {
        var repoRoot = Path.Combine(_root, "repo");
        Directory.CreateDirectory(repoRoot);
        ConfigureRepo(
            repoRoot,
            currentBranch: "main",
            topLevel: repoRoot,
            isDirty: true,
            localBranches: ["main", "feature/foo"]);
        SetTarget(repoRoot, "feature/foo");

        var shortcut = BuildLaunchShortcut(repoRoot, multiTab: true);
        TerminalLauncher.StartProcessOverride = _ => true;

        try
        {
            var result = ShortcutLaunchExecutor.Launch(
                shortcut,
                TerminalHostIds.WindowsTerminal,
                TerminalHostIds.DefaultProfile,
                new ShortcutLaunchOptions(BlockDirtyBranchSwitch: false));

            Assert.True(result.Dismiss);
            Assert.Equal(1, _switchCalls);
            Assert.Equal(1, WorkspaceGitLaunchGate.SwitchAttemptCount);
            Assert.Equal("feature/foo", GetRepo(repoRoot).CurrentBranch);
        }
        finally
        {
            TerminalLauncher.StartProcessOverride = null;
        }
    }

    [Fact]
    public void Launch_AlreadyAlignedBranch_DoesNotSwitch()
    {
        var repoRoot = Path.Combine(_root, "repo");
        Directory.CreateDirectory(repoRoot);
        ConfigureRepo(repoRoot, currentBranch: "main", topLevel: repoRoot);
        SetTarget(repoRoot, "main");

        var shortcut = BuildLaunchShortcut(repoRoot);
        TerminalLauncher.StartProcessOverride = _ => true;

        try
        {
            var result = ShortcutLaunchExecutor.Launch(
                shortcut,
                TerminalHostIds.WindowsTerminal,
                TerminalHostIds.DefaultProfile);

            Assert.True(result.Dismiss);
            Assert.Equal(0, _switchCalls);
        }
        finally
        {
            TerminalLauncher.StartProcessOverride = null;
        }
    }

    [Fact]
    public void LaunchEntry_AppliesSameGitGate()
    {
        var repoRoot = Path.Combine(_root, "repo");
        Directory.CreateDirectory(repoRoot);
        ConfigureRepo(repoRoot, currentBranch: "main", topLevel: repoRoot, isDirty: true);
        SetTarget(repoRoot, "feature/foo");

        var shortcut = BuildLaunchShortcut(repoRoot);
        TerminalLauncher.StartProcessOverride = _ => true;

        try
        {
            var result = ShortcutLaunchExecutor.LaunchEntry(
                shortcut,
                shortcut.Launches[0],
                TerminalHostIds.WindowsTerminal,
                TerminalHostIds.DefaultProfile,
                new ShortcutLaunchOptions(BlockDirtyBranchSwitch: true));

            Assert.False(result.Dismiss);
            Assert.Equal(0, _switchCalls);
        }
        finally
        {
            TerminalLauncher.StartProcessOverride = null;
        }
    }

    [Fact]
    public void Launch_DetachedHeadWithTarget_SwitchesToTarget()
    {
        var repoRoot = Path.Combine(_root, "repo");
        Directory.CreateDirectory(repoRoot);
        ConfigureRepo(
            repoRoot,
            currentBranch: "HEAD",
            topLevel: repoRoot,
            isDetached: true,
            localBranches: ["main", "feature/foo"]);
        SetTarget(repoRoot, "main");

        var shortcut = BuildLaunchShortcut(repoRoot);
        TerminalLauncher.StartProcessOverride = _ => true;

        try
        {
            var result = ShortcutLaunchExecutor.Launch(
                shortcut,
                TerminalHostIds.WindowsTerminal,
                TerminalHostIds.DefaultProfile,
                new ShortcutLaunchOptions(BlockDirtyBranchSwitch: false));

            Assert.True(result.Dismiss);
            Assert.Equal(1, _switchCalls);
            Assert.Equal("main", GetRepo(repoRoot).CurrentBranch);
        }
        finally
        {
            TerminalLauncher.StartProcessOverride = null;
        }
    }

    [Fact]
    public void Launch_MissingGitWithTarget_ReturnsStayOpen()
    {
        var path = Path.Combine(_root, "broken");
        Directory.CreateDirectory(path);
        Assert.True(WorkspaceGitOperations.TryNormalizeWorktreeKey(path, out var key));
        _branchTargets[key] = "main";

        WorkspaceGitOperations.GitRunOverride = (directory, gitArguments) => gitArguments switch
        {
            ["rev-parse", "--is-inside-work-tree"] => Success("true"),
            ["rev-parse", "--show-toplevel"] => Success(path),
            _ => new GitCommandResult(128, string.Empty, "fatal: not a git repository", TimedOut: false),
        };

        var shortcut = BuildLaunchShortcut(path);
        var result = ShortcutLaunchExecutor.Launch(
            shortcut,
            TerminalHostIds.WindowsTerminal,
            TerminalHostIds.DefaultProfile);

        Assert.False(result.Dismiss);
        Assert.Contains("not a git repository", result.StayOpenMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Launch_MissingLocalBranch_ReturnsStayOpen()
    {
        var repoRoot = Path.Combine(_root, "repo");
        Directory.CreateDirectory(repoRoot);
        ConfigureRepo(
            repoRoot,
            currentBranch: "main",
            topLevel: repoRoot,
            localBranches: ["main"]);
        SetTarget(repoRoot, "missing-branch");

        var shortcut = BuildLaunchShortcut(repoRoot);
        var result = ShortcutLaunchExecutor.Launch(
            shortcut,
            TerminalHostIds.WindowsTerminal,
            TerminalHostIds.DefaultProfile,
            new ShortcutLaunchOptions(BlockDirtyBranchSwitch: false));

        Assert.False(result.Dismiss);
        Assert.Equal(1, _switchCalls);
        Assert.Contains("missing-branch", result.StayOpenMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Launch_SwitchFailure_ReturnsStayOpen()
    {
        var repoRoot = Path.Combine(_root, "repo");
        Directory.CreateDirectory(repoRoot);
        ConfigureRepo(repoRoot, currentBranch: "main", topLevel: repoRoot, failSwitch: true);
        SetTarget(repoRoot, "feature/foo");

        var shortcut = BuildLaunchShortcut(repoRoot);
        var result = ShortcutLaunchExecutor.Launch(
            shortcut,
            TerminalHostIds.WindowsTerminal,
            TerminalHostIds.DefaultProfile,
            new ShortcutLaunchOptions(BlockDirtyBranchSwitch: false));

        Assert.False(result.Dismiss);
        Assert.Contains("failed to switch", result.StayOpenMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GitStatus_WorktreeWithoutGitDirectory_IsSupported()
    {
        var worktreePath = Path.Combine(_root, "linked");
        Directory.CreateDirectory(worktreePath);
        ConfigureRepo(worktreePath, currentBranch: "feature/worktree", topLevel: worktreePath, isDirty: true);

        Assert.False(Directory.Exists(Path.Combine(worktreePath, ".git")));
        Assert.True(WorkspaceGitOperations.TryGetStatus(worktreePath, out var status));
        Assert.Equal("feature/worktree", status.Branch);
        Assert.True(status.IsDirty);
    }

    [Fact]
    public void SelectTargetBranch_DirtyBlocked_RetainsTargetWithoutSwitching()
    {
        var repoRoot = Path.Combine(_root, "repo");
        Directory.CreateDirectory(repoRoot);
        ConfigureRepo(repoRoot, currentBranch: "main", topLevel: repoRoot, isDirty: true);

        var result = WorkspaceGitLaunchGate.SelectTargetBranch(
            repoRoot,
            "feature/foo",
            blockDirtyBranchSwitch: true);

        Assert.False(result.CanProceed);
        Assert.Contains("Target set to feature/foo", result.StayOpenMessage, StringComparison.Ordinal);
        Assert.Contains("uncommitted changes", result.StayOpenMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("feature/foo", WorktreeBranchTargetStore.GetTargetForDirectory(repoRoot));
        Assert.Equal("main", GetRepo(repoRoot).CurrentBranch);
        Assert.Equal(0, _switchCalls);
    }

    [Fact]
    public void ClearTargetBranch_DoesNotChangeHead()
    {
        var repoRoot = Path.Combine(_root, "repo");
        Directory.CreateDirectory(repoRoot);
        ConfigureRepo(repoRoot, currentBranch: "main", topLevel: repoRoot);
        SetTarget(repoRoot, "feature/foo");

        WorkspaceGitLaunchGate.ClearTargetBranch(repoRoot);

        Assert.Null(WorktreeBranchTargetStore.GetTargetForDirectory(repoRoot));
        Assert.Equal("main", GetRepo(repoRoot).CurrentBranch);
    }

    [Theory]
    [InlineData("main", "feature/foo", true, "Branch: main → feature/foo · dirty")]
    [InlineData("main", null, true, "Branch: main · dirty")]
    [InlineData("feature/foo", "feature/foo", true, "Branch: feature/foo · dirty")]
    public void FormatBranchContextLabel_ReflectsLiveAndDesiredState(
        string current,
        string? target,
        bool dirty,
        string expected)
    {
        var status = new WorkspaceGitStatus(current, dirty, IsDetached: false);
        var label = WorkspaceGitOperations.FormatBranchContextLabel(status, target);
        Assert.Equal(expected, label);
    }

    private void ResetSeams()
    {
        WorkspaceGitOperations.GitRunOverride = null;
        WorkspaceGitOperations.GitStatusOverride = null;
        WorktreeBranchTargetStore.ResetForTests();
        WorktreeBranchTargetStore.FilePathOverride = null;
        WorktreeBranchTargetStore.GetTargetOverride = key =>
            _branchTargets.TryGetValue(key, out var branch) ? branch : null;
        WorktreeBranchTargetStore.SetTargetOverride = (key, branch) =>
        {
            if (string.IsNullOrWhiteSpace(branch))
            {
                _branchTargets.Remove(key);
            }
            else
            {
                _branchTargets[key] = branch;
            }
        };
        WorkspaceGitLaunchGate.ResetForTests();
        _switchCalls = 0;
        _repos.Clear();
        _branchTargets.Clear();
        CompanionAppLauncher.TryLaunchOverride = null;
        WorkspaceDevServerActions.TryOpenOverride = null;
        WorkspaceHealthCheck.GitStatusOverride = null;
        WorkspaceHealthCheck.GitCommandOverride = null;
    }

    private void ConfigureRepo(
        string path,
        string currentBranch,
        string topLevel,
        bool isDirty = false,
        bool isDetached = false,
        IReadOnlyList<string>? localBranches = null,
        bool failSwitch = false)
    {
        _repos[topLevel] = new GitRepoState
        {
            TopLevel = topLevel,
            CurrentBranch = currentBranch,
            IsDirty = isDirty,
            IsDetached = isDetached,
            LocalBranches = localBranches?.ToList() ?? [currentBranch is "HEAD" ? "main" : currentBranch],
            FailSwitch = failSwitch,
        };

        WorkspaceGitOperations.GitRunOverride = RunGit;
    }

    private GitCommandResult RunGit(string directory, IReadOnlyList<string> gitArguments)
    {
        var repo = FindRepo(directory);
        if (repo is null)
        {
            return new GitCommandResult(128, string.Empty, "not a git repository", TimedOut: false);
        }

        return gitArguments switch
        {
            ["rev-parse", "--is-inside-work-tree"] => Success("true"),
            ["rev-parse", "--show-toplevel"] => Success(repo.TopLevel),
            ["rev-parse", "--abbrev-ref", "HEAD"] => Success(repo.IsDetached ? "HEAD" : repo.CurrentBranch),
            ["status", "--porcelain"] => Success(repo.IsDirty ? " M file.txt" : string.Empty),
            ["for-each-ref", "refs/heads", "--format=%(refname:short)"] => Success(string.Join('\n', repo.LocalBranches)),
            ["switch", var branch] => HandleSwitch(repo, branch),
            _ => new GitCommandResult(1, string.Empty, $"unsupported git args: {string.Join(' ', gitArguments)}", TimedOut: false),
        };
    }

    private GitCommandResult HandleSwitch(GitRepoState repo, string branch)
    {
        _switchCalls++;
        if (repo.FailSwitch)
        {
            return new GitCommandResult(1, string.Empty, "failed to switch", TimedOut: false);
        }

        if (!repo.LocalBranches.Contains(branch, StringComparer.Ordinal))
        {
            return new GitCommandResult(1, string.Empty, $"pathspec '{branch}' did not match any file(s) known to git", TimedOut: false);
        }

        repo.CurrentBranch = branch;
        repo.IsDetached = false;
        return Success(string.Empty);
    }

    private GitRepoState? FindRepo(string directory)
    {
        if (_repos.TryGetValue(directory, out var direct))
        {
            return direct;
        }

        return _repos.Values.FirstOrDefault(candidate =>
            directory.Equals(candidate.TopLevel, StringComparison.OrdinalIgnoreCase)
            || directory.StartsWith(
                candidate.TopLevel + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase));
    }

    private GitRepoState GetRepo(string path) =>
        FindRepo(path) ?? throw new InvalidOperationException($"Repo not found for '{path}'.");

    private void SetTarget(string directory, string branch)
    {
        Assert.True(WorkspaceGitOperations.TryResolveWorktreeKey(directory, out var key));
        _branchTargets[key] = branch;
    }

    private static GitCommandResult Success(string output) => new(0, output, string.Empty, TimedOut: false);

    private static TerminalShortcut BuildLaunchShortcut(
        string directory,
        bool multiTab = false,
        bool includeCompanion = false,
        bool includeDevServer = false)
    {
        var launches = multiTab
            ? new List<WorkspaceEntry>
            {
                new() { Id = Guid.NewGuid().ToString("N"), Label = "One", Terminal = "wt", Command = "cmd", IsEnabled = true, Order = 0 },
                new() { Id = Guid.NewGuid().ToString("N"), Label = "Two", Terminal = "wt", Command = "cmd", IsEnabled = true, Order = 1 },
            }
            : new List<WorkspaceEntry>
            {
                new() { Id = Guid.NewGuid().ToString("N"), Label = "Main", Terminal = "wt", Command = "cmd", IsEnabled = true, Order = 0 },
            };

        return new TerminalShortcut
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Test",
            Directory = directory,
            OpenCompanionAppOnLaunch = includeCompanion,
            CompanionAppPath = includeCompanion ? "code.exe" : null,
            DevServerUrl = includeDevServer ? "http://localhost:5173" : null,
            OpenDevServerOnLaunch = includeDevServer,
            Launches = launches,
        };
    }

    private sealed class GitRepoState
    {
        public required string TopLevel { get; init; }

        public string CurrentBranch { get; set; } = "main";

        public bool IsDirty { get; set; }

        public bool IsDetached { get; set; }

        public List<string> LocalBranches { get; set; } = [];

        public bool FailSwitch { get; set; }
    }
}
