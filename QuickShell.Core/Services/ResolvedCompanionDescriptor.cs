namespace QuickShell.Services;

internal sealed record ResolvedCompanionDescriptor(
    string Path,
    string? ExpandedArguments,
    string WorkingDirectory,
    bool OpenOnLaunch);
