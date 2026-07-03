using QuickShell.Models;

namespace QuickShell.Services;

/// <summary>
/// Relevance scoring for the PowerToys Run plugin. Browse mode (bare <c>qs</c>) must rank
/// shortcuts above manage utilities so results are not hidden by Run's max-result cap.
/// </summary>
internal static class RunQueryScoring
{
    public const int BrowseShortcutBaseScore = 5000;
    public const int BrowseUtilityBaseScore = 100;
    private const int BrowseMaxRecencyBonus = 40;
    private const int BrowsePinnedMinimumBonus = BrowseMaxRecencyBonus + 1;
    private const int BrowseUnorderedPinOrder = 100;

    public static int ComputeShortcutScore(
        TerminalShortcut shortcut,
        string search,
        bool directActivationBrowse,
        DateTime? utcNow = null)
    {
        var now = utcNow ?? DateTime.UtcNow;

        if (directActivationBrowse && string.IsNullOrWhiteSpace(search))
        {
            var score = BrowseShortcutBaseScore;
            if (shortcut.IsPinned)
            {
                var pinOrder = shortcut.PinOrder ?? BrowseUnorderedPinOrder;
                score += Math.Max(
                    BrowsePinnedMinimumBonus,
                    50 + (100 - Math.Min(pinOrder, 99)));
            }
            else
            {
                score += RecencyBonus(shortcut, now);
            }

            return score;
        }

        var result = shortcut.IsPinned ? 100 : 0;
        result += AbbreviationBonus(shortcut, search);
        result += RecencyBonus(shortcut, now);
        return result;
    }

    public static int ComputeUtilityScore(int rankedScore, string search, int utilityOrder) =>
        string.IsNullOrWhiteSpace(search)
            ? BrowseUtilityBaseScore - utilityOrder
            : rankedScore - utilityOrder;

    public static bool ShouldIncludeUtility(string search, string[] keywords)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return true;
        }

        return keywords.Any(keyword => keyword.Contains(search, StringComparison.OrdinalIgnoreCase)
            || search.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private static int AbbreviationBonus(TerminalShortcut shortcut, string search)
    {
        if (string.IsNullOrWhiteSpace(search) || string.IsNullOrWhiteSpace(shortcut.Abbreviation))
        {
            return 0;
        }

        if (shortcut.Abbreviation.Equals(search, StringComparison.OrdinalIgnoreCase))
        {
            return 200;
        }

        if (shortcut.Abbreviation.StartsWith(search, StringComparison.OrdinalIgnoreCase))
        {
            return 120;
        }

        return 0;
    }

    private static int RecencyBonus(TerminalShortcut shortcut, DateTime utcNow)
    {
        if (shortcut.LastUsedUtc is null)
        {
            return 0;
        }

        var ageHours = Math.Max(0, (utcNow - shortcut.LastUsedUtc.Value).TotalHours);
        return (int)Math.Round(Math.Max(0, 40 - ageHours));
    }
}
