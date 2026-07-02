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

    public static int ComputeShortcutScore(TerminalShortcut shortcut, string search, bool directActivationBrowse)
    {
        if (directActivationBrowse && string.IsNullOrWhiteSpace(search))
        {
            var score = BrowseShortcutBaseScore;
            if (shortcut.IsPinned)
            {
                score += 1000 - Math.Min(shortcut.PinOrder ?? 0, 999);
            }

            score += RecencyBonus(shortcut);
            return score;
        }

        var result = shortcut.IsPinned ? 100 : 0;
        result += AbbreviationBonus(shortcut, search);
        result += RecencyBonus(shortcut);
        return result;
    }

    public static int ComputeUtilityScore(int rankedScore, string search, int utilityOrder) =>
        string.IsNullOrWhiteSpace(search)
            ? BrowseUtilityBaseScore + utilityOrder
            : rankedScore;

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

    private static int RecencyBonus(TerminalShortcut shortcut)
    {
        if (shortcut.LastUsedUtc is null)
        {
            return 0;
        }

        var ageHours = Math.Max(0, (DateTime.UtcNow - shortcut.LastUsedUtc.Value).TotalHours);
        return (int)Math.Max(0, 40 - ageHours);
    }
}
