using System.Text.Json;

namespace QuickShell.Services;

internal static class TaskTypeCatalog
{
    public const string None = "none";
    public const string Api = "api";
    public const string Frontend = "frontend";
    public const string Services = "services";
    public const string Logs = "logs";
    public const string Test = "test";
    public const string Build = "build";
    public const string Agent = "agent";

    public const string Database = Services;

    private static readonly IReadOnlyList<(string Id, string Title)> Definitions =
    [
        (Api, "API"),
        (Frontend, "Frontend"),
        (Services, "Services"),
        (Logs, "Logs"),
        (Test, "Test"),
        (Build, "Build"),
    ];

    public static IReadOnlyList<(string Id, string Title)> GetChoices() => Definitions;

    public static string BuildFormChoicesJson(string? directory = null, TaskTypePickContext? pickContext = null) =>
        BuildPickerChoicesJson(directory, includePlaceholder: false, pickContext);

    public static string BuildPickerChoicesJson(
        string? directory = null,
        bool includePlaceholder = true,
        TaskTypePickContext? pickContext = null)
    {
        pickContext ??= TaskTypePickContext.Empty;
        var choices = new List<object>();
        if (includePlaceholder)
        {
            choices.Add(new
            {
                title = "Choose a command…",
                value = None,
                tooltip = TaskTypeCommandSuggestion.PickerTooltip,
            });
        }

        foreach (var definition in Definitions)
        {
            if (!TaskTypeCommandSuggestion.IsAvailable(directory, definition.Id, pickContext))
            {
                continue;
            }

            choices.Add(new
            {
                title = definition.Title,
                value = definition.Id,
                tooltip = TaskTypeCommandSuggestion.GetChoiceTooltip(directory, definition.Id, pickContext),
            });
        }

        return JsonSerializer.Serialize(choices);
    }

    public static string Normalize(string? id) =>
        (id ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            Api => Api,
            Frontend => Frontend,
            Services or "database" => Services,
            Logs => Logs,
            Test or "tests" => Test,
            Build => Build,
            Agent or "ai" or "agents" => Agent,
            _ => None,
        };

    public static string GetTitle(string? id) =>
        Normalize(id) switch
        {
            Api => "API",
            Frontend => "Frontend",
            Services => "Services",
            Logs => "Logs",
            Test => "Test",
            Build => "Build",
            Agent => "Agent",
            _ => string.Empty,
        };

    public static string? GetGlyph(string? id) =>
        Normalize(id) switch
        {
            Api => ShortcutGlyphs.TaskApi,
            Frontend => ShortcutGlyphs.TaskFrontend,
            Services => ShortcutGlyphs.TaskServices,
            Logs => ShortcutGlyphs.TaskLogs,
            Test => ShortcutGlyphs.TaskTest,
            Build => ShortcutGlyphs.TaskBuild,
            Agent => ShortcutGlyphs.TaskAgent,
            _ => null,
        };
}
