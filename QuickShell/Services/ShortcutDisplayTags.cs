using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Models;
using QuickShell.Services;

namespace QuickShell;

internal static class ShortcutDisplayTags
{
    public static Tag[]? BuildTags(TerminalShortcut shortcut)
    {
        var tags = new List<Tag>();
        if (shortcut.RunAsAdmin)
        {
            tags.Add(new Tag(string.Empty)
            {
                Icon = new IconInfo(ShortcutGlyphs.AdminShield),
                ToolTip = "Always run as administrator",
            });
        }

        if (shortcut.IsPinned)
        {
            tags.Add(FavoriteTagStyle.CreateFavoriteTag());
        }

        return tags.Count == 0 ? null : tags.ToArray();
    }
}
