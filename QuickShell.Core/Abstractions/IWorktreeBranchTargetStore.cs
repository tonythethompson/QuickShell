namespace QuickShell.Abstractions;

internal interface IWorktreeBranchTargetStore
{
    string? GetTarget(string worktreeKey);

    string? GetTargetForDirectory(string directory, IWorkspaceGitOperations git);

    void SetTarget(string worktreeKey, string? branch);

    bool TrySetTargetForDirectory(string directory, string? branch, IWorkspaceGitOperations git, out string? error);

    void ClearTargetForDirectory(string directory, IWorkspaceGitOperations git);
}
