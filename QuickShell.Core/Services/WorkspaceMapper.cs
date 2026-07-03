using QuickShell.Models;

namespace QuickShell.Services;

internal static class WorkspaceMapper
{
    public static Workspace CloneWorkspace(Workspace workspace) => new()
    {
        Id = workspace.Id,
        Name = workspace.Name,
        Abbreviation = workspace.Abbreviation,
        Directory = workspace.Directory,
        IsPinned = workspace.IsPinned,
        PinOrder = workspace.PinOrder,
        Entries = workspace.Entries.Select(CloneEntry).ToList(),
    };

    public static WorkspaceEntry CloneEntry(WorkspaceEntry entry) => new()
    {
        Id = entry.Id,
        Label = entry.Label,
        Terminal = entry.Terminal,
        WtProfile = entry.WtProfile,
        Command = entry.Command,
        RunAsAdmin = entry.RunAsAdmin,
        IsEnabled = entry.IsEnabled,
        Order = entry.Order,
    };

    public static void NormalizeEntryOrders(Workspace workspace)
    {
        var ordered = workspace.Entries.OrderBy(entry => entry.Order).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            ordered[i].Order = i;
        }

        workspace.Entries = ordered;
    }
}
