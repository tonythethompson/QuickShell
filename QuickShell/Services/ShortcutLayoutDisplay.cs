using Microsoft.CommandPalette.Extensions;
using QuickShell.Models;

namespace QuickShell.Services;

internal static class ShortcutLayoutDisplay
{
    public const string FavoritesSectionTitle = "Favorites";

    public const string ShortcutsSectionTitle = "Workspaces";

    public static IEnumerable<IListItem> BuildListItems(
        IReadOnlyList<ShortcutLayoutEntry> layout,
        Func<TerminalShortcut, IListItem> buildShortcutItem,
        IReadOnlySet<string>? excludeShortcutIds = null)
    {
        foreach (var item in BuildFavoriteItems(layout, buildShortcutItem))
        {
            yield return item;
        }

        foreach (var item in BuildWorkspaceItems(
                     layout,
                     buildShortcutItem,
                     excludeShortcutIds,
                     showDefaultWorkspacesHeader: GetPinnedShortcuts(layout).Count > 0))
        {
            yield return item;
        }
    }

    public static IEnumerable<IListItem> BuildFavoriteItems(
        IReadOnlyList<ShortcutLayoutEntry> layout,
        Func<TerminalShortcut, IListItem> buildShortcutItem)
    {
        var pinned = GetPinnedShortcuts(layout);
        if (pinned.Count == 0)
        {
            yield break;
        }

        foreach (var item in SectionListItems.InSection(
                     FavoritesSectionTitle,
                     pinned.Select(buildShortcutItem)))
        {
            yield return item;
        }
    }

    public static IEnumerable<IListItem> BuildWorkspaceItems(
        IReadOnlyList<ShortcutLayoutEntry> layout,
        Func<TerminalShortcut, IListItem> buildShortcutItem,
        IReadOnlySet<string>? excludeShortcutIds = null,
        bool showDefaultWorkspacesHeader = false)
    {
        excludeShortcutIds ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var activeWorkspaceSection = showDefaultWorkspacesHeader ? ShortcutsSectionTitle : null;
        var workspaceHeaderEmitted = false;

        foreach (var entry in layout)
        {
            switch (entry.Kind)
            {
                case ShortcutLayoutEntryKind.Separator:
                    activeWorkspaceSection = string.IsNullOrWhiteSpace(entry.SeparatorTitle)
                        ? ShortcutsSectionTitle
                        : entry.SeparatorTitle;
                    workspaceHeaderEmitted = false;
                    break;
                case ShortcutLayoutEntryKind.Shortcut when entry.Shortcut is { IsPinned: false } shortcut
                    && !excludeShortcutIds.Contains(shortcut.Id):
                    if (!string.IsNullOrWhiteSpace(activeWorkspaceSection) && !workspaceHeaderEmitted)
                    {
                        yield return SectionListItems.CreateHeader(activeWorkspaceSection);
                        workspaceHeaderEmitted = true;
                    }

                    yield return buildShortcutItem(shortcut);
                    break;
            }
        }
    }

    private static List<TerminalShortcut> GetPinnedShortcuts(IReadOnlyList<ShortcutLayoutEntry> layout) =>
        layout
            .Where(entry => entry.Kind == ShortcutLayoutEntryKind.Shortcut && entry.Shortcut?.IsPinned == true)
            .Select(entry => entry.Shortcut!)
            .OrderBy(shortcut => shortcut.PinOrder ?? int.MaxValue)
            .ThenBy(shortcut => shortcut.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
}
