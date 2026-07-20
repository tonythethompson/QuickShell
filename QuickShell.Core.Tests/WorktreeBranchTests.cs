using QuickShell.Abstractions;
using QuickShell.Models;
using QuickShell.Services;
using System.Text.Json;
using QuickShell;

namespace QuickShell.Core.Tests;

public sealed class WorktreeBranchTests : IDisposable
{
    private readonly string _root;
    private readonly Dictionary<string, string> _branchTargets = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, GitRepoState> _repos = new(StringComparer.OrdinalIgnoreCase);
    private readonly WorkspaceGitOperations _git;
    private FakeWorktreeBranchTargetStore _targetStore = null!;
    private WorkspaceGitLaunchGate _gate = null!;
    private int _switchCalls;

    public WorktreeBranchTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "qs-worktree-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        _git = new WorkspaceGitOperations(RunGit, getStatus: null);
        LaunchExecutorTestEnvironment.Apply();
        ResetSeams();
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

    private LaunchTestBundle CreateLaunchBundle() =>
        LaunchTestServices.CreateBundle(git: _git, targetStore: _targetStore);

    [Fact]
    public void ResolveWorktreeKey_RootAndNestedDirectory_ShareSameKey()
    {
        var repoRoot = Path.Combine(_root, "repo");
        var nested = Path.Combine(repoRoot, "src");
        Directory.CreateDirectory(nested);
        ConfigureRepo(repoRoot, currentBranch: "main", topLevel: repoRoot);

        Assert.True(_git.TryResolveWorktreeKey(repoRoot, out var rootKey));
        Assert.True(_git.TryResolveWorktreeKey(nested, out var nestedKey));
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

        Assert.True(_git.TryResolveWorktreeKey(mainWorktree, out var mainKey));
        Assert.True(_git.TryResolveWorktreeKey(featureWorktree, out var featureKey));
        Assert.NotEqual(mainKey, featureKey, StringComparer.OrdinalIgnoreCase);

        _targetStore.SetTarget(mainKey, "main");
        _targetStore.SetTarget(featureKey, "feature/x");

        Assert.Equal("main", _targetStore.GetTarget(mainKey));
        Assert.Equal("feature/x", _targetStore.GetTarget(featureKey));
    }

    [Fact]
    public void WorktreeTargets_PersistAndReloadFromDisk()
    {
        var repoRoot = Path.Join(_root, "repo");
        Directory.CreateDirectory(repoRoot);
        ConfigureRepo(repoRoot, currentBranch: "main", topLevel: repoRoot);
        Assert.True(_git.TryResolveWorktreeKey(repoRoot, out var worktreeKey));

        var appRoot = Path.Join(_root, "appdata");
        Directory.CreateDirectory(Path.Join(appRoot, "QuickShell"));
        var targetsPath = Path.Join(appRoot, "QuickShell", "worktree-branch-targets.json");
        var store = new WorktreeBranchTargetStore(new AppDataPaths(appRoot), new AtomicFileWriter());

        store.SetTarget(worktreeKey, "feature/persisted");
        Assert.True(File.Exists(targetsPath));

        var reloaded = new WorktreeBranchTargetStore(new AppDataPaths(appRoot), new AtomicFileWriter());
        Assert.Equal("feature/persisted", reloaded.GetTarget(worktreeKey));
    }

    [Fact]
    public void WorktreeTargets_LoadsLegacyLowercaseTargetsProperty()
    {
        var repoRoot = Path.Combine(_root, "repo");
        Directory.CreateDirectory(repoRoot);
        ConfigureRepo(repoRoot, currentBranch: "main", topLevel: repoRoot);
        Assert.True(_git.TryResolveWorktreeKey(repoRoot, out var worktreeKey));

        var appRoot = Path.Join(_root, "appdata-legacy");
        Directory.CreateDirectory(Path.Join(appRoot, "QuickShell"));
        var targetsPath = Path.Join(appRoot, "QuickShell", "worktree-branch-targets.json");
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

        var store = new WorktreeBranchTargetStore(new AppDataPaths(appRoot), new AtomicFileWriter());
        Assert.Equal("feature/legacy", store.GetTarget(worktreeKey));
    }

    [Fact]
    public void WorktreeTargets_IgnoresUnreadableTargetFile()
    {
        var repoRoot = Path.Combine(_root, "repo");
        Directory.CreateDirectory(repoRoot);
        ConfigureRepo(repoRoot, currentBranch: "main", topLevel: repoRoot);

        var appRoot = Path.Join(_root, "appdata-bad");
        Directory.CreateDirectory(Path.Join(appRoot, "QuickShell"));
        var targetsPath = Path.Join(appRoot, "QuickShell", "worktree-branch-targets.json");
        File.WriteAllText(targetsPath, "not-json");

        var store = new WorktreeBranchTargetStore(new AppDataPaths(appRoot), new AtomicFileWriter());
        Assert.Null(store.GetTargetForDirectory(repoRoot, _git));
    }

