using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Security.Cryptography;
using System.Text;
using QuickShell.Models;

namespace QuickShell.Services;

/// <summary>
/// Resolves and caches list-sized terminal profile icons. CmdPal's IconInfo has no size API;
/// shrinking source PNGs (e.g. PowerShell) before handing them to the host keeps them sharper.
/// </summary>
internal static class TerminalListIconCache
{
    /// <summary>Target edge length for home-list bitmap icons (host still scales; this reduces blur).</summary>
    public const int ListIconPixels = 24;

    private static readonly ConcurrentDictionary<string, string> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object DiskSync = new();

    public static string? TryResolveUpgradedListIcon(TerminalShortcut shortcut)
    {
        if (shortcut.RunAsAdmin)
        {
            return null;
        }

        if (ShortcutHealth.WouldNeedRepair(shortcut, requireDirectoryExists: false))
        {
            return null;
        }

        var resolved = TerminalLaunchGlyphs.GetForShortcut(shortcut);
        if (string.IsNullOrWhiteSpace(resolved))
        {
            return null;
        }

        return PrepareForList(resolved);
    }

    public static string PrepareForList(string icon)
    {
        var trimmed = icon.Trim();
        if (TerminalProfileIconResolver.IsCmdPalGlyphIcon(trimmed)
            || LooksLikeInlineEmoji(trimmed))
        {
            return trimmed;
        }

        if (!Path.IsPathRooted(trimmed) || !File.Exists(trimmed))
        {
            return trimmed;
        }

        // .ico sources skip the GDI+ resize path entirely: some .ico encodings (PNG-compressed
        // large frames in particular) decode via System.Drawing into a blank/transparent bitmap
        // without throwing, so the catch below never catches it — the icon just silently goes
        // blank. The host already scales whatever we hand it, so there's nothing to gain from
        // resizing an .ico and real risk in doing it.
        if (string.Equals(Path.GetExtension(trimmed), ".ico", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        try
        {
            var version = $"{File.GetLastWriteTimeUtc(trimmed).Ticks}:{new FileInfo(trimmed).Length}";
            var cacheKey = $"{trimmed}|{version}";
            return Cache.GetOrAdd(cacheKey, _ => CreateOrGetResizedPath(trimmed, ListIconPixels));
        }
        catch
        {
            return trimmed;
        }
    }

    public static void PrewarmProfiles()
    {
        try
        {
            _ = WtProfilesService.GetProfiles();
        }
        catch
        {
            // Best effort.
        }
    }

    internal static void ResetForTests() => Cache.Clear();

    private static string CreateOrGetResizedPath(string sourcePath, int pixels)
    {
        var cacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "QuickShell",
            "icon-cache");

        var key = HashKey(sourcePath) + $"_{pixels}.png";
        var destPath = Path.Combine(cacheDir, key);

        lock (DiskSync)
        {
            if (File.Exists(destPath))
            {
                try
                {
                    var sourceWrite = File.GetLastWriteTimeUtc(sourcePath);
                    var destWrite = File.GetLastWriteTimeUtc(destPath);
                    if (destWrite >= sourceWrite)
                    {
                        return destPath;
                    }
                }
                catch
                {
                    return sourcePath;
                }
            }

            try
            {
                Directory.CreateDirectory(cacheDir);

                using var source = Image.FromFile(sourcePath);
                if (source.Width <= pixels && source.Height <= pixels)
                {
                    return sourcePath;
                }

                using var resized = new Bitmap(pixels, pixels, PixelFormat.Format32bppArgb);
                using (var graphics = Graphics.FromImage(resized))
                {
                    graphics.Clear(Color.Transparent);
                    graphics.CompositingMode = CompositingMode.SourceOver;
                    graphics.CompositingQuality = CompositingQuality.HighQuality;
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.SmoothingMode = SmoothingMode.HighQuality;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

                    var scale = Math.Min((float)pixels / source.Width, (float)pixels / source.Height);
                    var width = Math.Max(1, (int)Math.Round(source.Width * scale));
                    var height = Math.Max(1, (int)Math.Round(source.Height * scale));
                    var x = (pixels - width) / 2;
                    var y = (pixels - height) / 2;
                    graphics.DrawImage(source, new Rectangle(x, y, width, height));
                }

                resized.Save(destPath, ImageFormat.Png);
                return destPath;
            }
            catch
            {
                return sourcePath;
            }
        }
    }

    private static string HashKey(string path)
    {
        var bytes = Encoding.UTF8.GetBytes(path.ToLowerInvariant());
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash.AsSpan(0, 12));
    }

    private static bool LooksLikeInlineEmoji(string value) =>
        value.Length <= 8
        && !value.Contains('\\', StringComparison.Ordinal)
        && !value.Contains('/', StringComparison.Ordinal)
        && !value.Contains('.', StringComparison.Ordinal);
}
