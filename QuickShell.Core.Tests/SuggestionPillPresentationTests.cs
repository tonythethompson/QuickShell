using QuickShell.Services;

namespace QuickShell.Core.Tests;

public sealed class SuggestionPillPresentationTests
{
    [Fact]
    public void FormatDisplayTitle_UsesCommandOnlyAndTruncates()
    {
        Assert.Equal("npm test", SuggestionPillPresentation.FormatDisplayTitle("npm test"));
        Assert.Equal(
            "claude",
            SuggestionPillPresentation.FormatDisplayTitle("claude"));

        var longCommand = new string('x', SuggestionPillPresentation.DisplayTitleMaxLength + 5);
        var title = SuggestionPillPresentation.FormatDisplayTitle(longCommand);
        Assert.Equal(SuggestionPillPresentation.DisplayTitleMaxLength, title.Length);
        Assert.EndsWith("…", title, StringComparison.Ordinal);
        Assert.DoesNotContain('·', title);
    }

    [Fact]
    public void FormatTooltip_IncludesCategoryCommandAndProductName()
    {
        Assert.Equal(
            "Test · npm test",
            SuggestionPillPresentation.FormatTooltip("Test", "npm test"));

        Assert.Equal(
            "Agent · Claude Code — Claude Code detected on PATH. Adds `claude` as a launch command.",
            SuggestionPillPresentation.FormatTooltip(
                "Agent",
                "claude",
                productName: "Claude Code",
                detail: "Claude Code detected on PATH. Adds `claude` as a launch command."));
    }
}
