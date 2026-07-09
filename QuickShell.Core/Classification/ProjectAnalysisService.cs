using QuickShell.Abstractions.Classification;
using QuickShell.Services;

namespace QuickShell.Classification;

internal sealed class ProjectAnalysisService : IProjectAnalysisService
{
    private readonly IReadOnlyList<IProjectClassifier> _classifiers;
    private readonly IProjectLayoutAnalyzer _layoutAnalyzer;

    public ProjectAnalysisService(
        IEnumerable<IProjectClassifier> classifiers,
        IProjectLayoutAnalyzer layoutAnalyzer)
    {
        ArgumentNullException.ThrowIfNull(classifiers);
        ArgumentNullException.ThrowIfNull(layoutAnalyzer);

        _classifiers = classifiers.OrderByDescending(classifier => classifier.Priority).ToArray();
        _layoutAnalyzer = layoutAnalyzer;
    }

    public ProjectClassification Classify(string directory) =>
        ProjectClassificationPipeline.Classify(directory, _classifiers, _layoutAnalyzer);

    public bool HasAvailableTaskTypes(string? directory) =>
        TaskTypeCommandSuggestion.HasAvailableTypes(directory);

    public IReadOnlyList<string> GetAvailableTaskTypes(string? directory, TaskTypePickContext pickContext) =>
        TaskTypeCommandSuggestion.GetAvailableTaskTypes(directory, pickContext);

    public bool IsTaskTypeAvailable(string? directory, string? taskType, TaskTypePickContext pickContext) =>
        TaskTypeCommandSuggestion.IsAvailable(directory, taskType, pickContext);

    public string? TrySuggestTaskCommand(string? directory, string? taskType, TaskTypePickContext pickContext) =>
        TaskTypeCommandSuggestion.TrySuggest(directory, taskType, pickContext);

    public string GetTaskTypeChoiceTooltip(string? directory, string? taskType, TaskTypePickContext pickContext) =>
        TaskTypeCommandSuggestion.GetChoiceTooltip(directory, taskType, pickContext);
}
