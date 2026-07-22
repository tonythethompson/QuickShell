using System.Text.Json;
using QuickShell.Abstractions.Classification;

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

    private static readonly IReadOnlyList<(string Id, string Title)> Definitions = [(Api, "API"), (Frontend, "Frontend"), (Services, "Services"), (Logs, "Logs"), (Test, "Test"), (Build, "Build"), (Agent, "Agent")];
    public static IReadOnlyList<(string Id, string Title)> GetChoices() => Definitions;

    public static string BuildFormChoicesJson(IProjectAnalysisService projectAnalysis, string? directory = null, TaskTypePickContext? pickContext = null) => projectAnalysis.BuildTaskTypeChoicesJson(directory, pickContext, includePlaceholder: false);
    public static string BuildPickerChoicesJson(IProjectAnalysisService projectAnalysis, string? directory = null, bool includePlaceholder = true, TaskTypePickContext? pickContext = null) => projectAnalysis.BuildTaskTypeChoicesJson(directory, pickContext, includePlaceholder);

    public static string Normalize(string? id) => (id ?? string.Empty).Trim().ToLowerInvariant() switch { Api => Api, Frontend => Frontend, Services or "database" => Services, Logs => Logs, Test or "tests" => Test, Build => Build, Agent or "ai" or "agents" => Agent, _ => None };
    public static string GetTitle(string? id) => Normalize(id) switch { Api => "API", Frontend => "Frontend", Services => "Services", Logs => "Logs", Test => "Test", Build => "Build", Agent => "Agent", _ => string.Empty };
    public static string? GetGlyph(string? id) => Normalize(id) switch { Api => ShortcutGlyphs.TaskApi, Frontend => ShortcutGlyphs.TaskFrontend, Services => ShortcutGlyphs.TaskServices, Logs => ShortcutGlyphs.TaskLogs, Test => ShortcutGlyphs.TaskTest, Build => ShortcutGlyphs.TaskBuild, Agent => ShortcutGlyphs.TaskAgent, _ => null };

    /// <summary>Colored circle emoji for CmdPal Adaptive Card pill titles (Action.Submit cannot use arbitrary colors).</summary>
    public static string? GetMarkerEmoji(string? id) => Normalize(id) switch
    {
        Api => "🔵",
        Frontend => "🟢",
        Services => "⚪",
        Logs => "🟤",
        Test => "🟡",
        Build => "🟠",
        Agent => "🟣",
        _ => null,
    };

    /// <summary>
    /// Adaptive Cards Action.Submit <c>style</c>. Always <c>default</c> so type color comes only from
    /// <see cref="GetMarkerEmoji"/> (positive/destructive paint the whole button the same accent).
    /// </summary>
    public static string GetAdaptiveCardActionStyle(string? id)
    {
        _ = id;
        return "default";
    }

    /// <summary>Opaque ARGB accent for WPF / other hosts that can paint real colors.</summary>
    public static uint GetAccentArgb(string? id) => Normalize(id) switch
    {
        Api => 0xFF3498DB,
        Frontend => 0xFF1ABC9C,
        Services => 0xFF95A5A6,
        Logs => 0xFFA0522D,
        Test => 0xFF27AE60,
        Build => 0xFFE67E22,
        Agent => 0xFF9B59B6,
        _ => 0xFF808080,
    };
}