    [Fact]
    public void Launch_DirtyMismatchWithBlockOn_PreventsAllSideEffects()
    {
        var repoRoot = Path.Combine(_root, "repo");
        Directory.CreateDirectory(repoRoot);
        ConfigureRepo(repoRoot, currentBranch: "main", topLevel: repoRoot, isDirty: true);
        SetTarget(repoRoot, "feature/foo");

        var shortcut = BuildLaunchShortcut(repoRoot, includeCompanion: true, includeDevServer: true);
        WorkspaceDevServerActions.TryOpenOverride = _ => true;

        try
        {
            var bundle = CreateLaunchBundle();
            var result = bundle.Executor.Launch(
                shortcut,
                TerminalHostIds.WindowsTerminal,
                TerminalHostIds.DefaultProfile,
                new ShortcutLaunchOptions(BlockDirtyBranchSwitch: true));

            Assert.False(result.Dismiss);
            Assert.Contains("uncommitted changes", result.StayOpenMessage, StringComparison.OrdinalIgnoreCase);
            Assert.False(bundle.Companion.LastLaunchAttempted);
            Assert.False(WorkspaceDevServerActions.LastOpenAttempted);
            Assert.Equal(0, _switchCalls);
            Assert.Empty(bundle.ProcessStarter.Started);
        }
        finally
        {
            WorkspaceDevServerActions.TryOpenOverride = null;
        }
    }

