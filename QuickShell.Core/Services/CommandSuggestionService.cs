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
    private SuggestionResultCache? _resultCache;

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
            _resultCache = null;
        }
    }

    public CommandSuggestionPill? TryFindPill(
        IReadOnlyList<CommandSuggestionPill> pills,
        string? command,
        string? taskType)
    {
        // Blank command is a legitimate pill value (the "Open to Directory" pill has no
        // command by definition). Only taskType is normalized/optional; command matches
        // as-is including blank-to-blank.
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
        var classification = analysis.Classify(directory);
        var existing = BuildEntries(pick.UsedCommands);
        var ctx = new TaskSuggestionContext(
            directory,
            ProjectLayoutAnalyzer.Default.Analyze(directory),
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

    private static IReadOnlyList<WorkspaceEntry> BuildEntries(IReadOnlySet<string> used) =>
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
            var cache = _resultCache;
            if (cache is not null
                && cache.ExpiresAt > Environment.TickCount64
                && string.Equals(cache.Directory, directory, StringComparison.OrdinalIgnoreCase)
                && string.Equals(cache.UsedKey, usedKey, StringComparison.Ordinal))
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
            _resultCache = new(directory, usedKey, pills, Environment.TickCount64 + ResultCacheTtlMs);
        }
    }

    private sealed record SuggestionResultCache(
        string Directory,
        string UsedKey,
        IReadOnlyList<CommandSuggestionPill> Pills,
        long ExpiresAt);
}
