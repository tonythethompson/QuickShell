using QuickShell.Abstractions;
using QuickShell.Abstractions.Classification;

namespace QuickShell.Services;

internal static class SuggestionPillPresentation
{
    public const int MaxSlots = 16;
    public const int DefaultVisibleSlots = 8;
    // 60 not 42 -- pills render 2 per row instead of 3 (see PillsPerRow in
    // ShortcutLaunchFormJson.SuggestionPills.cs), so there's more horizontal room per pill.
    public const int DisplayTitleMaxLength = 60;

    /// <summary>
    /// Always-available pill that explicitly marks a launch row as folder-only (blank Command,
    /// no task). Previously a row was only ever implicitly folder-only by having a blank
    /// Command with no visible indication, which was indistinguishable from a still-empty
    /// unused row — confusing given new shortcuts default to three blank rows and only one
    /// intentionally-blank convention (not all three opening a shell). Appended after ranked
    /// suggestions so it never displaces a real command match.
    /// </summary>
    public static readonly CommandSuggestionPill OpenToDirectoryPill = new(
        Command: string.Empty,
        TaskType: TaskTypeCatalog.None,
        TypeTitle: "Folder",
        DisplayTitle: "Open to Directory",
        Tooltip: "Open this folder without running a command.",
        Score: 0,
        Source: "folder-only");

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
        IProjectClassificationCache classificationCache,
        bool expandSuggestionPills,
        bool isScanningSuggestions = false)
    {
        ArgumentNullException.ThrowIfNull(classificationCache);

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

        var ranked = CommandSuggestionService.GetPills(directory, usedCommands, projectAnalysis, classificationCache);

        // Loosely group same-type pills together for display (Agent, Test, Services, ...).
        // OrderBy is stable, so within each type group pills keep GetPills' original score
        // order -- this only reorders across groups, ranking within a group is untouched.
        var pills = new List<CommandSuggestionPill>(ranked.Count + 1);
        pills.AddRange(ranked.OrderBy(pill => pill.TypeTitle, StringComparer.OrdinalIgnoreCase));
        pills.Add(OpenToDirectoryPill);

        fields["ShowSuggestionPills"] = "true";
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

    public static IReadOnlyDictionary<string, string> BuildClearLaunchFields(
        IReadOnlyList<LaunchRowDraft> rows)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < rows.Count; i++)
        {
            fields[$"ShowClearLaunch_{i}"] = string.IsNullOrWhiteSpace(rows[i].Command) ? "false" : "true";
        }

        return fields;
    }
}
