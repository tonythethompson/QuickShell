using QuickShell.Services;

namespace QuickShell.Abstractions.Classification;

internal interface IProjectAnalysisService
{
    ProjectClassification Classify(string directory);

    bool HasAvailableTaskTypes(string? directory);

    IReadOnlyList<string> GetAvailableTaskTypes(string? directory, TaskTypePickContext pickContext);

    bool IsTaskTypeAvailable(string? directory, string? taskType, TaskTypePickContext pickContext);

    string? TrySuggestTaskCommand(string? directory, string? taskType, TaskTypePickContext pickContext);

    string GetTaskTypeChoiceTooltip(string? directory, string? taskType, TaskTypePickContext pickContext);

    CompanionAppSuggestion? TrySuggestCompanionApp(string directory);

    string? TryDetectDevServerUrl(string directory);

    string? TryInferTaskType(string directory);

    string? TryDetectDevLaunchCommand(string directory);

    string FormatPackageScriptCommand(string directory, string scriptName);
}
