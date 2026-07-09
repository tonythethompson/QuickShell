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
}
