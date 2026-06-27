namespace QuickShell.Services;

internal enum TerminalSettingsSource
{
    WindowsTerminal,
    WindowsTerminalPreview,
    IntelligentTerminal,
    Unpackaged,
}

internal sealed class TerminalSettingsLocation
{
    public required string SettingsPath { get; init; }

    public required TerminalSettingsSource Source { get; init; }

    public required string HostExecutable { get; init; }

    public required string IdPrefix { get; init; }

    public required string DisplayPrefix { get; init; }
}
