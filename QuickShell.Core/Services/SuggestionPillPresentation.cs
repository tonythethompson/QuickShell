using QuickShell.Abstractions;
using QuickShell.Abstractions.Classification;

namespace QuickShell.Services;

internal static class SuggestionPillPresentation
{
    public const int MaxSlots = 16;
    public const int DefaultVisibleSlots = 8;
    // Titles stay readable while the single flowing ActionSet wraps across the card;
    // full command remains on the tooltip.
    public const int DisplayTitleMaxLength = 48;

    /// <summary>Pill button label: command text only (truncated).</summary>
    public static string FormatDisplayTitle(string command)
    {
        var text = (command ?? string.Empty).Trim();
        if (text.Length <= DisplayTitleMaxLength)
        {
            return text;
        }

        return text[..(DisplayTitleMaxLength - 1)] + "…";
    }

    /// <summary>
    /// Hover text: category and optional product/friendly name, plus the command.
    /// Examples: <c>Test · npm test</c>, <c>Agent · Claude Code — … Adds `claude`.</c>
    /// </summary>
    public static string FormatTooltip(string categoryTitle, string command, string? productName = null, string? detail = null)
    {
        var category = (categoryTitle ?? string.Empty).Trim();
        var cmd = (command ?? string.Empty).Trim();
        var product = (productName ?? string.Empty).Trim();

        var head = string.IsNullOrWhiteSpace(product)
            ? (string.IsNullOrWhiteSpace(category) ? cmd : $"{category} · {cmd}")
            : (string.IsNullOrWhiteSpace(category) ? product : $"{category} · {product}");

        if (string.IsNullOrWhiteSpace(detail))
        {
            return head;
        }

        return $"{head} — {detail.Trim()}";
    }

    public static IReadOnlyDictionary<string, string> BuildDataFields(
        string? directory,
        IEnumerable<string?> usedCommands,
        IProjectAnalysisService projectAnalysis,
        ICommandSuggestionService commandSuggestions,
        bool expandSuggestionPills,
        bool isScanningSuggestions = false)
    {
        ArgumentNullException.ThrowIfNull(commandSuggestions);

        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ShowSuggestionPills"] = "false",
            ["SuggestionScanning"] = isScanningSuggestions ? "true" : "false",
            ["ExpandSuggestionPills"] = expandSuggestionPills ? "true" : "false",
            ["ShowMoreSuggestions"] = "false",
            ["ShowFewerSuggestions"] = "false",
        };

        for (var i = 0; i < MaxSlots; i++)
        {
            fields[$"ShowPill_{i}"] = "false";
            fields[$"PillTitle_{i}"] = string.Empty;
            fields[$"PillCommand_{i}"] = string.Empty;
            fields[$"PillTaskType_{i}"] = TaskTypeCatalog.None;
            fields[$"PillTooltip_{i}"] = string.Empty;
        }

        if (isScanningSuggestions
            || string.IsNullOrWhiteSpace(directory)
            || !Directory.Exists(directory))
        {
            return fields;
        }

        // Same ordered real-command list the form uses when applying a pill click.
        var pills = BuildSelectablePills(directory, usedCommands, projectAnalysis, commandSuggestions);

        fields["ShowSuggestionPills"] = pills.Count > 0 ? "true" : "false";
        fields["ShowMoreSuggestions"] = pills.Count > DefaultVisibleSlots && !expandSuggestionPills ? "true" : "false";
        fields["ShowFewerSuggestions"] = expandSuggestionPills && pills.Count > DefaultVisibleSlots ? "true" : "false";

        for (var i = 0; i < pills.Count && i < MaxSlots; i++)
        {
            var visible = i < DefaultVisibleSlots || expandSuggestionPills;
            fields[$"ShowPill_{i}"] = visible ? "true" : "false";
            fields[$"PillTitle_{i}"] = pills[i].DisplayTitle;
            fields[$"PillCommand_{i}"] = pills[i].Command;
            fields[$"PillTaskType_{i}"] = pills[i].TaskType;
            fields[$"PillTooltip_{i}"] = pills[i].Tooltip;
        }

        return fields;
    }

    /// <summary>Ranked real-command suggestions in the same order rendered by the form.</summary>
    public static IReadOnlyList<CommandSuggestionPill> BuildSelectablePills(
        string? directory,
        IEnumerable<string?> usedCommands,
        IProjectAnalysisService projectAnalysis,
        ICommandSuggestionService commandSuggestions)
    {
        ArgumentNullException.ThrowIfNull(commandSuggestions);

        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return [];
        }

        var ranked = commandSuggestions.GetPills(directory, usedCommands, projectAnalysis);

        // Loosely group same-type pills together for display (Agent, Test, Services, ...).
        // OrderBy is stable, so within each type group pills keep GetPills' original score
        // order -- this only reorders across groups, ranking within a group is untouched.
        return ranked.OrderBy(pill => pill.TypeTitle, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
