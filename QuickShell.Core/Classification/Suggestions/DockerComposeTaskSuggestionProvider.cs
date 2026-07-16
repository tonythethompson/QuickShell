using QuickShell.Abstractions.Classification;
using QuickShell.Services;

namespace QuickShell.Classification.Suggestions;

/// <summary>
/// Provider that emits docker-compose service suggestions for a project directory.
/// </summary>
internal sealed class DockerComposeTaskSuggestionProvider : ITaskSuggestionProvider
{
    public int Priority => 50;

    public IReadOnlyList<WorkspaceSetupTask> GetSuggestions(
        string directory,
        ProjectClassification classification,
        IProjectAnalysisService projectAnalysis)
    {
        if (!classification.Has(ProjectStack.Docker))
        {
            return [];
        }

        return [.. DockerComposeDiscovery.BuildServiceSuggestions(directory)
            .Take(CommandSuggestionService.MaxDockerServices * 2)];
    }
}
