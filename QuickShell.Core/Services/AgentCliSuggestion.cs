namespace QuickShell.Services;

/// <summary>
/// Builds AI agent CLI suggestion pills from PATH installs and project marker files.
/// </summary>
internal static class AgentCliSuggestion
{
    public static IReadOnlyList<CommandSuggestionPill> BuildPills(
        string directory,
        TaskTypePickContext pickContext)
    {
        var pills = new List<CommandSuggestionPill>();

        foreach (var definition in AgentCliCatalog.Definitions)
        {
            var detectedCommand = definition.PathNames.FirstOrDefault(AgentCliCatalog.IsCommandOnPath);
            var onPath = detectedCommand is not null;
            var hasMarker = AgentCliCatalog.HasProjectMarker(directory, definition);
            if (!onPath && !hasMarker)
            {
                continue;
            }

            var command = detectedCommand ?? definition.Command;
            if (pickContext.UsedCommands.Contains(command)
                || pickContext.UsedCommands.Contains(definition.Command))
            {
                continue;
            }

            var score = onPath ? AgentCliCatalog.PathDetectedScore : AgentCliCatalog.MarkerFallbackScore;
            var source = onPath ? "agent-path" : "agent-marker";
            var reason = onPath
                ? $"{definition.Title} detected on PATH"
                : $"{definition.Title} project marker found";
            var displayTitle = SuggestionPillPresentation.FormatDisplayTitle(command);
            var tooltip = SuggestionPillPresentation.FormatTooltip(
                "Agent",
                command,
                productName: definition.Title,
                detail: $"{reason}. Adds `{command}` as a launch command.");

            pills.Add(new CommandSuggestionPill(
                command,
                TaskTypeCatalog.Agent,
                "Agent",
                displayTitle,
                tooltip,
                score,
                source));
        }

        return pills
            .OrderByDescending(pill => pill.Score)
            .ThenBy(pill => pill.DisplayTitle, StringComparer.OrdinalIgnoreCase)
            .Take(AgentCliCatalog.MaxDefaultAgentPills)
            .ToList();
    }
}
