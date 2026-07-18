using QuickShell.Abstractions;
using QuickShell.Models;

namespace QuickShell.Services;

internal enum RootPaletteResultKind
{
    None,
    TaskActions,
    Workspaces,
    Discover,
    GitRepos,
}

internal readonly record struct RootPaletteSearchResult(
    RootPaletteResultKind Kind,
    IReadOnlyList<WorkspaceTaskAction>? TaskActions = null,
    IReadOnlyList<TerminalShortcut>? Workspaces = null,
    IReadOnlyList<GitRepoCandidate>? GitRepos = null,
    long Generation = 0);

/// <summary>
/// A cached, snapshot-backed index for the root palette (fallback) search path.
/// Precomputes trimmed search fields, saved-directory exclusion, and Git search roots
/// so each keystroke runs a single local pass and only calls the Git index when needed.
/// </summary>
internal sealed class RootPaletteSearchIndex
{
    private readonly IReadOnlyList<WorkspaceTaskView> _taskViews;
    private readonly IReadOnlyList<WorkspaceRootView> _rootViews;
    private readonly IReadOnlySet<string> _savedDirectories;
    private readonly IReadOnlyList<string> _gitSearchRoots;

    public RootPaletteSearchIndex(WorkspaceRepositorySnapshot snapshot)
    {
        Revision = snapshot.Version;

        var shortcuts = snapshot.Shortcuts;
        var taskViews = new List<WorkspaceTaskView>(shortcuts.Count);
        var rootViews = new List<WorkspaceRootView>(shortcuts.Count);

        var savedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var shortcut in shortcuts)
        {
            if (shortcut is null)
            {
                continue;
            }

            var directory = shortcut.Directory?.Trim();
            if (!string.IsNullOrWhiteSpace(directory))
            {
                savedDirectories.Add(directory);
            }

            rootViews.Add(new WorkspaceRootView(shortcut));
            taskViews.Add(new WorkspaceTaskView(shortcut));
        }

