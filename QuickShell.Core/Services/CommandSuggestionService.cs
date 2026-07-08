namespace QuickShell.Services;

internal static class CommandSuggestionService
{
    public const int MaxPills = SuggestionPillPresentation.MaxSlots;
    public const int MaxPreDedupeCandidates = 32;
    public const int MaxNodeScripts = 40;
    public const int MaxDockerServices = 20;
    public const int MaxRootProjects = 10;

    public const string FieldLabel = "Suggested commands";

    public const string FieldHelp =
        "Click to add a launch row. Based on files in this folder.";

    public static bool HasSuggestions(string? directory, IEnumerable<string?> usedCommands)
    {
        return GetPills(directory, usedCommands, maxCount: 1).Count > 0;
    }

    public static IReadOnlyList<CommandSuggestionPill> GetPills(
        string? directory,
        IEnumerable<string?> usedCommands,
        int maxCount = MaxPills)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return [];
        }

        var pickContext = TaskTypePickContext.FromCommands(usedCommands);
        var classification = ProjectClassificationCache.Classify(directory);
        if (classification.Stacks == ProjectStack.None)
        {
            return [];
        }

        var suggestions = WorkspaceSetupSuggestion.Build(directory, classification);
        var context = new TaskTypeCandidateBuilder.SuggestionContext(directory, suggestions, classification);
        var merged = new Dictionary<string, (CommandSuggestionPill Pill, int Score)>(StringComparer.OrdinalIgnoreCase);
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

                var typeTitle = TaskTypeCatalog.GetTitle(definition.Id);
                var displayTitle = SuggestionPillPresentation.FormatDisplayTitle(typeTitle, candidate.Command);
                var pill = new CommandSuggestionPill(
                    candidate.Command,
                    definition.Id,
                    typeTitle,
                    displayTitle,
                    candidate.Command,
                    candidate.Score,
                    candidate.Source);

                if (!merged.TryGetValue(candidate.Command, out var existing)
                    || candidate.Score > existing.Score)
                {
                    merged[candidate.Command] = (pill, candidate.Score);
                }
            }
        }

        return merged.Values
            .Select(entry => entry.Pill)
            .OrderByDescending(pill => pill.Score)
            .ThenBy(pill => pill.DisplayTitle, StringComparer.OrdinalIgnoreCase)
            .Take(maxCount)
            .ToList();
    }

    public static CommandSuggestionPill? TryFindPill(
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
            string.Equals(pill.Command, command, StringComparison.OrdinalIgnoreCase)
            && (normalizedTaskType is null
                || string.Equals(pill.TaskType, normalizedTaskType, StringComparison.Ordinal)));
    }

    public static bool ApplyPill(
        List<LaunchRowDraft> rows,
        CommandSuggestionPill pill,
        string fallbackLaunchTarget) =>
        LaunchRowListEditor.ApplyPill(rows, pill, fallbackLaunchTarget);
}
