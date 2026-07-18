namespace QuickShell.Services;

internal sealed record CompanionLaunchResult(
    bool Success,
    IReadOnlyList<string> StartedExecutables,
    string? Error);
