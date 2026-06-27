using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Models;

namespace QuickShell.Services;

internal static class ShortcutLayoutDisplay
{
    public const string FavoritesSectionTitle = "Favorites";
    public const string ShortcutsSectionTitle = "Shortcuts";

    public static IEnumerable<IListItem> BuildListItems(
        IReadOnlyList<ShortcutLayoutEntry> layout,
        Func<TerminalShortcut, IListItem> buildShortcutItem)
    {
        var pinned = layout
            .Where(entry => entry.Kind == ShortcutLayoutEntryKind.Shortcut && entry.Shortcut?.IsPinned == true)
            .Select(entry => entry.Shortcut!)
            .OrderBy(shortcut => shortcut.PinOrder ?? int.MaxValue)
            .ThenBy(shortcut => shortcut.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (pinned.Count > 0)
        {
            yield return new Separator(FavoritesSectionTitle);
            foreach (var shortcut in pinned)
            {
                yield return buildShortcutItem(shortcut);
            }

            yield return new Separator(ShortcutsSectionTitle);
        }

        foreach (var entry in layout)
        {
            switch (entry.Kind)
            {
                case ShortcutLayoutEntryKind.Separator:
                    yield return new Separator(entry.SeparatorTitle ?? string.Empty);
                    break;
                case ShortcutLayoutEntryKind.Shortcut when entry.Shortcut is { IsPinned: false } shortcut:
                    yield return buildShortcutItem(shortcut);
                    break;
            }
        }
    }
}
