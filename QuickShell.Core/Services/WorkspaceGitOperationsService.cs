using QuickShell.Abstractions;

namespace QuickShell.Services;

internal sealed class WorkspaceGitOperationsService : IWorkspaceGitOperations
{
    public bool TryGetStatus(string directory, out WorkspaceGitStatus status) =>
        WorkspaceGitOperations.TryGetStatus(directory, out status);

    public IReadOnlyList<string> ListLocalBranches(string directory) =>
        WorkspaceGitOperations.ListLocalBranches(directory);

    public bool TrySwitchBranch(string directory, string branch, out string? error) =>
        WorkspaceGitOperations.TrySwitchBranch(directory, branch, out error);
}
