using QuickShell.Abstractions.Classification;
using QuickShell.Services;

namespace QuickShell.Classification;

internal sealed class ProjectAnalysisService : IProjectAnalysisService
{
    private readonly IReadOnlyList<IProjectClassifier> _classifiers;
    private readonly IProjectLayoutAnalyzer _layoutAnalyzer;
    private readonly ICompanionAppDetector _companionAppDetector;
    private readonly IDevServerDetector _devServerDetector;
    private readonly ICommandSuggestionService _commandSuggestionService;

    public ProjectAnalysisService(IEnumerable<IProjectClassifier> classifiers, IProjectLayoutAnalyzer layoutAnalyzer, ICompanionAppDetector companionAppDetector, IDevServerDetector devServerDetector, ICommandSuggestionService commandSuggestionService)
    {
        _classifiers = classifiers.OrderByDescending(c => c.Priority).ToArray();
        _layoutAnalyzer = layoutAnalyzer ?? throw new ArgumentNullException(nameof(layoutAnalyzer));
        _companionAppDetector = companionAppDetector ?? throw new ArgumentNullException(nameof(companionAppDetector));
        _devServerDetector = devServerDetector ?? throw new ArgumentNullException(nameof(devServerDetector));
        _commandSuggestionService = commandSuggestionService ?? throw new ArgumentNullException(nameof(commandSuggestionService));
    }

    public ProjectClassification Classify(string directory) => ProjectClassificationPipeline.Classify(directory, _classifiers, _layoutAnalyzer);
    public bool HasAvailableTaskTypes(string? directory) => GetAvailableTaskTypes(directory, TaskTypePickContext.Empty).Count > 0;
    public IReadOnlyList<string> GetAvailableTaskTypes(string? directory, TaskTypePickContext pickContext) => GetPills(directory, pickContext).Where(p => LaunchCommandSanity.IsUsableSuggestion(p.Command)).Select(p => p.TaskType).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    public bool IsTaskTypeAvailable(string? directory, string? taskType, TaskTypePickContext pickContext) => TaskTypeCatalog.Normalize(taskType) is var n && n != TaskTypeCatalog.None && GetPills(directory, pickContext).Any(p => string.Equals(p.TaskType, n, StringComparison.Ordinal));
    public string? TrySuggestTaskCommand(string? directory, string? taskType, TaskTypePickContext pickContext) => GetTaskTypePills(directory, taskType, pickContext) is var c && c.Count > 0 ? c[0].Command : null;
    public string GetTaskTypeChoiceTooltip(string? directory, string? taskType, TaskTypePickContext pickContext) { var n = TaskTypeCatalog.Normalize(taskType); var c = GetTaskTypePills(directory, taskType, pickContext); if (c.Count == 0) return GetStaticChoiceTooltip(n); var f = c[0]; if (c.Count == 1) return $"Suggests: {f.Command}"; return $"Suggests: {f.Command} \u00b7 also {string.Join(", ", c.Skip(1).Take(2).Select(x => x.Command))}"; }

    public string BuildTaskTypeChoicesJson(string? directory = null, TaskTypePickContext? pickContext = null, bool includePlaceholder = true)
    {
        pickContext ??= TaskTypePickContext.Empty;
        var choices = new List<object>();
        if (includePlaceholder) choices.Add(new { title = "Choose a command\u2026", value = TaskTypeCatalog.None, tooltip = "Adds a new command row with a project-aware suggestion." });
        foreach (var def in TaskTypeCatalog.GetChoices())
            if (IsTaskTypeAvailable(directory, def.Id, pickContext))
                choices.Add(new { title = def.Title, value = def.Id, tooltip = GetTaskTypeChoiceTooltip(directory, def.Id, pickContext) });
        return System.Text.Json.JsonSerializer.Serialize(choices);
    }

    public CompanionAppSuggestion? TrySuggestCompanionApp(string directory) => _companionAppDetector.TrySuggest(directory);
    public string? TryDetectDevServerUrl(string directory) => _devServerDetector.TryDetectDevServerUrl(directory);
    public string? TryInferTaskType(string directory) => _devServerDetector.TryInferTaskType(directory);
    public string? TryDetectDevLaunchCommand(string directory) => _devServerDetector.TryDetectDevLaunchCommand(directory);
    public string FormatPackageScriptCommand(string directory, string scriptName) => _devServerDetector.FormatPackageScriptCommand(directory, scriptName);

    private IReadOnlyList<CommandSuggestionPill> GetPills(string? directory, TaskTypePickContext pickContext) => _commandSuggestionService.GetPills(directory, pickContext.UsedCommands, this, int.MaxValue);
    private IReadOnlyList<CommandSuggestionPill> GetTaskTypePills(string? directory, string? taskType, TaskTypePickContext pickContext) { var n = TaskTypeCatalog.Normalize(taskType); return n == TaskTypeCatalog.None ? [] : GetPills(directory, pickContext).Where(p => string.Equals(p.TaskType, n, StringComparison.Ordinal)).ToList(); }
    private static string GetStaticChoiceTooltip(string taskType) => taskType switch { TaskTypeCatalog.Api => "Backend or API server (e.g. dotnet watch, go run)", TaskTypeCatalog.Frontend => "Dev server or UI (e.g. npm run dev)", TaskTypeCatalog.Services => "Infrastructure services (e.g. docker compose up postgres)", TaskTypeCatalog.Logs => "Log stream (e.g. docker compose logs -f)", TaskTypeCatalog.Test => "Test runner (e.g. dotnet test, npm test)", TaskTypeCatalog.Build => "Build or compile (e.g. dotnet build, npm run build)", _ => "No category \u2014 leaves the command unchanged" };
}
