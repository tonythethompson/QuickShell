namespace QuickShell.Abstractions;

using QuickShell.Services;

internal interface IWorkspaceGitOperations
{
    bool TryResolveWorktreeKey(string directory, out string worktreeKey);

    bool TryGetStatus(string directory, out WorkspaceGitStatus status);

    bool TryGetStatusForLaunch(string directory, out WorkspaceGitStatus status);

    IReadOnlyList<string> ListLocalBranches(string directory);

    bool TrySwitchBranch(string directory, string branch, out string? error);
}
