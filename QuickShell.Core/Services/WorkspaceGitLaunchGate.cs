using QuickShell.Abstractions;

namespace QuickShell.Services;

internal sealed record WorkspaceGitLaunchGateResult(bool CanProceed, string? StayOpenMessage)
{
    public static WorkspaceGitLaunchGateResult Proceed() => new(true, null);

    public static WorkspaceGitLaunchGateResult StayOpen(string message) => new(false, message);
}

internal sealed class WorkspaceGitLaunchGate
{
    private const int LaunchGitStatusTimeoutMs = 3000;
    private readonly IWorkspaceGitOperations _git;
    private readonly IWorktreeBranchTargetStore _targetStore;

    public WorkspaceGitLaunchGate(IWorkspaceGitOperations git, IWorktreeBranchTargetStore targetStore)
    {
        _git = git ?? throw new ArgumentNullException(nameof(git));
        _targetStore = targetStore ?? throw new ArgumentNullException(nameof(targetStore));
    }

    internal int SwitchAttemptCount { get; private set; }

    public WorkspaceGitLaunchGateResult EvaluateBeforeLaunch(
        string directory,
        bool blockDirtyBranchSwitch)
    {
        var target = _targetStore.GetTargetForDirectory(directory, _git);
        if (string.IsNullOrWhiteSpace(target))
        {
            return WorkspaceGitLaunchGateResult.Proceed();
        }

        return TryEnsureTargetBranch(directory, target, blockDirtyBranchSwitch, persistTargetOnFailure: false);
    }

    public WorkspaceGitLaunchGateResult SelectTargetBranch(
        string directory,
        string branch,
        bool blockDirtyBranchSwitch)
    {
        if (!_targetStore.TrySetTargetForDirectory(directory, branch, _git, out var storeError))
        {
            return WorkspaceGitLaunchGateResult.StayOpen(storeError ?? "Could not save branch target.");
        }

        return TryEnsureTargetBranch(directory, branch, blockDirtyBranchSwitch, persistTargetOnFailure: true);
    }

    public WorkspaceGitLaunchGateResult ClearTargetBranch(string directory)
    {
        _targetStore.ClearTargetForDirectory(directory, _git);
        return WorkspaceGitLaunchGateResult.Proceed();
    }

    private WorkspaceGitLaunchGateResult TryEnsureTargetBranch(
        string directory,
        string target,
        bool blockDirtyBranchSwitch,
        bool persistTargetOnFailure)
    {
        if (!_git.TryGetStatusForLaunch(directory, out var status, out var timedOut))
        {
            if (timedOut)
            {
                return WorkspaceGitLaunchGateResult.StayOpen(
                    persistTargetOnFailure
                        ? $"Target set to {target}, but Git status timed out before the branch could be checked."
                        : "Git status timed out before the configured branch target could be checked.");
            }

            return WorkspaceGitLaunchGateResult.StayOpen(
                persistTargetOnFailure
                    ? $"Target set to {target}, but this folder is not a git repository."
                    : "Git branch target is configured, but this folder is not a git repository.");
        }

        if (WorkspaceGitOperations.IsOnBranch(status, target))
        {
            return WorkspaceGitLaunchGateResult.Proceed();
        }

        if (status.IsDirty && blockDirtyBranchSwitch)
        {
            return WorkspaceGitLaunchGateResult.StayOpen(
                persistTargetOnFailure
                    ? $"Target set to {target}, but not switched because the working tree has uncommitted changes."
                    : "The working tree has uncommitted changes. Switch or commit changes before launching.");
        }

        SwitchAttemptCount++;
        if (!_git.TrySwitchBranch(directory, target, out var switchError))
        {
            return WorkspaceGitLaunchGateResult.StayOpen(
                persistTargetOnFailure
                    ? $"Target set to {target}, but not switched because {switchError}"
                    : switchError ?? $"Could not switch to branch '{target}'.");
        }

        return WorkspaceGitLaunchGateResult.Proceed();
    }
}
