using QuickShell.Abstractions;
using QuickShell.Services;

namespace QuickShell.Core.Tests;

/// <summary>
/// TerminalListIconCache is the public boundary of the async list-icon upgrade:
/// PrepareForList decides whether to pass a glyph through unchanged or resize an
/// on-disk profile PNG. The generation/ordering guard that gates the upgrade lives
/// in QuickShellPage and is exercised through ExtensionCallbackQueue (FIFO + swallow).
/// </summary>
public sealed class TerminalListIconCacheTests
{
    private readonly TerminalListIconCache _cache;

    public TerminalListIconCacheTests()
    {
        var profiles = new WtProfilesService([]);
        var catalog = new TerminalCatalog(profiles);
        var testRoot = Path.Join(Path.GetTempPath(), "qs-test-appdata-" + Guid.NewGuid().ToString("N"));
        var appDataPaths = new AppDataPaths(testRoot);
        var glyphs = new TerminalLaunchGlyphs(
            new TerminalProfileResolver(new QuickShellSettingsReader(appDataPaths: null, catalog), profiles, catalog));
        _cache = new TerminalListIconCache(profiles, glyphs, appDataPaths);
    }

    [Fact]
    public void PrepareForList_passes_emoji_glyph_through_unchanged()
    {
        const string glyph = "🚀";

        var result = _cache.PrepareForList(glyph);

        Assert.Equal(glyph, result);
    }

    [Fact]
    public void PrepareForList_passes_cmdpal_glyph_through_unchanged()
    {
        const string glyph = "ms-appx:///Assets/SomeGlyph.png";

        var result = _cache.PrepareForList(glyph);

        Assert.Equal(glyph, result);
    }

    [Fact]
    public void PrepareForList_trims_surrounding_whitespace()
    {
        var result = _cache.PrepareForList("  🚀  ");

        Assert.Equal("🚀", result);
    }

    [Fact]
    public void PrepareForList_does_not_resize_missing_root_path()
    {
        var missing = Path.Combine(Path.GetTempPath(), "qs-missing-icon-" + Guid.NewGuid().ToString("N") + ".png");

        var result = _cache.PrepareForList(missing);

        Assert.Equal(missing, result);
    }

    [Fact]
    public void PrewarmProfiles_DoesNotThrow_WhenProfilesEmpty()
    {
        var exception = Record.Exception(() => _cache.PrewarmProfiles());
        Assert.Null(exception);
    }

    [Fact]
    public void PrepareForList_ConvertsLargeNonBlankIcoToPng()
    {
        var ico = CreateIco(64, 64, color: System.Drawing.Color.Blue);

        var result = _cache.PrepareForList(ico);

        Assert.NotEqual(ico, result);
        Assert.Equal(".png", Path.GetExtension(result), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void PrepareForList_ReturnsBlankIcoSourceWhenGdiDecodesBlank()
    {
        var ico = CreateIco(64, 64, color: null);

        var result = _cache.PrepareForList(ico);

        Assert.Equal(ico, result);
    }

    [Fact]
    public void prepare_for_list_does_not_treat_non_blank_png_as_blank()
    {
        var png = CreatePng(64, 64, color: System.Drawing.Color.Red);

        var result = _cache.PrepareForList(png);

        Assert.NotEqual(png, result);
        Assert.Equal(".png", Path.GetExtension(result), StringComparer.OrdinalIgnoreCase);
    }

    private static string CreateIco(int width, int height, System.Drawing.Color? color)
    {
        var path = Path.Combine(Path.GetTempPath(), $"qs-ico-{width}-{Guid.NewGuid():N}.ico");
        using var bitmap = new System.Drawing.Bitmap(width, height);
        if (color is { } c)
        {
            using var graphics = System.Drawing.Graphics.FromImage(bitmap);
            graphics.Clear(c);
        }
        else
        {
            bitmap.MakeTransparent();
        }

        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Icon);
        return path;
    }

    private static string CreatePng(int width, int height, System.Drawing.Color color)
    {
        var path = Path.Combine(Path.GetTempPath(), $"qs-png-{width}-{Guid.NewGuid():N}.png");
        using var bitmap = new System.Drawing.Bitmap(width, height);
        using var graphics = System.Drawing.Graphics.FromImage(bitmap);
        graphics.Clear(color);
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        return path;
    }
}
