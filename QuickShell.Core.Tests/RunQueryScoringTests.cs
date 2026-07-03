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
            LastUsedUtc = DateTime.UtcNow,
        };
        var olderFirst = new TerminalShortcut
        {
            Name = "A",
            IsPinned = true,
            PinOrder = 1,
            LastUsedUtc = null,
        };

        var firstScore = RunQueryScoring.ComputeShortcutScore(olderFirst, string.Empty, directActivationBrowse: true);
        var secondScore = RunQueryScoring.ComputeShortcutScore(recentSecond, string.Empty, directActivationBrowse: true);

        Assert.True(firstScore > secondScore);
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
        var utilityScore = RunQueryScoring.ComputeUtilityScore(2000, "export", utilityOrder: 0);
        Assert.Equal(2000, utilityScore);
    }
}
