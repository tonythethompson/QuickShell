namespace QuickShell.Services;

internal sealed class CompanionAppSuggestion
{
    public required string PresetId { get; init; }

    public string? ExecutablePath { get; init; }

    public string Arguments { get; init; } = string.Empty;

    public bool EnableOnLaunch { get; init; }
}
