namespace QuickShell.Models;

internal enum ShortcutLayoutEntryKind
{
    Shortcut,
    Separator,
}

internal sealed class ShortcutLayoutEntry
{
    public ShortcutLayoutEntryKind Kind { get; init; }

    public TerminalShortcut? Shortcut { get; set; }

    /// <summary>Repository-owned metadata for local persistence; omitted from portable export.</summary>
    public WorkspaceSecurityMetadata? Security { get; internal set; }

    public string? SeparatorTitle { get; init; }

    public static ShortcutLayoutEntry FromShortcut(
        TerminalShortcut shortcut,
        WorkspaceSecurityMetadata? security = null) =>
        new()
        {
            Kind = ShortcutLayoutEntryKind.Shortcut,
            Shortcut = shortcut,
            Security = security is null ? new WorkspaceSecurityMetadata() : security with { },
        };

    public static ShortcutLayoutEntry FromSeparator(string? title) =>
        new()
        {
            Kind = ShortcutLayoutEntryKind.Separator,
            SeparatorTitle = string.IsNullOrWhiteSpace(title) ? null : title.Trim(),
        };
}
