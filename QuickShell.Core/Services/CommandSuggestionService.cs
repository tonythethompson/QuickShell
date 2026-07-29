using QuickShell.Abstractions;
using QuickShell.Abstractions.Classification;
using QuickShell.Classification;
using QuickShell.Models;

namespace QuickShell.Services;

internal sealed class CommandSuggestionService : ICommandSuggestionService
{
    public const int MaxPills = SuggestionPillPresentation.MaxSlots;
    public const int MaxNodeScripts = 40;
    public const int MaxDockerServices = 20;
    internal const int ResultCacheTtlMs = 2500;

    private readonly IReadOnlyList<ITaskSuggestionProvider> _providers;
    private readonly object _cacheGate = new();
    private const int MaxCachedResults = 8;

    private readonly Dictionary<(string Directory, string UsedKey), SuggestionResultCache> _resultCache =
        new(CacheKeyComparer.Instance);

    private readonly Dictionary<string, ProjectContextCache> _projectCache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Directory compares case-insensitively (Windows paths); the used-command key is exact.</summary>
    private sealed class CacheKeyComparer : IEqualityComparer<(string Directory, string UsedKey)>
    {
        public static readonly CacheKeyComparer Instance = new();

        public bool Equals((string Directory, string UsedKey) left, (string Directory, string UsedKey) right) =>
            string.Equals(left.Directory, right.Directory, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.UsedKey, right.UsedKey, StringComparison.Ordinal);

