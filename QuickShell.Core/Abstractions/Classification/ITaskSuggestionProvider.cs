using QuickShell.Services;

namespace QuickShell.Abstractions.Classification;

/// <summary>
/// Pluggable source of workspace task suggestions ("pills") for a project directory.
/// Multiple providers are registered in DI and aggregated by <see cref="IProjectAnalysisService"/>.
/// </summary>
internal interface ITaskSuggestionProvider
{
    /// <summary>
    /// Higher priority providers are evaluated first. Providers with duplicate suggestions
    /// are de-duplicated by command during aggregation.
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Returns task suggestions for the given directory and classification.
    /// </summary>
    /// <param name="directory">The workspace directory to inspect.</param>
    /// <param name="classification">The already-computed project classification for the directory.</param>
    /// <param name="projectAnalysis">The active project analysis service, used for formatting helper commands.</param>
    IReadOnlyList<WorkspaceSetupTask> GetSuggestions(
        string directory,
        ProjectClassification classification,
        IProjectAnalysisService projectAnalysis);
}
