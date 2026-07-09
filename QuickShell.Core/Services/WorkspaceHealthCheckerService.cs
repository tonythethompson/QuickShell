using QuickShell.Abstractions;
using QuickShell.Models;

namespace QuickShell.Services;

internal sealed class WorkspaceHealthCheckerService : IWorkspaceHealthChecker
{
    public WorkspaceHealthResult Check(
        TerminalShortcut shortcut,
        string terminalApplicationId,
        string defaultProfileId,
        bool includeVolatile = true,
        bool includeGit = true) =>
        WorkspaceHealthCheck.Check(shortcut, terminalApplicationId, defaultProfileId, includeVolatile, includeGit);

    public WorkspaceHealthResult CheckEntry(
        TerminalShortcut shortcut,
        WorkspaceEntry entry,
        string terminalApplicationId,
        string defaultProfileId,
        bool includeVolatile = true) =>
        WorkspaceHealthCheck.CheckEntry(shortcut, entry, terminalApplicationId, defaultProfileId, includeVolatile);
}
