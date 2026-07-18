namespace QuickShell.Services;

internal sealed record ResolvedWorkspaceLaunchPlan(
    string WorkspaceId,
    long RepositoryVersion,
    IReadOnlyList<ResolvedLaunchPlanEntry> Entries,
    IReadOnlyList<ResolvedLaunchGroup> Groups,
    IReadOnlyList<ResolvedCompanionDescriptor> Companions);