    [Fact]
    public void Launch_StatusTimeout_ReportsTimeoutInsteadOfNonGit()
    {
        var repoRoot = Path.Combine(_root, "repo");
        Directory.CreateDirectory(repoRoot);
        ConfigureRepo(
            repoRoot,
            currentBranch: "main",
            topLevel: repoRoot,
            statusTimesOut: true);
        SetTarget(repoRoot, "feature/foo");

        var result = _gate.EvaluateBeforeLaunch(repoRoot, blockDirtyBranchSwitch: true);

        Assert.False(result.CanProceed);
        Assert.Contains("timed out", result.StayOpenMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("not a git repository", result.StayOpenMessage, StringComparison.OrdinalIgnoreCase);
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
        var bundle = CreateLaunchBundle();

        var result = bundle.Executor.Launch(
            shortcut,
            TerminalHostIds.WindowsTerminal,
            TerminalHostIds.DefaultProfile,
            new ShortcutLaunchOptions(BlockDirtyBranchSwitch: false));

        Assert.True(result.Dismiss);
        Assert.Equal(1, _switchCalls);
        Assert.Equal(1, bundle.GitGate.SwitchAttemptCount);
        Assert.Equal("feature/foo", GetRepo(repoRoot).CurrentBranch);
    }

    [Fact]
    public void Launch_AlreadyAlignedBranch_DoesNotSwitch()
    {
        var repoRoot = Path.Combine(_root, "repo");
        Directory.CreateDirectory(repoRoot);
        ConfigureRepo(repoRoot, currentBranch: "main", topLevel: repoRoot);
        SetTarget(repoRoot, "main");

        var shortcut = BuildLaunchShortcut(repoRoot);
        var bundle = CreateLaunchBundle();

        var result = bundle.Executor.Launch(
            shortcut,
            TerminalHostIds.WindowsTerminal,
            TerminalHostIds.DefaultProfile);

        Assert.True(result.Dismiss);
        Assert.Equal(0, _switchCalls);
    }

    [Fact]
    public void LaunchEntry_AppliesSameGitGate()
    {
        var repoRoot = Path.Combine(_root, "repo");
        Directory.CreateDirectory(repoRoot);
        ConfigureRepo(repoRoot, currentBranch: "main", topLevel: repoRoot, isDirty: true);
        SetTarget(repoRoot, "feature/foo");

        var shortcut = BuildLaunchShortcut(repoRoot);
        var bundle = CreateLaunchBundle();

        var result = bundle.Executor.LaunchEntry(
            shortcut,
            shortcut.Launches[0],
            TerminalHostIds.WindowsTerminal,
            TerminalHostIds.DefaultProfile,
            new ShortcutLaunchOptions(BlockDirtyBranchSwitch: true));

        Assert.False(result.Dismiss);
        Assert.Equal(0, _switchCalls);
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
        var bundle = CreateLaunchBundle();

        var result = bundle.Executor.Launch(
            shortcut,
            TerminalHostIds.WindowsTerminal,
            TerminalHostIds.DefaultProfile,
            new ShortcutLaunchOptions(BlockDirtyBranchSwitch: false));

        Assert.True(result.Dismiss);
        Assert.Equal(1, _switchCalls);
        Assert.Equal("main", GetRepo(repoRoot).CurrentBranch);
    }

    [Fact]
    public void Launch_MissingGitWithTarget_ReturnsStayOpen()
    {
        var path = Path.Combine(_root, "broken");
        Directory.CreateDirectory(path);
        Assert.True(WorkspaceGitOperations.TryNormalizeWorktreeKey(path, out var key));
        _targetStore.SetTarget(key, "main");
        _branchTargets[key] = "main";

        // Override RunGit for this path: resolve worktree key succeeds, status fails.
        var git = new WorkspaceGitOperations(
            (directory, gitArguments) => gitArguments switch
            {
                ["rev-parse", "--is-inside-work-tree"] => Success("true"),
                ["rev-parse", "--show-toplevel"] => Success(path),
                _ => new GitCommandResult(128, string.Empty, "fatal: not a git repository", TimedOut: false),
            },
            getStatus: null);
        var bundle = LaunchTestServices.CreateBundle(git: git, targetStore: _targetStore);

        var shortcut = BuildLaunchShortcut(path);
        var result = bundle.Executor.Launch(
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
        var bundle = CreateLaunchBundle();
        var result = bundle.Executor.Launch(
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
        var bundle = CreateLaunchBundle();
        var result = bundle.Executor.Launch(
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
        Assert.True(_git.TryGetStatus(worktreePath, out var status));
        Assert.Equal("feature/worktree", status.Branch);
        Assert.True(status.IsDirty);
    }

    [Fact]
    public void SelectTargetBranch_DirtyBlocked_RetainsTargetWithoutSwitching()
    {
        var repoRoot = Path.Combine(_root, "repo");
        Directory.CreateDirectory(repoRoot);
        ConfigureRepo(repoRoot, currentBranch: "main", topLevel: repoRoot, isDirty: true);

        var result = _gate.SelectTargetBranch(
            repoRoot,
            "feature/foo",
            blockDirtyBranchSwitch: true);

        Assert.False(result.CanProceed);
        Assert.Contains("Target set to feature/foo", result.StayOpenMessage, StringComparison.Ordinal);
        Assert.Contains("uncommitted changes", result.StayOpenMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("feature/foo", _targetStore.GetTargetForDirectory(repoRoot, _git));
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

        _gate.ClearTargetBranch(repoRoot);

        Assert.Null(_targetStore.GetTargetForDirectory(repoRoot, _git));
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
        _branchTargets.Clear();
        _targetStore = new FakeWorktreeBranchTargetStore();
        _gate = new WorkspaceGitLaunchGate(_git, _targetStore);
        _switchCalls = 0;
        _repos.Clear();
        WorkspaceDevServerActions.TryOpenOverride = null;
    }

    private void ConfigureRepo(
        string path,
        string currentBranch,
        string topLevel,
        bool isDirty = false,
        bool isDetached = false,
        IReadOnlyList<string>? localBranches = null,
        bool failSwitch = false,
        bool statusTimesOut = false)
    {
        _repos[topLevel] = new GitRepoState
        {
            TopLevel = topLevel,
            CurrentBranch = currentBranch,
            IsDirty = isDirty,
            IsDetached = isDetached,
            LocalBranches = localBranches?.ToList() ?? [currentBranch is "HEAD" ? "main" : currentBranch],
            FailSwitch = failSwitch,
            StatusTimesOut = statusTimesOut,
        };
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
            ["status", "--porcelain=v2", "--branch"] when repo.StatusTimesOut =>
                new GitCommandResult(-1, string.Empty, string.Empty, TimedOut: true),
            ["status", "--porcelain=v2", "--branch"] => Success(BuildPorcelainV2(repo)),
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
        Assert.True(_git.TryResolveWorktreeKey(directory, out var key));
        _branchTargets[key] = branch;
        _targetStore.SetTarget(key, branch);
    }

    private static GitCommandResult Success(string output) => new(0, output, string.Empty, TimedOut: false);

    private static string BuildPorcelainV2(GitRepoState repo)
    {
        var head = repo.IsDetached ? "(detached)" : repo.CurrentBranch;
        var dirty = repo.IsDirty ? "1 M. N... 100644 100644 100644 e69de29 e69de29 file.txt" : string.Empty;
        return $"# branch.oid 0000000000000000000000000000000000000000{Environment.NewLine}# branch.head {head}{Environment.NewLine}{dirty}";
    }

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

        public bool StatusTimesOut { get; set; }
    }
}
