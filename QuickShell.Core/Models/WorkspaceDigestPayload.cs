namespace QuickShell.Models;

/// <summary>
/// Shape of the payload hashed by <see cref="QuickShell.Services.WorkspaceSecurityPolicy.ComputeDigest"/>.
/// A named type (not an anonymous type) so it can go through the source-generated
/// <see cref="QuickShell.QuickShellDigestJsonContext"/> instead of reflection-based serialization,
/// which trimming forbids. Field set and order are load-bearing: changing either changes the
/// digest and invalidates outstanding <c>WorkspaceReviewToken</c> values.
/// </summary>
internal sealed record WorkspaceDigestPayload(
    string Id,
    string Name,
    string Directory,
    string? Command,
    string Terminal,
    string? WtProfile,
    bool RunAsAdmin,
    List<WorkspaceEntry> Launches,
    string? DevServerUrl,
    bool OpenDevServerOnLaunch,
    string? RepoUrl,
    List<CompanionAppEntry> CompanionApps,
    bool OpenCompanionAppOnLaunch,
    string? CompanionAppPath,
    string? CompanionAppArguments);
