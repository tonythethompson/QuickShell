using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Security.Cryptography;
using System.Text;
using QuickShell.Abstractions;
using QuickShell.Models;

namespace QuickShell.Services;

/// <summary>
/// Resolves and caches list-sized terminal profile icons. CmdPal's IconInfo has no size API;
/// shrinking source PNGs (e.g. PowerShell) before handing them to the host keeps them sharper.
/// </summary>
internal sealed class TerminalListIconCache : ITerminalListIconCache
{
    /// <summary>Target edge length for home-list bitmap icons (host still scales; this reduces blur).</summary>
    public const int ListIconPixels = 24;

    /// <summary>
    /// Sources at or under this size skip the GDI+ resize entirely and go straight to the host.
    /// Deliberately larger than ListIconPixels: Windows Terminal's bundled profile icons (e.g.
    /// PowerShell, cmd) ship at 32x32, and a 32-to-24 downscale is small enough that the host's
    /// own scaling looks fine — resizing it ourselves means every one of those icons pays a full
    /// decode+composite+PNG-encode+disk-write under a single global lock on every cold start,
    /// which is what caused the post-scale-reorder perf regression (icons + git discovery both
    /// stalling behind that lock). Only genuinely large custom icons still get downsized.
    /// </summary>
    private const int SkipResizeThreshold = ListIconPixels * 2;

    private readonly ConcurrentDictionary<string, string> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _diskSync = new();
    private readonly IWtProfilesService _profiles;
    private readonly ITerminalLaunchGlyphs _glyphs;
    private readonly IAppDataPaths _appDataPaths;

    public TerminalListIconCache(
        IWtProfilesService profiles,
        ITerminalLaunchGlyphs glyphs,
        IAppDataPaths appDataPaths)
    {
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        _glyphs = glyphs ?? throw new ArgumentNullException(nameof(glyphs));
        _appDataPaths = appDataPaths ?? throw new ArgumentNullException(nameof(appDataPaths));
    }

    public string? TryResolveUpgradedListIcon(TerminalShortcut shortcut)
    {
        if (shortcut.RunAsAdmin)
        {
            return null;
        }

        if (ShortcutHealth.WouldNeedRepair(shortcut, requireDirectoryExists: false))
        {
            return null;
        }

        var resolved = _glyphs.GetForShortcut(shortcut);
        if (string.IsNullOrWhiteSpace(resolved))
        {
            return null;
        }

        return PrepareForList(resolved);
    }

    public string PrepareForList(string icon)
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
            return _cache.GetOrAdd(cacheKey, _ => CreateOrGetResizedPath(trimmed, ListIconPixels));
        }
        catch
        {
            return trimmed;
        }
    }

    public void PrewarmProfiles()
    {
        try
        {
            var profiles = _profiles.GetProfiles();
            if (profiles.Count == 0)
            {
                return;
            }

            // Only prewarm packaged install paths when there are profiles that could use
            // ms-appx:///ProfileIcons/... resolution. Unpackaged dev builds keep their
            // assets alongside the executable and should not force a PowerShell fallback.
            var mayHavePackagedIcon = profiles.Any(p =>
                p.Source != TerminalSettingsSource.Unpackaged
                || (!string.IsNullOrWhiteSpace(p.Icon)
                    && p.Icon.StartsWith("ms-appx", StringComparison.OrdinalIgnoreCase)));
            if (mayHavePackagedIcon)
            {
                _ = WindowsTerminalInstallDiscovery.GetInstallPaths();
            }
        }
        catch
        {
            // Best effort.
        }
    }

    private string CreateOrGetResizedPath(string sourcePath, int pixels)
    {
        var cacheDir = Path.Join(_appDataPaths.Root, "QuickShell", "icon-cache");

        var key = HashKey(sourcePath) + $"_{pixels}.png";
        var destPath = Path.Join(cacheDir, key);

        lock (_diskSync)
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
                if (source.Width <= SkipResizeThreshold && source.Height <= SkipResizeThreshold)
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
