using QuickShell.Abstractions.Classification;
using QuickShell.Services;

namespace QuickShell.Classification.Suggestions;

internal sealed class DockerComposeTaskSuggestionProvider : ITaskSuggestionProvider
{
    public int Order => 50;

    public IReadOnlyList<CommandSuggestionPill> GetSuggestions(TaskSuggestionContext context)
    {
        if (!context.ProjectClassification.Has(ProjectStack.Docker)) return [];
        var tasks = DockerComposeDiscovery.BuildServiceSuggestions(context.WorkspaceDirectory).Take(CommandSuggestionService.MaxDockerServices * 2);
        return TaskTypeCandidateBuilder.BuildPills(tasks, context);
    }
}
