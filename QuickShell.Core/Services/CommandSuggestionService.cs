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

    private static readonly Comparison<CommandSuggestionPill> PillRankComparison = static (l, r) => r.Score.CompareTo(l.Score) is var s && s != 0 ? s : string.Compare(l.DisplayTitle, r.DisplayTitle, StringComparison.OrdinalIgnoreCase) is var t && t != 0 ? t : string.Compare(l.Command, r.Command, StringComparison.OrdinalIgnoreCase);

    public CommandSuggestionService(IEnumerable<ITaskSuggestionProvider> providers)
    {
        _providers = providers.OrderBy(p => p.Order).ToArray() ?? throw new ArgumentNullException(nameof(providers));
    }

    public bool HasSuggestions(string? directory, IEnumerable<string?> usedCommands, IProjectAnalysisService projectAnalysis) => GetPills(directory, usedCommands, projectAnalysis, 1).Count > 0;

    public IReadOnlyList<CommandSuggestionPill> GetPills(string? directory, IEnumerable<string?> usedCommands, IProjectAnalysisService projectAnalysis, int maxCount = MaxPills)
    {
        if (maxCount <= 0 || !TryNormalizeDirectory(directory, out var dir)) return [];
        maxCount = Math.Min(maxCount, MaxPills);
        var pick = TaskTypePickContext.FromCommands(usedCommands);
        var key = BuildUsedKey(pick.UsedCommands);
        if (TryGetCached(dir, key, out var c)) return Slice(c, maxCount);
        var ranked = BuildRanked(dir, pick, projectAnalysis);
        StoreCache(dir, key, ranked);
        return Slice(ranked, maxCount);
    }

    public void ResetForTests() { lock (_cacheGate) _resultCache = null; }

    public CommandSuggestionPill? TryFindPill(IReadOnlyList<CommandSuggestionPill> pills, string? command, string? taskType)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        var nt = string.IsNullOrWhiteSpace(taskType) ? null : TaskTypeCatalog.Normalize(taskType);
        return pills.FirstOrDefault(p => string.Equals(p.Command, command, StringComparison.OrdinalIgnoreCase) && (nt is null || string.Equals(p.TaskType, nt, StringComparison.Ordinal)));
    }

    public bool ApplyPill(List<LaunchRowDraft> rows, CommandSuggestionPill pill, string fallback) => LaunchRowListEditor.ApplyPill(rows, pill, fallback);

    private List<CommandSuggestionPill> BuildRanked(string directory, TaskTypePickContext pick, IProjectAnalysisService analysis)
    {
        var classification = analysis.Classify(directory);
        var existing = BuildEntries(pick.UsedCommands);
        var ctx = new TaskSuggestionContext(directory, ProjectLayoutAnalyzer.Default.Analyze(directory), classification, existing, analysis, CancellationToken.None);
        var merged = new Dictionary<string, CommandSuggestionPill>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in _providers)
            foreach (var pill in p.GetSuggestions(ctx))
                if (LaunchCommandSanity.IsUsableSuggestion(pill.Command))
                    Consider(merged, pill);
        return RankTop(merged.Values, MaxPills);
    }

    private static IReadOnlyList<WorkspaceEntry> BuildEntries(IReadOnlySet<string> used) => used.Count == 0 ? [] : used.Select(c => new WorkspaceEntry { Command = c }).ToArray();
    private static void Consider(Dictionary<string, CommandSuggestionPill> m, CommandSuggestionPill p) { if (!m.TryGetValue(p.Command, out var e) || p.Score > e.Score) m[p.Command] = p; }
    private static List<CommandSuggestionPill> RankTop(IEnumerable<CommandSuggestionPill> candidates, int max) { var top = new List<CommandSuggestionPill>(Math.Min(max, 8)); foreach (var p in candidates) InsertBounded(top, p, max); return top; }
    private static void InsertBounded(List<CommandSuggestionPill> top, CommandSuggestionPill pill, int max) { if (top.Count == max) { if (PillRankComparison(pill, top[^1]) >= 0) return; top.RemoveAt(top.Count - 1); } top.Insert(~top.BinarySearch(pill, Comparer<CommandSuggestionPill>.Create(PillRankComparison)), pill); }
    private static IReadOnlyList<CommandSuggestionPill> Slice(IReadOnlyList<CommandSuggestionPill> pills, int max) => pills.Count <= max ? pills : pills is List<CommandSuggestionPill> l ? l.GetRange(0, max) : pills.Take(max).ToArray();
    private static bool TryNormalizeDirectory(string? d, out string n) { n = (d ?? "").Trim(); return n.Length > 0 && Directory.Exists(n); }
    private static string BuildUsedKey(IReadOnlySet<string> used) { if (used.Count == 0) return ""; var a = used.ToArray(); Array.Sort(a, StringComparer.OrdinalIgnoreCase); return string.Join('\n', a); }
    private bool TryGetCached(string dir, string key, out IReadOnlyList<CommandSuggestionPill> pills) { lock (_cacheGate) { var c = _resultCache; if (c is not null && c.ExpiresAt > Environment.TickCount64 && string.Equals(c.Directory, dir, StringComparison.OrdinalIgnoreCase) && string.Equals(c.UsedKey, key, StringComparison.Ordinal)) { pills = c.Pills; return true; } } pills = []; return false; }
    private void StoreCache(string dir, string key, IReadOnlyList<CommandSuggestionPill> pills) { lock (_cacheGate) _resultCache = new(dir, key, pills, Environment.TickCount64 + ResultCacheTtlMs); }
    private sealed record SuggestionResultCache(string Directory, string UsedKey, IReadOnlyList<CommandSuggestionPill> Pills, long ExpiresAt);
}
