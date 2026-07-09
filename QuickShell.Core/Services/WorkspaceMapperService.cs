using QuickShell.Abstractions;
using QuickShell.Models;

namespace QuickShell.Services;

internal sealed class WorkspaceMapperService : IWorkspaceMapper
{
    public Workspace CloneWorkspace(Workspace workspace) => WorkspaceMapper.CloneWorkspace(workspace);

    public WorkspaceEntry CloneEntry(WorkspaceEntry entry) => WorkspaceMapper.CloneEntry(entry);

    public void NormalizeEntryOrders(Workspace workspace) => WorkspaceMapper.NormalizeEntryOrders(workspace);
}
