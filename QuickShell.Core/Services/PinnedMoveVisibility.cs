using QuickShell.Models;

namespace QuickShell.Services;

internal readonly record struct PinnedMoveVisibility(
    bool ShowUp,
    bool ShowDown,
    bool ShowToTop,
    bool ShowToBottom)
{
    public static PinnedMoveVisibility None => default;

    public static PinnedMoveVisibility FromPinnedIndex(int index, int pinnedCount)
    {
        if (index < 0 || pinnedCount <= 1)
        {
            return None;
        }

        return new(
            ShowUp: index > 0,
            ShowDown: index < pinnedCount - 1,
            ShowToTop: index > 0,
            ShowToBottom: index < pinnedCount - 1);
    }

    public static PinnedMoveVisibility ForShortcut(TerminalShortcut shortcut, IReadOnlyList<TerminalShortcut> pinnedInOrder)
    {
        if (!shortcut.IsPinned || pinnedInOrder.Count <= 1)
        {
            return None;
        }

        for (var i = 0; i < pinnedInOrder.Count; i++)
        {
            var candidate = pinnedInOrder[i];
            if ((!string.IsNullOrWhiteSpace(shortcut.Id)
                    && !string.IsNullOrWhiteSpace(candidate.Id)
                    && candidate.Id.Equals(shortcut.Id, StringComparison.OrdinalIgnoreCase))
                || candidate.Name.Equals(shortcut.Name, StringComparison.OrdinalIgnoreCase))
            {
                return FromPinnedIndex(i, pinnedInOrder.Count);
            }
        }

        return None;
    }
}
