namespace QuickShell.Services;

internal sealed record WorkspaceGitLaunchGateResult(bool CanProceed, string? StayOpenMessage)
{
    public static WorkspaceGitLaunchGateResult Proceed() => new(true, null);

    public static WorkspaceGitLaunchGateResult StayOpen(string message) => new(false, message);
}

internal static class WorkspaceGitLaunchGate
{
    internal static int SwitchAttemptCount { get; private set; }

    public static WorkspaceGitLaunchGateResult EvaluateBeforeLaunch(
        string directory,
        bool blockDirtyBranchSwitch)
    {
        var target = WorktreeBranchTargetStore.GetTargetForDirectory(directory);
        if (string.IsNullOrWhiteSpace(target))
        {
            return WorkspaceGitLaunchGateResult.Proceed();
        }

        return TryEnsureTargetBranch(directory, target, blockDirtyBranchSwitch, persistTargetOnFailure: false);
    }

    public static WorkspaceGitLaunchGateResult SelectTargetBranch(
        string directory,
        string branch,
        bool blockDirtyBranchSwitch)
    {
        if (!WorktreeBranchTargetStore.TrySetTargetForDirectory(directory, branch, out var storeError))
        {
            return WorkspaceGitLaunchGateResult.StayOpen(storeError ?? "Could not save branch target.");
        }

        return TryEnsureTargetBranch(directory, branch, blockDirtyBranchSwitch, persistTargetOnFailure: true);
    }

    public static WorkspaceGitLaunchGateResult ClearTargetBranch(string directory)
    {
        WorktreeBranchTargetStore.ClearTargetForDirectory(directory);
        return WorkspaceGitLaunchGateResult.Proceed();
    }

    private static WorkspaceGitLaunchGateResult TryEnsureTargetBranch(
        string directory,
        string target,
        bool blockDirtyBranchSwitch,
        bool persistTargetOnFailure)
    {
        if (!WorkspaceGitOperations.TryGetStatus(directory, out var status))
        {
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
        if (!WorkspaceGitOperations.TrySwitchBranch(directory, target, out var switchError))
        {
            return WorkspaceGitLaunchGateResult.StayOpen(
                persistTargetOnFailure
                    ? $"Target set to {target}, but not switched because {switchError}"
                    : switchError ?? $"Could not switch to branch '{target}'.");
        }

        return WorkspaceGitLaunchGateResult.Proceed();
    }

    internal static void ResetForTests()
    {
        SwitchAttemptCount = 0;
    }
}
