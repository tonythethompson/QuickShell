using QuickShell.Abstractions.Classification;
using QuickShell.Services;

namespace QuickShell.Classification.Suggestions;

/// <summary>
/// Provider that emits project-aware setup tasks from file markers (package scripts,
/// csproj, Cargo, Makefile, etc.).
/// </summary>
internal sealed class WorkspaceSetupTaskSuggestionProvider : ITaskSuggestionProvider
{
    public string Name => "Workspace setup";

    public int Priority => 100;

    public IReadOnlyList<WorkspaceSetupTask> GetSuggestions(
        string directory,
        ProjectClassification classification,
        IProjectAnalysisService projectAnalysis,
        CancellationToken cancellationToken = default) =>
        WorkspaceSetupSuggestion.Build(directory, classification, projectAnalysis);
}
