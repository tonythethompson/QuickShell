using QuickShell.Abstractions.Classification;
using QuickShell.Services;

namespace QuickShell.Classification;

internal sealed class ProjectAnalysisService : IProjectAnalysisService
{
    private readonly IReadOnlyList<IProjectClassifier> _classifiers;
    private readonly IProjectLayoutAnalyzer _layoutAnalyzer;
    private readonly ICompanionAppDetector _companionAppDetector;
    private readonly IDevServerDetector _devServerDetector;

    public ProjectAnalysisService(
        IEnumerable<IProjectClassifier> classifiers,
        IProjectLayoutAnalyzer layoutAnalyzer,
        ICompanionAppDetector companionAppDetector,
        IDevServerDetector devServerDetector)
    {
        ArgumentNullException.ThrowIfNull(classifiers);
        ArgumentNullException.ThrowIfNull(layoutAnalyzer);
        ArgumentNullException.ThrowIfNull(companionAppDetector);
        ArgumentNullException.ThrowIfNull(devServerDetector);

        _classifiers = classifiers.OrderByDescending(classifier => classifier.Priority).ToArray();
        _layoutAnalyzer = layoutAnalyzer;
        _companionAppDetector = companionAppDetector;
        _devServerDetector = devServerDetector;
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

    public CompanionAppSuggestion? TrySuggestCompanionApp(string directory) =>
        _companionAppDetector.TrySuggest(directory);

    public string? TryDetectDevServerUrl(string directory) =>
        _devServerDetector.TryDetectDevServerUrl(directory);

    public string? TryInferTaskType(string directory) =>
        _devServerDetector.TryInferTaskType(directory);

    public string? TryDetectDevLaunchCommand(string directory) =>
        _devServerDetector.TryDetectDevLaunchCommand(directory);

    public string FormatPackageScriptCommand(string directory, string scriptName) =>
        _devServerDetector.FormatPackageScriptCommand(directory, scriptName);
}
