using QuickShell.Models;
using QuickShell.Services;

namespace QuickShell.Core.Tests;

public sealed class RunQueryScoringTests
{
    [Fact]
    public void BrowseMode_ShortcutsOutrankUtilities()
    {
        var shortcut = new TerminalShortcut { Name = "Demo", Directory = @"C:\Demo" };
        var shortcutScore = RunQueryScoring.ComputeShortcutScore(shortcut, search: string.Empty, directActivationBrowse: true);
        var utilityScore = RunQueryScoring.ComputeUtilityScore(rankedScore: 2000, search: string.Empty, utilityOrder: 0);

        Assert.True(shortcutScore > utilityScore);
    }

    [Fact]
    public void BrowseMode_PinnedShortcutsRespectPinOrder()
    {
        var first = new TerminalShortcut { Name = "A", IsPinned = true, PinOrder = 1 };
        var second = new TerminalShortcut { Name = "B", IsPinned = true, PinOrder = 2 };

        var firstScore = RunQueryScoring.ComputeShortcutScore(first, string.Empty, directActivationBrowse: true);
        var secondScore = RunQueryScoring.ComputeShortcutScore(second, string.Empty, directActivationBrowse: true);

        Assert.True(firstScore > secondScore);
    }

    [Fact]
    public void BrowseMode_PinOrderBeatsRecency()
    {
        var recentSecond = new TerminalShortcut
        {
            Name = "B",
            IsPinned = true,
            PinOrder = 2,
            LastUsedUtc = new DateTime(2024, 6, 1, 12, 0, 0, DateTimeKind.Utc),
        };
        var olderFirst = new TerminalShortcut
        {
            Name = "A",
            IsPinned = true,
            PinOrder = 1,
            LastUsedUtc = null,
        };

        var now = new DateTime(2024, 6, 1, 13, 0, 0, DateTimeKind.Utc);
        var firstScore = RunQueryScoring.ComputeShortcutScore(olderFirst, string.Empty, directActivationBrowse: true, now);
        var secondScore = RunQueryScoring.ComputeShortcutScore(recentSecond, string.Empty, directActivationBrowse: true, now);

        Assert.True(firstScore > secondScore);
    }

    [Fact]
    public void BrowseMode_UnorderedPinnedShortcutsRankBelowExplicitOrder()
    {
        var ordered = new TerminalShortcut { Name = "A", IsPinned = true, PinOrder = 1 };
        var unordered = new TerminalShortcut { Name = "B", IsPinned = true, PinOrder = null };

        var orderedScore = RunQueryScoring.ComputeShortcutScore(ordered, string.Empty, directActivationBrowse: true);
        var unorderedScore = RunQueryScoring.ComputeShortcutScore(unordered, string.Empty, directActivationBrowse: true);

        Assert.True(orderedScore > unorderedScore);
    }

    [Fact]
    public void BrowseMode_UnorderedPinnedBeatsRecentUnpinned()
    {
        var favorite = new TerminalShortcut { Name = "Fav", IsPinned = true, PinOrder = null };
        var recent = new TerminalShortcut
        {
            Name = "Recent",
            LastUsedUtc = new DateTime(2024, 6, 1, 12, 0, 0, DateTimeKind.Utc),
        };

        var now = new DateTime(2024, 6, 1, 13, 0, 0, DateTimeKind.Utc);
        var favoriteScore = RunQueryScoring.ComputeShortcutScore(favorite, string.Empty, directActivationBrowse: true, now);
        var recentScore = RunQueryScoring.ComputeShortcutScore(recent, string.Empty, directActivationBrowse: true, now);

        Assert.True(favoriteScore > recentScore);
    }

    [Fact]
    public void BrowseMode_UtilitiesPreserveDeclarationOrder()
    {
        var firstUtility = RunQueryScoring.ComputeUtilityScore(2000, string.Empty, utilityOrder: 0);
        var secondUtility = RunQueryScoring.ComputeUtilityScore(2000, string.Empty, utilityOrder: 1);

        Assert.True(firstUtility > secondUtility);
    }

    [Fact]
    public void SearchMode_UtilitiesKeepHighRankWhenMatched()
    {
        var firstUtility = RunQueryScoring.ComputeUtilityScore(2000, "export", utilityOrder: 0);
        var secondUtility = RunQueryScoring.ComputeUtilityScore(2000, "export", utilityOrder: 1);

        Assert.Equal(2000, firstUtility);
        Assert.True(firstUtility > secondUtility);
    }
}