        public int GetHashCode((string Directory, string UsedKey) value) =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(value.Directory),
                StringComparer.Ordinal.GetHashCode(value.UsedKey));
    }

    private static readonly Comparison<CommandSuggestionPill> PillRankComparison = static (l, r) =>
        r.Score.CompareTo(l.Score) is var s && s != 0
            ? s
            : string.Compare(l.DisplayTitle, r.DisplayTitle, StringComparison.OrdinalIgnoreCase) is var t && t != 0
                ? t
                : string.Compare(l.Command, r.Command, StringComparison.OrdinalIgnoreCase);

    public CommandSuggestionService(IEnumerable<ITaskSuggestionProvider> providers)
    {
        _providers = providers.OrderBy(p => p.Order).ToArray()
            ?? throw new ArgumentNullException(nameof(providers));
    }

    public bool HasSuggestions(
        string? directory,
        IEnumerable<string?> usedCommands,
        IProjectAnalysisService projectAnalysis) =>
        GetPills(directory, usedCommands, projectAnalysis, 1).Count > 0;

    public IReadOnlyList<CommandSuggestionPill> GetPills(
        string? directory,
        IEnumerable<string?> usedCommands,
        IProjectAnalysisService projectAnalysis,
        int maxCount = MaxPills)
    {
        if (maxCount <= 0 || !TryNormalizeDirectory(directory, out var dir))
        {
            return [];
        }

        maxCount = Math.Min(maxCount, MaxPills);
        var pick = TaskTypePickContext.FromCommands(usedCommands);
        var key = BuildUsedKey(pick.UsedCommands);
        if (TryGetCached(dir, key, out var cached))
        {
            return Slice(cached, maxCount);
        }

        var ranked = BuildRanked(dir, pick, projectAnalysis);
        StoreCache(dir, key, ranked);
        return Slice(ranked, maxCount);
    }

    public void ResetForTests()
    {
        lock (_cacheGate)
        {
            _resultCache.Clear();
            _projectCache.Clear();
        }
    }

    public CommandSuggestionPill? TryFindPill(
        IReadOnlyList<CommandSuggestionPill> pills,
        string? command,
        string? taskType)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        var normalizedTaskType = string.IsNullOrWhiteSpace(taskType)
            ? null
            : TaskTypeCatalog.Normalize(taskType);

        return pills.FirstOrDefault(pill =>
            string.Equals(pill.Command, command ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            && (normalizedTaskType is null
                || string.Equals(pill.TaskType, normalizedTaskType, StringComparison.Ordinal)));
    }

    public bool ApplyPill(List<LaunchRowDraft> rows, CommandSuggestionPill pill, string fallback) =>
        LaunchRowListEditor.ApplyPill(rows, pill, fallback);

    private List<CommandSuggestionPill> BuildRanked(
        string directory,
        TaskTypePickContext pick,
        IProjectAnalysisService analysis)
    {
        var (classification, layout) = GetProjectContext(directory, analysis);
        var existing = BuildEntries(pick.UsedCommands);
        var ctx = new TaskSuggestionContext(
            directory,
            layout,
            classification,
            existing,
            analysis,
            CancellationToken.None);
        var merged = new Dictionary<string, CommandSuggestionPill>(StringComparer.OrdinalIgnoreCase);
        foreach (var provider in _providers)
        {
            foreach (var pill in provider.GetSuggestions(ctx))
            {
                if (LaunchCommandSanity.IsUsableSuggestion(pill.Command))
                {
                    Consider(merged, pill);
                }
            }
        }

        return RankTop(merged.Values, MaxPills);
    }

    /// <summary>
    /// Classification and layout depend only on the directory, but the ranked result is keyed
    /// on the used-command set too. Without this, editing a command re-ran ~30 filesystem
    /// probes plus a full classify for a project that cannot have changed in between.
    /// </summary>
    private (ProjectClassification Classification, ProjectLayout Layout) GetProjectContext(
        string directory,
        IProjectAnalysisService analysis)
    {
        lock (_cacheGate)
        {
            if (_projectCache.TryGetValue(directory, out var cached)
                && cached.ExpiresAt > Environment.TickCount64)
            {
                return (cached.Classification, cached.Layout);
            }
        }

        var classification = analysis.Classify(directory);
        var layout = ProjectLayoutAnalyzer.Default.Analyze(directory);

        lock (_cacheGate)
        {
            if (_projectCache.Count >= MaxCachedResults)
            {
                PruneProjectCacheLocked();
            }

            _projectCache[directory] = new ProjectContextCache(
                classification,
                layout,
                Environment.TickCount64 + ResultCacheTtlMs);
        }

        return (classification, layout);
    }

    /// <summary>Drops expired entries, then the entry closest to expiry. Caller holds the gate.</summary>
    private void PruneProjectCacheLocked()
    {
        var now = Environment.TickCount64;
        List<string>? expired = null;
        foreach (var pair in _projectCache)
        {
            if (pair.Value.ExpiresAt <= now)
            {
                (expired ??= []).Add(pair.Key);
            }
        }

        if (expired is not null)
        {
            foreach (var key in expired)
            {
                _projectCache.Remove(key);
            }

            if (_projectCache.Count < MaxCachedResults)
            {
                return;
            }
        }

        _projectCache.Remove(_projectCache.OrderBy(pair => pair.Value.ExpiresAt).First().Key);
    }

    private static WorkspaceEntry[] BuildEntries(IReadOnlySet<string> used) =>
        used.Count == 0 ? [] : used.Select(c => new WorkspaceEntry { Command = c }).ToArray();

    private static void Consider(Dictionary<string, CommandSuggestionPill> merged, CommandSuggestionPill pill)
    {
        if (!merged.TryGetValue(pill.Command, out var existing) || pill.Score > existing.Score)
        {
            merged[pill.Command] = pill;
        }
    }

    private static List<CommandSuggestionPill> RankTop(IEnumerable<CommandSuggestionPill> candidates, int maxCount)
    {
        var top = new List<CommandSuggestionPill>(Math.Min(maxCount, 8));
        foreach (var pill in candidates)
        {
            InsertBounded(top, pill, maxCount);
        }

        return top;
    }

    private static void InsertBounded(List<CommandSuggestionPill> top, CommandSuggestionPill pill, int maxCount)
    {
        if (top.Count == maxCount)
        {
            if (PillRankComparison(pill, top[^1]) >= 0)
            {
                return;
            }

            top.RemoveAt(top.Count - 1);
        }

        var index = top.BinarySearch(pill, Comparer<CommandSuggestionPill>.Create(PillRankComparison));
        if (index < 0)
        {
            index = ~index;
        }

        top.Insert(index, pill);
    }

    private static IReadOnlyList<CommandSuggestionPill> Slice(IReadOnlyList<CommandSuggestionPill> pills, int maxCount)
    {
        if (pills.Count <= maxCount)
        {
            return pills;
        }

        return pills is List<CommandSuggestionPill> list
            ? list.GetRange(0, maxCount)
            : pills.Take(maxCount).ToArray();
    }

    private static bool TryNormalizeDirectory(string? directory, out string normalized)
    {
        normalized = (directory ?? string.Empty).Trim();
        return normalized.Length > 0 && Directory.Exists(normalized);
    }

    private static string BuildUsedKey(IReadOnlySet<string> used)
    {
        if (used.Count == 0)
        {
            return string.Empty;
        }

        var sorted = used.ToArray();
        Array.Sort(sorted, StringComparer.OrdinalIgnoreCase);
        return string.Join('\n', sorted);
    }

    private bool TryGetCached(string directory, string usedKey, out IReadOnlyList<CommandSuggestionPill> pills)
    {
        lock (_cacheGate)
        {
            if (_resultCache.TryGetValue((directory, usedKey), out var cache)
                && cache.ExpiresAt > Environment.TickCount64)
            {
                pills = cache.Pills;
                return true;
            }
        }

        pills = [];
        return false;
    }

    private void StoreCache(string directory, string usedKey, IReadOnlyList<CommandSuggestionPill> pills)
    {
        lock (_cacheGate)
        {
            // Several used-command sets are live at once while editing a workspace: the form
            // resolves a clicked pill against the pre-add set, then rebuilds its card against
            // the post-add set. A single-entry cache made those two evict each other, so every
            // pill click and every following card rebuild paid a full project rescan.
            if (_resultCache.Count >= MaxCachedResults)
            {
                PruneCacheLocked();
            }

            _resultCache[(directory, usedKey)] =
                new SuggestionResultCache(pills, Environment.TickCount64 + ResultCacheTtlMs);
        }
    }

    /// <summary>
    /// Drops expired entries, then the entry closest to expiry if that was not enough.
    /// Caller holds <see cref="_cacheGate"/>.
    /// </summary>
    private void PruneCacheLocked()
    {
        var now = Environment.TickCount64;
        List<(string Directory, string UsedKey)>? expired = null;
        foreach (var pair in _resultCache)
        {
            if (pair.Value.ExpiresAt <= now)
            {
                (expired ??= []).Add(pair.Key);
            }
        }

        if (expired is not null)
        {
            foreach (var key in expired)
            {
                _resultCache.Remove(key);
            }

            if (_resultCache.Count < MaxCachedResults)
            {
                return;
            }
        }

        var oldest = _resultCache.OrderBy(pair => pair.Value.ExpiresAt).First().Key;
        _resultCache.Remove(oldest);
    }

    private sealed record SuggestionResultCache(
        IReadOnlyList<CommandSuggestionPill> Pills,
        long ExpiresAt);

    private sealed record ProjectContextCache(
        ProjectClassification Classification,
        ProjectLayout Layout,
        long ExpiresAt);
}
