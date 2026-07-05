namespace QuickShell.Models;

internal sealed class WorkspaceEntry
{
    public string Id { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public string Terminal { get; set; } = "default";

    public string? WtProfile { get; set; }

    public string? Command { get; set; }

    public bool RunAsAdmin { get; set; }

    public bool IsEnabled { get; set; } = true;

    public int Order { get; set; }

    public string TaskType { get; set; } = "none";
}
