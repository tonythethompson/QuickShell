using QuickShell.Models;

namespace QuickShell.Services;

internal sealed class WorkspaceTaskAction
{
    public required TerminalShortcut Workspace { get; init; }

    public required WorkspaceEntry Launch { get; init; }

    public required int Score { get; init; }
}
