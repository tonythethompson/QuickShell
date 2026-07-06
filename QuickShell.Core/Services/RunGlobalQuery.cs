namespace QuickShell.Services;

/// <summary>
/// Global (non-<c>qs</c>) PowerToys Run query handling for the Quick Shell plugin.
/// </summary>
internal static class RunGlobalQuery
{
    private static readonly string[] ActivationPhrases =
    [
        "quick shell",
        "quickshell",
        "quick-shell",
        "quick shell for cmdpal",
        "quickshellforcmdpal",
    ];

    public static bool ShouldSuppressEmptyGlobalQuery(QueryActivationContext context) =>
        !context.HasActionKeyword && string.IsNullOrWhiteSpace(context.Search);

    public static bool TryActivate(string? search, string? rawQuery, out string remainingSearch)
    {
        foreach (var candidate in GetActivationCandidates(search, rawQuery))
        {
            if (TryActivateCandidate(candidate, out remainingSearch))
            {
                return true;
            }
        }

        remainingSearch = search?.Trim() ?? string.Empty;
        return false;
    }

    private static IEnumerable<string> GetActivationCandidates(string? search, string? rawQuery)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in new[] { search, rawQuery })
        {
            var trimmed = value?.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || !seen.Add(trimmed))
            {
                continue;
            }

            yield return trimmed;
        }
    }

    private static bool TryActivateCandidate(string candidate, out string remainingSearch)
    {
        foreach (var phrase in ActivationPhrases)
        {
            if (candidate.Equals(phrase, StringComparison.OrdinalIgnoreCase))
            {
                remainingSearch = string.Empty;
                return true;
            }

            if (candidate.StartsWith(phrase, StringComparison.OrdinalIgnoreCase)
                && candidate.Length > phrase.Length
                && char.IsWhiteSpace(candidate[phrase.Length]))
            {
                remainingSearch = candidate[phrase.Length..].Trim();
                return true;
            }
        }

        if (candidate.Equals("qs", StringComparison.OrdinalIgnoreCase))
        {
            remainingSearch = string.Empty;
            return true;
        }

        remainingSearch = candidate;
        return false;
    }
}

internal readonly record struct QueryActivationContext(bool HasActionKeyword, string Search);
