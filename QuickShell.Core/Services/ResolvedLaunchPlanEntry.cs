using QuickShell.Models;

namespace QuickShell.Services;

internal sealed record ResolvedLaunchPlanEntry(
    WorkspaceEntry Entry,
    ResolvedLaunch Resolved,
    bool EffectiveElevation,
    int Order);
