namespace QuickShell.Services;

internal static class TaskTypeCommandSuggestion
{
    public const string FieldLabel = "Quick add commands";

    public const string FieldHelp =
        "Choose a command type to insert a suggested script for this project's folder.";

    public const string PickerTooltip =
        "Adds a new command row with a project-aware suggestion.";

    public static bool HasAvailableTypes(string? directory) =>
        GetAvailableTaskTypes(directory).Count > 0;

    public static IReadOnlyList<string> GetAvailableTaskTypes(string? directory) =>
        GetAvailableTaskTypes(directory, TaskTypePickContext.Empty);

    public static IReadOnlyList<string> GetAvailableTaskTypes(string? directory, TaskTypePickContext pickContext)
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

    public static bool IsAvailable(string? directory, string? taskType) =>
        IsAvailable(directory, taskType, TaskTypePickContext.Empty);

    public static bool IsAvailable(string? directory, string? taskType, TaskTypePickContext pickContext)
    {
        if (!TryBuildContext(directory, out var context))
        {
            return false;
        }

        return IsAvailable(TaskTypeCatalog.Normalize(taskType), context, pickContext);
    }

    public static IReadOnlyList<TaskTypeCandidate> GetCandidates(
        string? directory,
        string? taskType,
        TaskTypePickContext? pickContext = null)
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

        return TaskTypeCandidateBuilder.Build(normalized, context, pickContext ?? TaskTypePickContext.Empty);
    }

    public static string? TrySuggest(string? directory, string? taskType) =>
        TrySuggest(directory, taskType, TaskTypePickContext.Empty);

    public static string? TrySuggest(string? directory, string? taskType, TaskTypePickContext pickContext)
    {
        var candidates = GetCandidates(directory, taskType, pickContext);
        return candidates.Count > 0 ? candidates[0].Command : null;
    }

    public static string GetChoiceTooltip(string? directory, string? taskType) =>
        GetChoiceTooltip(directory, taskType, TaskTypePickContext.Empty);

    public static string GetChoiceTooltip(string? directory, string? taskType, TaskTypePickContext pickContext)
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

        var alternates = string.Join(
            ", ",
            candidates.Skip(1).Take(2).Select(candidate => candidate.Command));
        return $"Suggests: {first.Command} · also {alternates}";
    }

    private static bool IsAvailable(
        string taskType,
        TaskTypeCandidateBuilder.SuggestionContext context,
        TaskTypePickContext pickContext) =>
        TaskTypeCandidateBuilder.Build(taskType, context, pickContext).Count > 0;

    private static bool TryBuildContext(string? directory, out TaskTypeCandidateBuilder.SuggestionContext context)
    {
        context = default!;
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return false;
        }

        var classification = ProjectClassifier.Classify(directory);
        var suggestions = WorkspaceSetupSuggestion.Build(directory, classification);
        context = new TaskTypeCandidateBuilder.SuggestionContext(directory, suggestions, classification);
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
