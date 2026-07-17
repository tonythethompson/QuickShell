using QuickShell.Abstractions;
using QuickShell.Abstractions.Classification;

namespace QuickShell.Services;

internal static class CommandSuggestionService
{
    public const int MaxPills = SuggestionPillPresentation.MaxSlots;
    public const int MaxPreDedupeCandidates = 32;
    public const int MaxNodeScripts = 40;
    public const int MaxDockerServices = 20;
    public const int MaxRootProjects = 10;

    /// <summary>Short TTL so form prewarm + first paint reuse the same directory scan.</summary>
    internal const int ResultCacheTtlMs = 2500;

    public const string FieldLabel = "Suggested commands";

    public const string FieldHelp = "Click a pill to add.";

    private static readonly object CacheGate = new();
    private static SuggestionResultCache? _resultCache;

    private static readonly Comparison<CommandSuggestionPill> PillRankComparison = static (left, right) =>
    {
        var byScore = right.Score.CompareTo(left.Score);
        if (byScore != 0)
        {
            return byScore;
        }

        var byTitle = string.Compare(left.DisplayTitle, right.DisplayTitle, StringComparison.OrdinalIgnoreCase);
        return byTitle != 0
            ? byTitle
            : string.Compare(left.Command, right.Command, StringComparison.OrdinalIgnoreCase);
    };

    /// <summary>
    /// True when at least one usable pill exists. Exits on the first match — does not
    /// rank or materialize the full candidate set (unlike <see cref="GetPills"/> with maxCount: 1).
    /// </summary>
    public static bool HasSuggestions(
        string? directory,
        IEnumerable<string?> usedCommands,
        IProjectAnalysisService projectAnalysis,
        IProjectClassificationCache classificationCache)
    {
        ArgumentNullException.ThrowIfNull(classificationCache);

        if (!TryNormalizeDirectory(directory, out var normalizedDir))
        {
            return false;
        }

        var pickContext = TaskTypePickContext.FromCommands(usedCommands);
        var usedKey = BuildUsedKey(pickContext.UsedCommands);

        if (TryGetCached(normalizedDir, usedKey, out var cached))
        {
            return cached.Count > 0;
        }

        if (AnyUsableSuggestion(normalizedDir, pickContext, projectAnalysis, classificationCache))
        {
            return true;
        }

        // Confirmed empty — cache so a follow-up GetPills does not rescan.
        StoreCache(normalizedDir, usedKey, []);
        return false;
    }

    public static IReadOnlyList<CommandSuggestionPill> GetPills(
        string? directory,
        IEnumerable<string?> usedCommands,
        IProjectAnalysisService projectAnalysis,
        IProjectClassificationCache classificationCache,
        int maxCount = MaxPills)
    {
        ArgumentNullException.ThrowIfNull(classificationCache);

        if (maxCount <= 0)
        {
            return [];
        }

        maxCount = Math.Min(maxCount, MaxPills);

        if (!TryNormalizeDirectory(directory, out var normalizedDir))
        {
            return [];
        }

        var pickContext = TaskTypePickContext.FromCommands(usedCommands);
        var usedKey = BuildUsedKey(pickContext.UsedCommands);

        if (TryGetCached(normalizedDir, usedKey, out var cached))
        {
            return Slice(cached, maxCount);
        }

        var ranked = BuildRankedPills(normalizedDir, pickContext, projectAnalysis, classificationCache);
        StoreCache(normalizedDir, usedKey, ranked);
        return Slice(ranked, maxCount);
    }

    /// <summary>Test/diagnostic seam: drop the short-lived result cache.</summary>
    internal static void ClearResultCache()
    {
        lock (CacheGate)
        {
            _resultCache = null;
        }
    }

    public static CommandSuggestionPill? TryFindPill(
        IReadOnlyList<CommandSuggestionPill> pills,
        string? command,
        string? taskType)
    {
        // Blank command is a legitimate pill value (the "Open directory only" pill has no
        // command by definition) — only taskType is normalized/optional, command matches
        // as-is including blank-to-blank.
        var normalizedTaskType = string.IsNullOrWhiteSpace(taskType)
            ? null
            : TaskTypeCatalog.Normalize(taskType);

        return pills.FirstOrDefault(pill =>
            string.Equals(pill.Command, command ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            && (normalizedTaskType is null
                || string.Equals(pill.TaskType, normalizedTaskType, StringComparison.Ordinal)));
    }

    public static bool ApplyPill(
        List<LaunchRowDraft> rows,
        CommandSuggestionPill pill,
        string fallbackLaunchTarget) =>
        LaunchRowListEditor.ApplyPill(rows, pill, fallbackLaunchTarget);

    private static bool TryNormalizeDirectory(string? directory, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return false;
        }

        normalized = directory.Trim();
        return true;
    }

    private static bool AnyUsableSuggestion(
        string directory,
        TaskTypePickContext pickContext,
        IProjectAnalysisService projectAnalysis,
        IProjectClassificationCache classificationCache)
    {
        foreach (var agentPill in AgentCliSuggestion.BuildPills(directory, pickContext))
        {
            if (LaunchCommandSanity.IsUsableSuggestion(agentPill.Command))
            {
                return true;
            }
        }

        return AnyUsableTaskTypeSuggestion(directory, pickContext, projectAnalysis, classificationCache);
    }

