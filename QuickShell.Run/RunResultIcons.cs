using QuickShell.Services;
using System.IO;
using Wox.Plugin;

namespace QuickShell.Run;

internal static class RunResultIcons
{
    private const string SegoeMdl2Font = "Segoe MDL2 Assets";
    private const string SegoeEmojiFont = "Segoe UI Emoji";

    public static void ApplyToResult(Result result, string icon)
    {
        if (TryResolveImagePath(icon, out var imagePath))
        {
            result.IcoPath = imagePath;
            result.Glyph = null;
            result.FontFamily = null;
            return;
        }

        result.IcoPath = null;
        result.Glyph = icon;
        result.FontFamily = IsEmojiIcon(icon) ? SegoeEmojiFont : SegoeMdl2Font;
    }

    public static (string Glyph, string FontFamily) ResolveGlyph(string icon) =>
        IsEmojiIcon(icon)
            ? (icon, SegoeEmojiFont)
            : (icon, SegoeMdl2Font);

    private static bool TryResolveImagePath(string? icon, out string imagePath)
    {
        imagePath = string.Empty;
        if (string.IsNullOrWhiteSpace(icon))
        {
            return false;
        }

        if (Path.IsPathRooted(icon) && File.Exists(icon))
        {
            imagePath = icon;
            return true;
        }

        var expanded = Environment.ExpandEnvironmentVariables(icon.Trim());
        if (Path.IsPathRooted(expanded) && File.Exists(expanded))
        {
            imagePath = expanded;
            return true;
        }

        return false;
    }

    private static bool IsEmojiIcon(string icon)
    {
        if (string.IsNullOrWhiteSpace(icon))
        {
            return false;
        }

        if (!TerminalProfileIconResolver.IsInlineGlyphIcon(icon))
        {
            return false;
        }

        foreach (var ch in icon)
        {
            if (ch > 0xFFFF)
            {
                return true;
            }
        }

        return false;
    }
}
