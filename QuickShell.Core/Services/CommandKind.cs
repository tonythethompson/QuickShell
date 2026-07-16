namespace QuickShell.Services;

/// <summary>
/// Categories for deep-link command IDs handled by <c>GetCommandItem</c>.
/// </summary>
internal enum CommandKind
{
    OpenSettings,
    ImportConflict,
    PendingShortcutEdit,
    CreateWorkspace,
    DiscoverCreateWorkspace,
    DiscoverGitRepos,
    OpenLaunch,
    OpenWorkspace,
    WorkspaceStatus,
    WorktreeBranchPicker,
    WorktreeBranchSelect,
    WorktreeBranchClear,
    FavoriteToggle,
    FavoriteMove,
}
