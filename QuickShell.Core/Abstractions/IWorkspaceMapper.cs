using QuickShell.Models;

namespace QuickShell.Abstractions;

internal interface IWorkspaceMapper
{
    Workspace CloneWorkspace(Workspace workspace);

    WorkspaceEntry CloneEntry(WorkspaceEntry entry);

    void NormalizeEntryOrders(Workspace workspace);
}
