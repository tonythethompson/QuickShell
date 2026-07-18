using QuickShell.Models;
using System.Linq;

namespace QuickShell.Services;

/// <summary>
/// A consistent, versioned point-in-time view of the workspace repository.
/// All reads against a single snapshot see the same shortcuts and layout, and the
/// version token makes cache invalidation cheap for consumers like pages.
/// </summary>
internal readonly record struct WorkspaceRepositorySnapshot(
    long Version,
    IReadOnlyList<TerminalShortcut> Shortcuts,
    IReadOnlyList<ShortcutLayoutEntry> Layout)
{
    public IEnumerable<TerminalShortcut> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Shortcuts;
        }

        if (!TryGetTrimmedQueryRange(query, out var queryStart, out var queryLength))
        {
            return [];
        }

        List<TerminalShortcut>? matches = null;
        foreach (var shortcut in Shortcuts.Where(shortcut =>
                     Matches(shortcut, query, queryStart, queryLength)))
        {
            matches ??= [];
            matches.Add(ShortcutRepository.Clone(shortcut));
        }

        return matches is null ? [] : matches.ToArray();
    }

    public IEnumerable<TerminalShortcut> SearchForRootPalette(string query)
    {
        if (!TryGetTrimmedQueryRange(query, out var queryStart, out var queryLength))
        {
            return [];
        }

        List<TerminalShortcut>? abbreviationMatches = null;
        List<TerminalShortcut>? matches = null;
        foreach (var shortcut in Shortcuts)
        {
            if (ContainsText(shortcut.Abbreviation, query, queryStart, queryLength))
            {
                abbreviationMatches ??= [];
                abbreviationMatches.Add(shortcut);
                continue;
            }

            if (!MatchesForRootPalette(shortcut, query, queryStart, queryLength))
            {
                continue;
            }

            matches ??= [];
            matches.Add(shortcut);
        }

        if (abbreviationMatches is not null)
        {
            abbreviationMatches.Sort((left, right) =>
                CompareAbbreviationMatch(left, right, query, queryStart, queryLength));
            return ShortcutRepository.CloneAll(abbreviationMatches);
        }

        return matches is null ? [] : ShortcutRepository.CloneAll(matches);
    }

    public IEnumerable<WorkspaceTaskAction> SearchTaskActions(string query)
    {
        var tokens = GetTaskSearchTokens(query);
        if (tokens.Length == 0)
        {
            return [];
        }

        List<WorkspaceTaskAction>? matches = null;
        foreach (var shortcut in Shortcuts)
        {
            if (shortcut.Launches is not { Count: > 0 })
            {
                continue;
            }

            bool? requiresRepair = null;
            foreach (var launch in shortcut.Launches)
            {
                if (!launch.IsEnabled)
                {
                    continue;
                }

                var score = ComputeTaskActionScore(shortcut, launch, tokens);
                if (score <= 0)
                {
                    continue;
                }

                requiresRepair ??= ShortcutHealth.WouldNeedRepair(shortcut);
                if (requiresRepair.Value)
                {
                    break;
                }

                matches ??= [];
                matches.Add(CreateTaskAction(shortcut, launch, score));
            }
        }

        if (matches is null)
        {
            return [];
        }

        matches.Sort(static (left, right) =>
        {
            var byScore = right.Score.CompareTo(left.Score);
            if (byScore != 0)
            {
                return byScore;
            }

            var byName = string.Compare(
                left.Workspace.Name,
                right.Workspace.Name,
                StringComparison.OrdinalIgnoreCase);
            return byName != 0 ? byName : left.Launch.Order.CompareTo(right.Launch.Order);
        });
        return matches.ToArray();
    }

    private static bool Matches(TerminalShortcut shortcut, string query, int queryStart, int queryLength)
    {
        if (MatchesForRootPalette(shortcut, query, queryStart, queryLength))
        {
            return true;
        }

        if (ContainsText(shortcut.Abbreviation, query, queryStart, queryLength))
        {
            return true;
        }

        if (ContainsText(shortcut.Command, query, queryStart, queryLength))
        {
            return true;
        }

        foreach (var launch in shortcut.Launches)
        {
            if (ContainsText(launch.Label, query, queryStart, queryLength))
            {
                return true;
            }

            if (ContainsText(launch.Command, query, queryStart, queryLength))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesForRootPalette(TerminalShortcut shortcut, string query, int queryStart, int queryLength)
    {
        if (ContainsText(shortcut.Name, query, queryStart, queryLength))
        {
            return true;
        }

        if (ContainsText(shortcut.Directory, query, queryStart, queryLength))
        {
            return true;
        }

        return ContainsText(shortcut.WtProfile, query, queryStart, queryLength);
    }

    private static WorkspaceTaskAction CreateTaskAction(TerminalShortcut shortcut, WorkspaceEntry launch, int score)
    {
        var workspace = ShortcutRepository.Clone(shortcut);
        var clonedLaunch = workspace.Launches.First(entry =>
            entry.Id.Equals(launch.Id, StringComparison.OrdinalIgnoreCase));
        return new WorkspaceTaskAction
        {
            Workspace = workspace,
            Launch = clonedLaunch,
            Score = score,
        };
    }

    private static int ComputeTaskActionScore(TerminalShortcut shortcut, WorkspaceEntry launch, IReadOnlyList<string> tokens)
    {
        var score = 0;
        var matchedLaunchSpecificField = false;

        foreach (var token in tokens)
        {
            var workspaceScore =
                ScoreToken(shortcut.Abbreviation, token, exact: 900, prefix: 650, contains: 200) +
                ScoreToken(shortcut.Name, token, exact: 700, prefix: 450, contains: 160) +
                ScoreToken(shortcut.Directory, token, exact: 100, prefix: 80, contains: 40);

            var profileLabel = TerminalCatalog.GetProfileLabel(new TerminalShortcut
            {
                Terminal = launch.Terminal,
                WtProfile = launch.WtProfile,
            });

            var launchScore =
                ScoreToken(launch.Label, token, exact: 1000, prefix: 750, contains: 300) +
                ScoreToken(launch.Command, token, exact: 850, prefix: 600, contains: 260) +
                ScoreToken(profileLabel, token, exact: 250, prefix: 175, contains: 90) +
                ScoreToken(launch.WtProfile, token, exact: 220, prefix: 160, contains: 80);

            if (workspaceScore + launchScore == 0)
            {
                return 0;
            }

            if (launchScore > 0)
            {
                matchedLaunchSpecificField = true;
            }

            score += workspaceScore + launchScore;
        }

        if (!matchedLaunchSpecificField)
        {
            return 0;
        }

        return score + Math.Max(0, 50 - launch.Order);
    }

    private static int ScoreToken(string? value, string token, int exact, int prefix, int contains)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        var trimmed = value.Trim();
        if (trimmed.Equals(token, StringComparison.OrdinalIgnoreCase))
        {
            return exact;
        }

        if (trimmed.StartsWith(token, StringComparison.OrdinalIgnoreCase))
        {
            return prefix;
        }

        return trimmed.Contains(token, StringComparison.OrdinalIgnoreCase) ? contains : 0;
    }

    private static string[] GetTaskSearchTokens(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        return query
            .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeTaskSearchToken)
            .Where(token => token.Length > 0 && !IsTaskSearchVerb(token))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string NormalizeTaskSearchToken(string token) =>
        token.Trim('\'', '"', '`', ',', '.', ':', ';', '(', ')', '[', ']', '{', '}');

    private static bool IsTaskSearchVerb(string token) =>
        token.Equals("run", StringComparison.OrdinalIgnoreCase) ||
        token.Equals("open", StringComparison.OrdinalIgnoreCase) ||
        token.Equals("start", StringComparison.OrdinalIgnoreCase) ||
        token.Equals("launch", StringComparison.OrdinalIgnoreCase) ||
        token.Equals("task", StringComparison.OrdinalIgnoreCase) ||
        token.Equals("tasks", StringComparison.OrdinalIgnoreCase) ||
        token.Equals("workspace", StringComparison.OrdinalIgnoreCase) ||
        token.Equals("workspaces", StringComparison.OrdinalIgnoreCase);

    private static int CompareAbbreviationMatch(
        TerminalShortcut left,
        TerminalShortcut right,
        string query,
        int queryStart,
        int queryLength)
    {
        var querySpan = query.AsSpan(queryStart, queryLength);
        var leftAbbreviation = left.Abbreviation.AsSpan();
        var rightAbbreviation = right.Abbreviation.AsSpan();
        var leftExact = leftAbbreviation.Equals(querySpan, StringComparison.OrdinalIgnoreCase);
        var rightExact = rightAbbreviation.Equals(querySpan, StringComparison.OrdinalIgnoreCase);
        if (leftExact != rightExact)
        {
            return leftExact ? -1 : 1;
        }

        var leftStarts = leftAbbreviation.StartsWith(querySpan, StringComparison.OrdinalIgnoreCase);
        var rightStarts = rightAbbreviation.StartsWith(querySpan, StringComparison.OrdinalIgnoreCase);
        if (leftStarts != rightStarts)
        {
            return leftStarts ? -1 : 1;
        }

        return string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsText(string? value, string query, int queryStart, int queryLength) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.AsSpan().Contains(query.AsSpan(queryStart, queryLength), StringComparison.OrdinalIgnoreCase);

    private static bool TryGetTrimmedQueryRange(string? query, out int start, out int length)
    {
        start = 0;
        length = 0;
        if (string.IsNullOrEmpty(query))
        {
            return false;
        }

        var end = query.Length - 1;
        while (start <= end && char.IsWhiteSpace(query[start]))
        {
            start++;
        }

        while (end >= start && char.IsWhiteSpace(query[end]))
        {
            end--;
        }

        if (start > end)
        {
            return false;
        }

        length = end - start + 1;
        return true;
    }
}
