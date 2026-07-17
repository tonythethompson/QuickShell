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
    public TerminalListIconCacheTests()
    {
        TerminalListIconCache.ResetForTests();
    }

    [Fact]
    public void PrepareForList_passes_emoji_glyph_through_unchanged()
    {
        const string glyph = "🚀";

        var result = TerminalListIconCache.PrepareForList(glyph);

        Assert.Equal(glyph, result);
    }

    [Fact]
    public void PrepareForList_passes_cmdpal_glyph_through_unchanged()
    {
        const string glyph = "ms-appx:///Assets/SomeGlyph.png";

        var result = TerminalListIconCache.PrepareForList(glyph);

        Assert.Equal(glyph, result);
    }

    [Fact]
    public void PrepareForList_trims_surrounding_whitespace()
    {
        var result = TerminalListIconCache.PrepareForList("  🚀  ");

        Assert.Equal("🚀", result);
    }

    [Fact]
    public void PrepareForList_does_not_resize_missing_root_path()
    {
        var missing = Path.Combine(Path.GetTempPath(), "qs-missing-icon-" + Guid.NewGuid().ToString("N") + ".png");

        var result = TerminalListIconCache.PrepareForList(missing);

        Assert.Equal(missing, result);
    }
}
