using System.Text.Json;

namespace QuickShell.Services;

internal static class TaskTypeCatalog
{
    public const string None = "none";
    public const string Api = "api";
    public const string Frontend = "frontend";
    public const string Database = "database";
    public const string Logs = "logs";

    private static readonly IReadOnlyList<(string Id, string Title)> Definitions =
    [
        (Api, "API"),
        (Frontend, "Frontend"),
        (Database, "Database"),
        (Logs, "Logs"),
    ];

    public static IReadOnlyList<(string Id, string Title)> GetChoices() => Definitions;

    public static string BuildFormChoicesJson()
    {
        var choices = new List<object> { new { title = "None", value = None } };
        foreach (var definition in Definitions)
        {
            choices.Add(new { title = definition.Title, value = definition.Id });
        }

        return JsonSerializer.Serialize(choices);
    }

    public static string Normalize(string? id) =>
        (id ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            Api => Api,
            Frontend => Frontend,
            Database => Database,
            Logs => Logs,
            _ => None,
        };

    public static string GetTitle(string? id) =>
        Normalize(id) switch
        {
            Api => "API",
            Frontend => "Frontend",
            Database => "Database",
            Logs => "Logs",
            _ => string.Empty,
        };

    public static string? GetGlyph(string? id) =>
        Normalize(id) switch
        {
            Api => ShortcutGlyphs.TaskApi,
            Frontend => ShortcutGlyphs.TaskFrontend,
            Database => ShortcutGlyphs.TaskDatabase,
            Logs => ShortcutGlyphs.TaskLogs,
            _ => null,
        };
}
