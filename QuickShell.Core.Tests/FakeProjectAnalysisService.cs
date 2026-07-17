using QuickShell.Abstractions.Classification;
using QuickShell.Services;

namespace QuickShell.Core.Tests;

/// <summary>
/// No-op IProjectAnalysisService for tests that only need a bound QuickShellServices
/// and never exercise project analysis. Returns safe empty/neutral values.
/// </summary>
internal sealed class FakeProjectAnalysisService : IProjectAnalysisService
{
    public ProjectClassification Classify(string directory) => ProjectClassification.Empty;

    public bool HasAvailableTaskTypes(string? directory) => false;

    public IReadOnlyList<string> GetAvailableTaskTypes(string? directory, TaskTypePickContext pickContext) => [];

    public bool IsTaskTypeAvailable(string? directory, string? taskType, TaskTypePickContext pickContext) => false;

    public string? TrySuggestTaskCommand(string? directory, string? taskType, TaskTypePickContext pickContext) => null;

    public string GetTaskTypeChoiceTooltip(string? directory, string? taskType, TaskTypePickContext pickContext) =>
        string.Empty;

    public CompanionAppSuggestion? TrySuggestCompanionApp(string directory) => null;

    public string? TryDetectDevServerUrl(string directory) => null;

    public string? TryInferTaskType(string directory) => null;

    public string? TryDetectDevLaunchCommand(string directory) => null;

    public string FormatPackageScriptCommand(string directory, string scriptName) => scriptName;

    public string BuildTaskTypeChoicesJson(string? d = null, TaskTypePickContext? p = null, bool ip = true) => "[]";
}
