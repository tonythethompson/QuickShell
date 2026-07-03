using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Services;

namespace QuickShell;

internal static class FavoriteTagStyle
{
    public static Tag CreateFavoriteTag(string? text = null)
    {
        var tag = string.IsNullOrEmpty(text)
            ? new Tag(string.Empty)
            : new Tag(text);

        tag.Icon = new IconInfo(ShortcutGlyphs.FavoriteFilled);
        tag.ToolTip = "Favorite";
        tag.Foreground = ColorHelpers.FromRgb(255, 200, 60);
        tag.Background = ColorHelpers.FromRgb(80, 60, 10);
        return tag;
    }
}
