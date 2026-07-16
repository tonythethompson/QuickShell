using QuickShell.Abstractions.Classification;
using QuickShell.Services;

namespace QuickShell.Classification;

internal sealed class ProjectAnalysisService : IProjectAnalysisService
{
    private readonly IReadOnlyList<IProjectClassifier> _classifiers;
    private readonly IProjectLayoutAnalyzer _layoutAnalyzer;
    private readonly ICompanionAppDetector _companionAppDetector;
    private readonly IDevServerDetector _devServerDetector;
    private readonly IReadOnlyList<ITaskSuggestionProvider> _taskSuggestionProviders;

    public ProjectAnalysisService(
        IEnumerable<IProjectClassifier> classifiers,
        IProjectLayoutAnalyzer layoutAnalyzer,
        ICompanionAppDetector companionAppDetector,
        IDevServerDetector devServerDetector,
        IEnumerable<ITaskSuggestionProvider> taskSuggestionProviders)
    {
        ArgumentNullException.ThrowIfNull(classifiers);
        ArgumentNullException.ThrowIfNull(layoutAnalyzer);
        ArgumentNullException.ThrowIfNull(companionAppDetector);
        ArgumentNullException.ThrowIfNull(devServerDetector);
        ArgumentNullException.ThrowIfNull(taskSuggestionProviders);

        _classifiers = classifiers.OrderByDescending(classifier => classifier.Priority).ToArray();
        _layoutAnalyzer = layoutAnalyzer;
        _companionAppDetector = companionAppDetector;
        _devServerDetector = devServerDetector;
        _taskSuggestionProviders = taskSuggestionProviders.OrderByDescending(provider => provider.Priority).ToArray();
    }

    public ProjectClassification Classify(string directory) =>
        ProjectClassificationPipeline.Classify(directory, _classifiers, _layoutAnalyzer);

    public bool HasAvailableTaskTypes(string? directory) =>
        GetAvailableTaskTypes(directory, TaskTypePickContext.Empty).Count > 0;

    public IReadOnlyList<string> GetAvailableTaskTypes(string? directory, TaskTypePickContext pickContext)
    {
        if (!TryBuildContext(directory, out var context))
        {
            return [];
        }

        return TaskTypeCatalog.GetChoices()
            .Where(choice => IsAvailable(choice.Id, context, pickContext))
            .Select(choice => choice.Id)
            .ToList();
    }

    public bool IsTaskTypeAvailable(string? directory, string? taskType, TaskTypePickContext pickContext)
    {
        if (!TryBuildContext(directory, out var context))
        {
            return false;
        }

        var normalized = TaskTypeCatalog.Normalize(taskType);
        return IsAvailable(normalized, context, pickContext);
    }

    public string? TrySuggestTaskCommand(string? directory, string? taskType, TaskTypePickContext pickContext)
    {
        var candidates = GetCandidates(directory, taskType, pickContext);
        return candidates.Count > 0 ? candidates[0].Command : null;
    }

    public string GetTaskTypeChoiceTooltip(string? directory, string? taskType, TaskTypePickContext pickContext)
    {
        var normalized = TaskTypeCatalog.Normalize(taskType);
        if (!TryBuildContext(directory, out var context))
        {
            return GetStaticChoiceTooltip(normalized);
        }

        var candidates = TaskTypeCandidateBuilder.Build(normalized, context, pickContext);
        if (candidates.Count == 0)
        {
            return GetStaticChoiceTooltip(normalized);
        }

        var first = candidates[0];
        if (candidates.Count == 1)
        {
            return $"Suggests: {first.Command}";
        }

        var alternates = string.Join(
            ", ",
            candidates.Skip(1).Take(2).Select(candidate => candidate.Command));
        return $"Suggests: {first.Command} · also {alternates}";
    }

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

    private IReadOnlyList<TaskTypeCandidate> GetCandidates(
        string? directory,
        string? taskType,
        TaskTypePickContext pickContext)
    {
        if (!TryBuildContext(directory, out var context))
        {
            return [];
        }

        var normalized = TaskTypeCatalog.Normalize(taskType);
        if (normalized == TaskTypeCatalog.None)
        {
            return [];
        }

        return TaskTypeCandidateBuilder.Build(normalized, context, pickContext);
    }

    private static bool IsAvailable(
        string taskType,
        TaskTypeCandidateBuilder.SuggestionContext context,
        TaskTypePickContext pickContext) =>
        TaskTypeCandidateBuilder.Build(taskType, context, pickContext).Count > 0;

    private bool TryBuildContext(string? directory, out TaskTypeCandidateBuilder.SuggestionContext context)
    {
        context = default!;
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return false;
        }

        var classification = Classify(directory);
        var suggestions = _taskSuggestionProviders
            .SelectMany(provider => provider.GetSuggestions(directory, classification, this))
            .ToList();
        context = new TaskTypeCandidateBuilder.SuggestionContext(directory, suggestions, classification, this);
        return true;
    }

    private static string GetStaticChoiceTooltip(string taskType) =>
        taskType switch
        {
            TaskTypeCatalog.Api => "Backend or API server (e.g. dotnet watch, go run)",
            TaskTypeCatalog.Frontend => "Dev server or UI (e.g. npm run dev)",
            TaskTypeCatalog.Services => "Infrastructure services (e.g. docker compose up postgres)",
            TaskTypeCatalog.Logs => "Log stream (e.g. docker compose logs -f)",
            TaskTypeCatalog.Test => "Test runner (e.g. dotnet test, npm test)",
            TaskTypeCatalog.Build => "Build or compile (e.g. dotnet build, npm run build)",
            _ => "No category — leaves the command unchanged",
        };
}
