using QuickShell.Abstractions.Classification;
using QuickShell.Classification;
using QuickShell.Services;

namespace QuickShell.Classification.Suggestions;

internal sealed class WorkspaceSetupTaskSuggestionProvider : ITaskSuggestionProvider
{
    public int Order => 0;

    public IReadOnlyList<CommandSuggestionPill> GetSuggestions(TaskSuggestionContext context)
    {
        var tasks = new List<WorkspaceSetupTask>();
        tasks.AddRange(WorkspaceSetupSuggestion.Build(context.WorkspaceDirectory, context.ProjectClassification, context.ProjectAnalysis));

        if (context.ProjectClassification.Has(ProjectStack.Node))
            foreach (var (scriptName, _) in context.ProjectClassification.NodeScripts.Take(CommandSuggestionService.MaxNodeScripts))
                tasks.Add(new WorkspaceSetupTask(scriptName, context.ProjectAnalysis.FormatPackageScriptCommand(context.WorkspaceDirectory, scriptName)));

        if (context.ProjectClassification.Has(ProjectStack.Deno))
            foreach (var (taskName, _) in context.ProjectClassification.DenoTasks.Take(CommandSuggestionService.MaxNodeScripts))
                tasks.Add(new WorkspaceSetupTask(taskName, $"deno task {taskName}"));

        return TaskTypeCandidateBuilder.BuildPills(tasks, context);
    }
}
