namespace QuickShell.Services;

/// <summary>
/// Well-known deep-link IDs and prefixes for host pages and commands.
/// </summary>
internal static class QuickShellDeepLinkIds
{
    public const string Settings = "com.quickshell.settings";

    public const string ImportConflict = "com.quickshell.import-conflict";

    public const string PendingShortcutEdit = "com.quickshell.pending-shortcut-edit";

    public const string DiscoverGitRepos = "com.quickshell.discover-git-repos";

    public const string OpenPrefix = "com.quickshell.shortcut.open.";

    public const string LaunchSeparator = ".launch.";

    public const string DiscoverCreatePrefix = "com.quickshell.discover.create.";

    public const string WorkspaceStatusPrefix = "com.quickshell.workspace-status.";

    public const string WorktreeBranchPickerPrefix = "com.quickshell.worktree-branch.picker.page.";

    public const string WorktreeBranchSelectPrefix = "com.quickshell.worktree-branch.select.";

    public const string WorktreeBranchClearPrefix = "com.quickshell.worktree-branch.clear.";
}
