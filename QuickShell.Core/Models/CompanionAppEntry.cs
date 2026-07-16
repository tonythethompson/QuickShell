namespace QuickShell.Models;

/// <summary>
/// One GUI companion app for a workspace (editor, git client, notes, etc.).
/// Ordered list on <see cref="TerminalShortcut.CompanionApps"/>; legacy scalar
/// companion fields are mirrored from the primary (first) entry on normalize.
/// </summary>
internal sealed class CompanionAppEntry
{
    public string Id { get; set; } = string.Empty;

    /// <summary>Executable path or catalog-resolvable name.</summary>
    public string? Path { get; set; }

    /// <summary>Optional arguments. Use <c>.</c> or <c>{folder}</c> for the workspace directory.</summary>
    public string? Arguments { get; set; }

    /// <summary>When true, opens this app whenever the full workspace runs.</summary>
    public bool OpenOnLaunch { get; set; }

    public int Order { get; set; }
}
