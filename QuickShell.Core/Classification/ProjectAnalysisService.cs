using QuickShell.Abstractions.Classification;
using QuickShell.Models;
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
        _classifiers = classifiers.OrderByDescending(c => c.Priority).ToArray();
        _layoutAnalyzer = layoutAnalyzer ?? throw new ArgumentNullException(nameof(layoutAnalyzer));
        _companionAppDetector = companionAppDetector ?? throw new ArgumentNullException(nameof(companionAppDetector));
        _devServerDetector = devServerDetector ?? throw new ArgumentNullException(nameof(devServerDetector));
    }

    public ProjectClassification Classify(string directory) =>
        ProjectClassificationPipeline.Classify(directory, _classifiers, _layoutAnalyzer);

    public bool HasAvailableTaskTypes(string? directory) =>
        GetAvailableTaskTypes(directory, TaskTypePickContext.Empty).Count > 0;

    public IReadOnlyList<string> GetAvailableTaskTypes(string? directory, TaskTypePickContext pickContext)
    {
        if (!TryBuildSuggestionContext(directory, out var context))
        {
            return [];
        }

        return TaskTypeCatalog.GetChoices()
            .Where(choice => TaskTypeCandidateBuilder.Build(choice.Id, context, pickContext).Count > 0)
            .Select(choice => choice.Id)
            .ToList();
    }

    public bool IsTaskTypeAvailable(string? directory, string? taskType, TaskTypePickContext pickContext)
    {
        if (!TryBuildSuggestionContext(directory, out var context))
        {
            return false;
        }

        var normalized = TaskTypeCatalog.Normalize(taskType);
        return normalized != TaskTypeCatalog.None
            && TaskTypeCandidateBuilder.Build(normalized, context, pickContext).Count > 0;
    }

    public string? TrySuggestTaskCommand(string? directory, string? taskType, TaskTypePickContext pickContext)
    {
        var candidates = GetCandidates(directory, taskType, pickContext);
        return candidates.Count > 0 ? candidates[0].Command : null;
    }

    public string GetTaskTypeChoiceTooltip(string? directory, string? taskType, TaskTypePickContext pickContext)
    {
        var normalized = TaskTypeCatalog.Normalize(taskType);
        var candidates = GetCandidates(directory, normalized, pickContext);
        if (candidates.Count == 0)
        {
            return GetStaticChoiceTooltip(normalized);
        }

        var first = candidates[0];
        if (candidates.Count == 1)
        {
            return $"Suggests: {first.Command}";
        }

        var alternates = string.Join(", ", candidates.Skip(1).Take(2).Select(c => c.Command));
        return $"Suggests: {first.Command} · also {alternates}";
    }

    /// <summary>
    /// Builds a JSON payload containing the available task type choices.
    /// </summary>
    /// <param name="directory">The project directory used to determine available choices.</param>
    /// <param name="pickContext">Context used to select task type suggestions.</param>
    /// <param name="includePlaceholder">Whether to include the choice for adding a new command row.</param>
    /// <returns>A JSON representation of the task type choices.</returns>
    public string BuildTaskTypeChoicesJson(
        string? directory = null,
        TaskTypePickContext? pickContext = null,
        bool includePlaceholder = true)
    {
        pickContext ??= TaskTypePickContext.Empty;
        var choices = new List<TaskTypeChoiceJson>();
        if (includePlaceholder)
        {
            choices.Add(new TaskTypeChoiceJson(
                "Choose a command…",
                TaskTypeCatalog.None,
                "Adds a new command row with a project-aware suggestion."));
        }

        foreach (var def in TaskTypeCatalog.GetChoices())
        {
            if (!IsTaskTypeAvailable(directory, def.Id, pickContext))
            {
                continue;
            }

            choices.Add(new TaskTypeChoiceJson(
                def.Title,
                def.Id,
                GetTaskTypeChoiceTooltip(directory, def.Id, pickContext)));
        }

        return System.Text.Json.JsonSerializer.Serialize(choices, QuickShellJsonContext.Default.ListTaskTypeChoiceJson);
    }

    /// <summary>
        /// Suggests a companion app for the specified project directory.
        /// </summary>
        /// <param name="directory">The project directory to inspect.</param>
        /// <returns>A companion app suggestion, or null when no suggestion is available.</returns>
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
        if (!TryBuildSuggestionContext(directory, out var context))
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

    private bool TryBuildSuggestionContext(
        string? directory,
        out TaskTypeCandidateBuilder.SuggestionContext context)
    {
        context = default!;
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return false;
        }

        var classification = Classify(directory);
        var suggestions = WorkspaceSetupSuggestion.Build(directory, classification, this);
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
            _ => "No category: leaves the command unchanged",
        };
}
