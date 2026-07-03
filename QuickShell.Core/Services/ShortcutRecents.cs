using QuickShell.Models;

namespace QuickShell.Services;

internal static class ShortcutRecents
{
    public const string SectionTitle = "Recent";

    public static List<TerminalShortcut> GetRecentWorkspaces(
        IReadOnlyList<TerminalShortcut> shortcuts,
        int maxCount = QuickShellRecentSettings.DefaultCount)
    {
        var limit = QuickShellRecentSettings.NormalizeCount(maxCount);
        if (limit == 0)
        {
            return [];
        }

        return shortcuts
            .Where(shortcut => shortcut.LastUsedUtc is not null && !shortcut.IsPinned)
            .OrderByDescending(shortcut => shortcut.LastUsedUtc)
            .Take(limit)
            .ToList();
    }
}