    private static bool AnyUsableTaskTypeSuggestion(
        string directory,
        TaskTypePickContext pickContext,
        IProjectAnalysisService projectAnalysis,
        IProjectClassificationCache classificationCache)
    {
        var classification = classificationCache.Classify(directory);
        if (classification.Stacks == ProjectStack.None)
        {
            return false;
        }

        var suggestions = WorkspaceSetupSuggestion.Build(directory, classification, projectAnalysis);
        var context = new TaskTypeCandidateBuilder.SuggestionContext(directory, suggestions, classification, projectAnalysis);
        var preDedupeCount = 0;

        foreach (var definition in TaskTypeCatalog.GetChoices())
        {
            if (preDedupeCount >= MaxPreDedupeCandidates)
            {
                break;
            }

            foreach (var candidate in TaskTypeCandidateBuilder.Build(definition.Id, context, pickContext))
            {
                preDedupeCount++;
                if (preDedupeCount > MaxPreDedupeCandidates)
                {
                    break;
                }

                if (LaunchCommandSanity.IsUsableSuggestion(candidate.Command))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static List<CommandSuggestionPill> BuildRankedPills(
        string directory,
        TaskTypePickContext pickContext,
        IProjectAnalysisService projectAnalysis,
        IProjectClassificationCache classificationCache)
    {
        // Dedup by command (keep highest score). Sanity-filter once on insert.
        var merged = new Dictionary<string, CommandSuggestionPill>(StringComparer.OrdinalIgnoreCase);

        foreach (var agentPill in AgentCliSuggestion.BuildPills(directory, pickContext))
        {
            if (!LaunchCommandSanity.IsUsableSuggestion(agentPill.Command))
            {
                continue;
            }

            Consider(merged, agentPill);
        }

        var classification = classificationCache.Classify(directory);
        if (classification.Stacks != ProjectStack.None)
        {
            var suggestions = WorkspaceSetupSuggestion.Build(directory, classification, projectAnalysis);
            var context = new TaskTypeCandidateBuilder.SuggestionContext(directory, suggestions, classification, projectAnalysis);
            var preDedupeCount = 0;

            foreach (var definition in TaskTypeCatalog.GetChoices())
            {
                if (preDedupeCount >= MaxPreDedupeCandidates)
                {
                    break;
                }

                foreach (var candidate in TaskTypeCandidateBuilder.Build(definition.Id, context, pickContext))
                {
                    preDedupeCount++;
                    if (preDedupeCount > MaxPreDedupeCandidates)
                    {
                        break;
                    }

                    if (!LaunchCommandSanity.IsUsableSuggestion(candidate.Command))
                    {
                        continue;
                    }

                    var typeTitle = TaskTypeCatalog.GetTitle(definition.Id);
                    var displayTitle = SuggestionPillPresentation.FormatDisplayTitle(candidate.Command);
                    var tooltip = SuggestionPillPresentation.FormatTooltip(typeTitle, candidate.Command);
                    var pill = new CommandSuggestionPill(
                        candidate.Command,
                        definition.Id,
                        typeTitle,
                        displayTitle,
                        tooltip,
                        candidate.Score,
                        candidate.Source);

                    Consider(merged, pill);
                }
            }
        }

        return RankTop(merged.Values, MaxPills);
    }

    private static void Consider(Dictionary<string, CommandSuggestionPill> merged, CommandSuggestionPill pill)
    {
        if (!merged.TryGetValue(pill.Command, out var existing) || pill.Score > existing.Score)
        {
            merged[pill.Command] = pill;
        }
    }

    /// <summary>
    /// Bounded top-N by score (then display title). Avoids LINQ OrderBy/Where/Take/ToList chains.
    /// </summary>
    private static List<CommandSuggestionPill> RankTop(IEnumerable<CommandSuggestionPill> candidates, int maxCount)
    {
        if (maxCount <= 0)
        {
            return [];
        }

        // Small k path: insertion into a sorted buffer of size maxCount (k is MaxPills ≤ 16).
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
            // Worse than or equal to the current floor — skip.
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

        if (pills is List<CommandSuggestionPill> list)
        {
            return list.GetRange(0, maxCount);
        }

        var slice = new CommandSuggestionPill[maxCount];
        for (var i = 0; i < maxCount; i++)
        {
            slice[i] = pills[i];
        }

        return slice;
    }

    private static string BuildUsedKey(IReadOnlySet<string> usedCommands)
    {
        if (usedCommands.Count == 0)
        {
            return string.Empty;
        }

        var ordered = new string[usedCommands.Count];
        var i = 0;
        foreach (var command in usedCommands)
        {
            ordered[i++] = command;
        }

        Array.Sort(ordered, StringComparer.OrdinalIgnoreCase);
        return string.Join('\n', ordered);
    }

    private static bool TryGetCached(
        string directory,
        string usedKey,
        out IReadOnlyList<CommandSuggestionPill> pills)
    {
        lock (CacheGate)
        {
            var cache = _resultCache;
            if (cache is null
                || cache.ExpiresAtTickMs < Environment.TickCount64
                || !string.Equals(cache.Directory, directory, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(cache.UsedKey, usedKey, StringComparison.Ordinal))
            {
                pills = [];
                return false;
            }

            pills = cache.Pills;
            return true;
        }
    }

    private static void StoreCache(
        string directory,
        string usedKey,
        IReadOnlyList<CommandSuggestionPill> pills)
    {
        lock (CacheGate)
        {
            _resultCache = new SuggestionResultCache(
                directory,
                usedKey,
                pills,
                Environment.TickCount64 + ResultCacheTtlMs);
        }
    }

    private sealed record SuggestionResultCache(
        string Directory,
        string UsedKey,
        IReadOnlyList<CommandSuggestionPill> Pills,
        long ExpiresAtTickMs);
}
