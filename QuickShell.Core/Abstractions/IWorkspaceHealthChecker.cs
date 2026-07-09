using QuickShell.Models;
using QuickShell.Services;

namespace QuickShell.Abstractions;

internal interface IWorkspaceHealthChecker
{
    WorkspaceHealthResult Check(
        TerminalShortcut shortcut,
        string terminalApplicationId,
        string defaultProfileId,
        bool includeVolatile = true,
        bool includeGit = true);

    WorkspaceHealthResult CheckEntry(
        TerminalShortcut shortcut,
        WorkspaceEntry entry,
        string terminalApplicationId,
        string defaultProfileId,
        bool includeVolatile = true);
}
