using System.Globalization;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Models;

namespace QuickShell.Services;

internal static class ShortcutDisplay
{
    public static string BuildDirectorySubtitle(TerminalShortcut shortcut)
    {
        return string.Join(" · ", ShortenPath(shortcut.Directory), TerminalCatalog.GetDisplayName(shortcut));
    }

    public static string BuildSubtitle(TerminalShortcut shortcut)
    {
        var parts = new List<string> { ShortenPath(shortcut.Directory) };

        parts.Add(TerminalCatalog.GetDisplayName(shortcut));

        if (!string.IsNullOrWhiteSpace(shortcut.Command))
        {
            parts.Add(shortcut.Command);
        }

        if (!string.IsNullOrWhiteSpace(shortcut.Abbreviation))
        {
            parts.Add($"trigger · {shortcut.Abbreviation}");
        }

        if (shortcut.LastUsedUtc is not null)
        {
            parts.Add(FormatRelativeTime(shortcut.LastUsedUtc.Value));
        }

        return string.Join(" · ", parts);
    }

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
            tags.Add(new Tag(string.Empty)
            {
                Icon = new IconInfo(ShortcutGlyphs.FavoriteFilled),
                ToolTip = "Favorite",
            });
        }

        return tags.Count == 0 ? null : tags.ToArray();
    }

    public static string FormatTerminal(string? launchTargetId) =>
        TerminalCatalog.Resolve(launchTargetId).DisplayName;

    private static string ShortenPath(string path)
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (path.StartsWith(userProfile, StringComparison.OrdinalIgnoreCase))
        {
            return "~" + path[userProfile.Length..];
        }

        return path;
    }

    private static string FormatRelativeTime(DateTime utc)
    {
        var elapsed = DateTime.UtcNow - utc;
        if (elapsed < TimeSpan.FromMinutes(1))
        {
            return "just now";
        }

        if (elapsed < TimeSpan.FromHours(1))
        {
            return $"{(int)elapsed.TotalMinutes}m ago";
        }

        if (elapsed < TimeSpan.FromDays(1))
        {
            return $"{(int)elapsed.TotalHours}h ago";
        }

        if (elapsed < TimeSpan.FromDays(7))
        {
            return $"{(int)elapsed.TotalDays}d ago";
        }

        return utc.ToLocalTime().ToString("MMM d", CultureInfo.InvariantCulture);
    }
}
