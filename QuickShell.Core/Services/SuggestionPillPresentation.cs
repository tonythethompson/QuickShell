namespace QuickShell.Services;

internal static class SuggestionPillPresentation
{
    public const int MaxSlots = 16;
    public const int DefaultVisibleSlots = 8;
    public const int DisplayTitleMaxLength = 42;

    public static string FormatDisplayTitle(string typeTitle, string command)
    {
        var full = string.IsNullOrWhiteSpace(typeTitle)
            ? command
            : $"{typeTitle} · {command}";

        if (full.Length <= DisplayTitleMaxLength)
        {
            return full;
        }

        return full[..(DisplayTitleMaxLength - 1)] + "…";
    }

    public static IReadOnlyDictionary<string, string> BuildDataFields(
        string? directory,
        IEnumerable<string?> usedCommands,
        bool expandSuggestionPills,
        bool isScanningSuggestions = false)
    {
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

        var pills = CommandSuggestionService.GetPills(directory, usedCommands);
        if (pills.Count == 0)
        {
            return fields;
        }

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
