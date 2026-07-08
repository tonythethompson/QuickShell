namespace QuickShell.Services;

internal sealed record CommandSuggestionPill(
    string Command,
    string TaskType,
    string TypeTitle,
    string DisplayTitle,
    string Tooltip,
    int Score,
    string Source);
