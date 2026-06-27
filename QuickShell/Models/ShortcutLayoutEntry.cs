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

    public string? SeparatorTitle { get; init; }

    public static ShortcutLayoutEntry FromShortcut(TerminalShortcut shortcut) =>
        new()
        {
            Kind = ShortcutLayoutEntryKind.Shortcut,
            Shortcut = shortcut,
        };

    public static ShortcutLayoutEntry FromSeparator(string? title) =>
        new()
        {
            Kind = ShortcutLayoutEntryKind.Separator,
            SeparatorTitle = string.IsNullOrWhiteSpace(title) ? null : title.Trim(),
        };
}