        _rootViews = rootViews;
        _taskViews = taskViews;
        _savedDirectories = savedDirectories;
        _gitSearchRoots = GitRepoSearchRoots.FromShortcuts(shortcuts).ToList();
    }

    public long Revision { get; }

    public RootPaletteSearchResult Search(
        string query,
        IGitRepoIndex gitRepos,
        long generation = 0)
    {
        if (ShouldSuppress(query))
        {
            return new(RootPaletteResultKind.None, Generation: generation);
        }

        var taskActions = SearchTaskActions(query);
        if (taskActions.Count > 0)
        {
            return new(RootPaletteResultKind.TaskActions, TaskActions: taskActions, Generation: generation);
        }

        var workspaces = SearchForRootPalette(query);
        if (workspaces.Count > 0)
        {
            return new(RootPaletteResultKind.Workspaces, Workspaces: workspaces, Generation: generation);
        }

        if (GitRepoIndex.IsDiscoverQuery(query))
        {
            return new(RootPaletteResultKind.Discover, Generation: generation);
        }

        var trimmed = query?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length < 2)
        {
            return new(RootPaletteResultKind.None, Generation: generation);
        }

        var gitRepoResults = gitRepos.Search(trimmed, _gitSearchRoots, _savedDirectories, maxResults: 8);
        if (gitRepoResults.Count > 0)
        {
            return new(RootPaletteResultKind.GitRepos, GitRepos: gitRepoResults, Generation: generation);
        }

        return new(RootPaletteResultKind.None, Generation: generation);
    }

    public IReadOnlyList<TerminalShortcut> SearchForRootPalette(string query)
    {
        if (!TryGetTrimmedQueryRange(query, out var queryStart, out var queryLength))
        {
            return [];
        }

        List<WorkspaceRootView>? abbreviationMatches = null;
        List<WorkspaceRootView>? matches = null;
        foreach (var view in _rootViews)
        {
            if (ContainsText(view.AbbreviationTrimmed, query, queryStart, queryLength))
            {
                abbreviationMatches ??= [];
                abbreviationMatches.Add(view);
                continue;
            }

            if (!MatchesForRootPalette(view, query, queryStart, queryLength))
            {
                continue;
            }

            matches ??= [];
            matches.Add(view);
        }

        if (abbreviationMatches is not null)
        {
            abbreviationMatches.Sort((left, right) =>
                CompareAbbreviationMatch(left, right, query, queryStart, queryLength));
            return abbreviationMatches.Select(view => view.Shortcut).ToArray();
        }

        return matches is null ? [] : matches.Select(view => view.Shortcut).ToArray();
    }

    public IReadOnlyList<WorkspaceTaskAction> SearchTaskActions(string query)
    {
        var tokens = GetTaskSearchTokens(query);
        if (tokens.Length == 0)
        {
            return [];
        }

        List<WorkspaceTaskAction>? matches = null;
        foreach (var workspace in _taskViews)
        {
            if (workspace.Launches.Count == 0)
            {
                continue;
            }

            bool? requiresRepair = null;
            foreach (var launch in workspace.Launches)
            {
                if (!launch.IsEnabled)
                {
                    continue;
                }

                var score = ComputeTaskActionScore(workspace, launch, tokens);
                if (score <= 0)
                {
                    continue;
                }

                // Root-palette search runs per keystroke; directory reachability belongs to launch health.
                requiresRepair ??= ShortcutHealth.WouldNeedRepair(
                    workspace.Shortcut,
                    requireDirectoryExists: false);
                if (requiresRepair.Value)
                {
                    break;
                }

                matches ??= [];
                matches.Add(new WorkspaceTaskAction
                {
                    Workspace = workspace.Shortcut,
                    Launch = launch.Launch,
                    Score = score,
                });
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

    private static bool ShouldSuppress(string? query) =>
        !string.IsNullOrWhiteSpace(query) &&
        query.Contains("quick shell", StringComparison.OrdinalIgnoreCase);

    private static bool MatchesForRootPalette(WorkspaceRootView view, string query, int queryStart, int queryLength) =>
        ContainsText(view.NameTrimmed, query, queryStart, queryLength) ||
        ContainsText(view.DirectoryTrimmed, query, queryStart, queryLength) ||
        ContainsText(view.WtProfileTrimmed, query, queryStart, queryLength);

    private static int ComputeTaskActionScore(WorkspaceTaskView workspace, LaunchTaskView launch, IReadOnlyList<string> tokens)
    {
        var score = 0;
        var matchedLaunchSpecificField = false;

        foreach (var token in tokens)
        {
            var workspaceScore =
                ScoreToken(workspace.AbbreviationTrimmed, token, exact: 900, prefix: 650, contains: 200) +
                ScoreToken(workspace.NameTrimmed, token, exact: 700, prefix: 450, contains: 160) +
                ScoreToken(workspace.DirectoryTrimmed, token, exact: 100, prefix: 80, contains: 40);

            var launchScore =
                ScoreToken(launch.LabelTrimmed, token, exact: 1000, prefix: 750, contains: 300) +
                ScoreToken(launch.CommandTrimmed, token, exact: 850, prefix: 600, contains: 260) +
                ScoreToken(launch.ProfileLabelTrimmed, token, exact: 250, prefix: 175, contains: 90) +
                ScoreToken(launch.WtProfileTrimmed, token, exact: 220, prefix: 160, contains: 80);

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

        if (value.Equals(token, StringComparison.OrdinalIgnoreCase))
        {
            return exact;
        }

        if (value.StartsWith(token, StringComparison.OrdinalIgnoreCase))
        {
            return prefix;
        }

        return value.Contains(token, StringComparison.OrdinalIgnoreCase) ? contains : 0;
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
        WorkspaceRootView left,
        WorkspaceRootView right,
        string query,
        int queryStart,
        int queryLength)
    {
        var querySpan = query.AsSpan(queryStart, queryLength);
        var leftAbbreviation = left.AbbreviationTrimmed.AsSpan();
        var rightAbbreviation = right.AbbreviationTrimmed.AsSpan();
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

        return string.Compare(left.Shortcut.Name, right.Shortcut.Name, StringComparison.OrdinalIgnoreCase);
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

    private readonly record struct WorkspaceRootView(TerminalShortcut Shortcut)
    {
        public string NameTrimmed { get; } = Shortcut.Name?.Trim() ?? string.Empty;

        public string? DirectoryTrimmed { get; } = Shortcut.Directory?.Trim();

        public string? WtProfileTrimmed { get; } = Shortcut.WtProfile?.Trim();

        public string? AbbreviationTrimmed { get; } = Shortcut.Abbreviation?.Trim();
    }

    private sealed class WorkspaceTaskView
    {
        public WorkspaceTaskView(TerminalShortcut shortcut)
        {
            Shortcut = shortcut;
            AbbreviationTrimmed = shortcut.Abbreviation?.Trim();
            NameTrimmed = shortcut.Name?.Trim() ?? string.Empty;
            DirectoryTrimmed = shortcut.Directory?.Trim();

            var launches = shortcut.Launches ?? [];
            var launchViews = new List<LaunchTaskView>(launches.Count);
            foreach (var launch in launches)
            {
                launchViews.Add(new LaunchTaskView(launch));
            }

            Launches = launchViews;
        }

        public TerminalShortcut Shortcut { get; }

        public string? AbbreviationTrimmed { get; }

        public string NameTrimmed { get; }

        public string? DirectoryTrimmed { get; }

        public List<LaunchTaskView> Launches { get; }
    }

    private sealed class LaunchTaskView
    {
        public LaunchTaskView(WorkspaceEntry launch)
        {
            Launch = launch;
            LabelTrimmed = launch.Label?.Trim();
            CommandTrimmed = launch.Command?.Trim();
            WtProfileTrimmed = launch.WtProfile?.Trim();
            IsEnabled = launch.IsEnabled;
            Order = launch.Order;

            var profileLabel = TerminalCatalog.GetProfileLabel(new TerminalShortcut
            {
                Terminal = launch.Terminal,
                WtProfile = launch.WtProfile,
            });
            ProfileLabelTrimmed = profileLabel?.Trim();
        }

        public WorkspaceEntry Launch { get; }

        public string? LabelTrimmed { get; }

        public string? CommandTrimmed { get; }

        public string? WtProfileTrimmed { get; }

        public string? ProfileLabelTrimmed { get; }

        public bool IsEnabled { get; }

        public int Order { get; }
    }
}
