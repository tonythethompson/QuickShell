namespace QuickShell.Services;

/// <summary>
/// Well-known deep-link IDs for host pages (mirrors page <c>PageId</c> constants).
/// Kept in Core so <see cref="CommandIdParser"/> stays SDK-free.
/// </summary>
internal static class QuickShellDeepLinkIds
{
    public const string Settings = "com.quickshell.settings";

    public const string ImportConflict = "com.quickshell.import-conflict";

    public const string PendingShortcutEdit = "com.quickshell.pending-shortcut-edit";

    public const string DiscoverGitRepos = "com.quickshell.discover-git-repos";
}
