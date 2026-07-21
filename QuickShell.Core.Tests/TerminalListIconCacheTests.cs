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
}
