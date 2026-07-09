namespace QuickShell.Abstractions;

using QuickShell.Services;

internal interface IWorkspaceGitOperations
{
    bool TryGetStatus(string directory, out WorkspaceGitStatus status);

    IReadOnlyList<string> ListLocalBranches(string directory);

    bool TrySwitchBranch(string directory, string branch, out string? error);
}
