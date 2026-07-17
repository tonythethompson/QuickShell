using System.Threading;
using QuickShell.Classification;
using QuickShell.Models;
using QuickShell.Services;

namespace QuickShell.Abstractions.Classification;

internal interface ITaskSuggestionProvider
{
    int Order { get; }
    IReadOnlyList<CommandSuggestionPill> GetSuggestions(TaskSuggestionContext context);
}

internal sealed record TaskSuggestionContext(
    string WorkspaceDirectory,
    ProjectLayout ProjectLayout,
    ProjectClassification ProjectClassification,
    IReadOnlyList<WorkspaceEntry> ExistingLaunches,
    IProjectAnalysisService ProjectAnalysis,
    CancellationToken CancellationToken);
