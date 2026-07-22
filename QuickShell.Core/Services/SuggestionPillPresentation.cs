using QuickShell.Abstractions;
using QuickShell.Abstractions.Classification;

namespace QuickShell.Services;

internal static class SuggestionPillPresentation
{
    /// <summary>Actions per Adaptive Card ActionSet row.</summary>
    public const int PillsPerRow = 4;

    /// <summary>Collapsed form shows this many ActionSet rows before "Show more".</summary>
    public const int DefaultVisibleRows = 3;

    public const int DefaultVisibleSlots = PillsPerRow * DefaultVisibleRows;

    /// <summary>Hard cap on ranked pills / Adaptive Card slots (5 rows when expanded).</summary>
    public const int MaxSlots = PillsPerRow * 5;

    // Readable titles at 4 per row; full command remains on the tooltip.
    public const int DisplayTitleMaxLength = 48;

    /// <summary>How many pill actions the Adaptive Card template should emit for this state.</summary>
    public static int GetVisiblePillCount(int totalPills, bool expandSuggestionPills, bool isScanningSuggestions)
    {
        if (isScanningSuggestions || totalPills <= 0)
        {
            return 0;
        }

        var capped = Math.Min(totalPills, MaxSlots);
        return expandSuggestionPills
            ? capped
            : Math.Min(capped, DefaultVisibleSlots);
    }

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
    /// CmdPal Adaptive Card title: optional type-color emoji + truncated command.
    /// Run/Raycast keep <see cref="FormatDisplayTitle"/> and paint color in chrome instead.
    /// </summary>
    public static string FormatCardTitle(string? taskType, string command)
    {
        var body = FormatDisplayTitle(command);
        var marker = TaskTypeCatalog.GetMarkerEmoji(taskType);
        return string.IsNullOrEmpty(marker) ? body : $"{marker} {body}";
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

        if (isScanningSuggestions
            || string.IsNullOrWhiteSpace(directory)
            || !Directory.Exists(directory))
        {
            return BuildDataFields([], expandSuggestionPills, isScanningSuggestions);
        }

        // Same ordered real-command list the form uses when applying a pill click.
        var pills = BuildSelectablePills(directory, usedCommands, projectAnalysis, commandSuggestions);
        return BuildDataFields(pills, expandSuggestionPills, isScanningSuggestions: false);
    }

    /// <summary>
    /// Fills Adaptive Card pill data from a precomputed presentation-ordered list
    /// (e.g. <c>WorkspaceEditState.Pills</c>) without calling <see cref="ICommandSuggestionService.GetPills"/>.
    /// </summary>
    public static IReadOnlyDictionary<string, string> BuildDataFields(
        IReadOnlyList<CommandSuggestionPill> pills,
        bool expandSuggestionPills,
        bool isScanningSuggestions = false)
    {
        ArgumentNullException.ThrowIfNull(pills);

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
            fields[$"PillStyle_{i}"] = "default";
            fields[$"PillTooltip_{i}"] = string.Empty;
        }

        if (isScanningSuggestions)
        {
            return fields;
        }

        fields["ShowSuggestionPills"] = pills.Count > 0 ? "true" : "false";
        fields["ShowMoreSuggestions"] = pills.Count > DefaultVisibleSlots && !expandSuggestionPills ? "true" : "false";
        fields["ShowFewerSuggestions"] = expandSuggestionPills && pills.Count > DefaultVisibleSlots ? "true" : "false";

        for (var i = 0; i < pills.Count && i < MaxSlots; i++)
        {
            // Template only contains the currently visible slots (no per-action $when).
            var visible = i < DefaultVisibleSlots || expandSuggestionPills;
            fields[$"ShowPill_{i}"] = visible ? "true" : "false";
            if (!visible)
            {
                continue;
            }

            fields[$"PillTitle_{i}"] = FormatCardTitle(pills[i].TaskType, pills[i].Command);
            fields[$"PillCommand_{i}"] = pills[i].Command;
            fields[$"PillTaskType_{i}"] = pills[i].TaskType;
            fields[$"PillStyle_{i}"] = TaskTypeCatalog.GetAdaptiveCardActionStyle(pills[i].TaskType);
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
        // Group order follows each type's best score so lower-scored agents sink below
        // Build/API/Test. Within a group, GetPills' score order is preserved (GroupBy is stable).
        return ranked
            .GroupBy(pill => pill.TypeTitle, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Max(pill => pill.Score))
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .SelectMany(group => group)
            .ToList();
    }
}
