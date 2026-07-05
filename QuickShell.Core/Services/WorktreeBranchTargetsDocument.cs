namespace QuickShell.Services;

internal sealed class WorktreeBranchTargetsDocument
{
    public Dictionary<string, string> Targets { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
