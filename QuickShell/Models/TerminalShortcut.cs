namespace QuickShell.Models;

internal sealed class TerminalShortcut
{
    public string Name { get; set; } = string.Empty;

    public string? Abbreviation { get; set; }

    public string Directory { get; set; } = string.Empty;

    public string? Command { get; set; }

    public string Terminal { get; set; } = "wt";

    public bool RunAsAdmin { get; set; }
}
