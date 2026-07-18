namespace QuickShell.Services;

internal sealed record ResolvedLaunchGroup(
    IReadOnlyList<ResolvedLaunchPlanEntry> Entries,
    string? HostExecutable,
    bool EffectiveElevation);
