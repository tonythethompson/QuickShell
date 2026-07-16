using QuickShell.Abstractions.Classification;
using QuickShell.Services;

namespace QuickShell.Classification.Suggestions;

/// <summary>
/// Provider that emits project-aware setup tasks from file markers (package scripts,
/// csproj, Cargo, Makefile, etc.).
/// </summary>
internal sealed class WorkspaceSetupTaskSuggestionProvider : ITaskSuggestionProvider
{
    public int Priority => 100;

    public IReadOnlyList<WorkspaceSetupTask> GetSuggestions(
        string directory,
        ProjectClassification classification,
        IProjectAnalysisService projectAnalysis) =>
        WorkspaceSetupSuggestion.Build(directory, classification, projectAnalysis);
}
