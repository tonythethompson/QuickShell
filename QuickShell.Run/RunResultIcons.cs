using QuickShell.Abstractions;
using QuickShell.Models;
using QuickShell.Services;
using System.IO;
using System.Text;
using Wox.Plugin;

namespace QuickShell.Run;

internal static class RunResultIcons
{
    public const string SegoeIconFont = "Segoe Fluent Icons,Segoe MDL2 Assets";
    private const string SegoeEmojiFont = "Segoe UI Emoji";

    public static void ApplyToResult(
        Result result,
        string icon,
        TerminalShortcut? shortcut = null,
        ITerminalLaunchGlyphs? glyphs = null)
    {
        icon = NormalizeIcon(icon, shortcut, glyphs);

        if (TryResolveImagePath(icon, out var imagePath))
        {
            result.IcoPath = imagePath;
            result.Glyph = null;
            result.FontFamily = null;
            return;
        }

        result.IcoPath = null;
        result.Glyph = icon;
        result.FontFamily = IsEmojiIcon(icon) ? SegoeEmojiFont : SegoeIconFont;
    }

    public static (string Glyph, string FontFamily) ResolveGlyph(string icon) =>
        IsEmojiIcon(icon)
            ? (icon, SegoeEmojiFont)
            : (icon, SegoeIconFont);

    private static string NormalizeIcon(string icon, TerminalShortcut? shortcut, ITerminalLaunchGlyphs? glyphs)
    {
        if (string.IsNullOrWhiteSpace(icon))
        {
            return ShortcutGlyphs.NewWindow;
        }

        if (TryResolveImagePath(icon, out _)
            || TerminalProfileIconResolver.IsInlineGlyphIcon(icon))
        {
            return icon;
        }

        if (shortcut is not null && glyphs is not null)
        {
            var fallback = glyphs.GetForShortcut(shortcut);
            if (TryResolveImagePath(fallback, out _) || TerminalProfileIconResolver.IsInlineGlyphIcon(fallback))
            {
                return fallback;
            }
        }

        return ShortcutGlyphs.NewWindow;
    }

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

        var normalized = expanded.Replace('/', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(normalized) && File.Exists(normalized))
        {
            imagePath = normalized;
            return true;
        }

        var resolved = TerminalProfileIconResolver.Resolve(icon, string.Empty);
        if (!string.IsNullOrWhiteSpace(resolved) && File.Exists(resolved))
        {
            imagePath = resolved;
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

        foreach (var rune in icon.EnumerateRunes())
        {
            if (rune.Value > 0xFFFF)
            {
                return true;
            }
        }

        return false;
    }
}

