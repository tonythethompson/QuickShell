namespace QuickShell.Models;

internal sealed class TerminalShortcut
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Abbreviation { get; set; }

    public string Directory { get; set; } = string.Empty;

    public string? Command { get; set; }

    // "default" means use extension-level default terminal setting.
    public string Terminal { get; set; } = "default";

    // Optional Windows Terminal profile name for terminal=wt/default->wt.
    public string? WtProfile { get; set; }

    public bool RunAsAdmin { get; set; }

    public bool IsPinned { get; set; }

    // Lower number means higher in the favorites section.
    public int? PinOrder { get; set; }

    public DateTime? LastUsedUtc { get; set; }
}
