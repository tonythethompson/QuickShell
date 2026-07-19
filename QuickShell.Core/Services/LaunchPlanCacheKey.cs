namespace QuickShell.Services;

internal readonly record struct LaunchPlanCacheKey(
    string WorkspaceId,
    long RepositoryVersion,
    string SettingsFingerprint,
    string TerminalCatalogFingerprint,
    string? LaunchEntryId,
    bool RunAsAdmin,
    bool RunAsStandard);
