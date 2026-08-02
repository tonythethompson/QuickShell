using QuickShell.Abstractions.Classification;
using QuickShell.Services;

namespace QuickShell.Classification.Suggestions;

internal sealed class AgentCliSuggestionProvider : ITaskSuggestionProvider
{
    public int Order => 100;

    public IReadOnlyList<CommandSuggestionPill> GetSuggestions(TaskSuggestionContext context)
    {
        // Bolt: Performance optimization - avoid LINQ iterator allocations for parsing existing launches
        var usedCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var launch in context.ExistingLaunches)
        {
            var command = launch.Command;
            if (!string.IsNullOrWhiteSpace(command))
            {
                usedCommands.Add(command!);
            }
        }

        var pills = new List<CommandSuggestionPill>();
        foreach (var def in AgentCliCatalog.Definitions)
        {
            var detected = def.PathNames.FirstOrDefault(AgentCliCatalog.IsCommandOnPath);
            if (detected is null && !AgentCliCatalog.HasProjectMarker(context.WorkspaceDirectory, def)) continue;
            var cmd = detected ?? def.Command;
            if (usedCommands.Contains(cmd) || usedCommands.Contains(def.Command)) continue;
            var score = detected is not null ? AgentCliCatalog.PathDetectedScore : AgentCliCatalog.MarkerFallbackScore;
            pills.Add(new CommandSuggestionPill(cmd, TaskTypeCatalog.Agent, "Agent", SuggestionPillPresentation.FormatDisplayTitle(cmd), SuggestionPillPresentation.FormatTooltip("Agent", cmd, productName: def.Title), score, detected is not null ? "agent-path" : "agent-marker"));
        }

        // Do not Take() here: a provider-level cap hid the rest behind silent replacement
        // instead of the form's "Show more suggestions" expand (DefaultVisibleSlots).
        return pills
            .OrderByDescending(p => p.Score)
            .ThenBy(p => p.DisplayTitle, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
