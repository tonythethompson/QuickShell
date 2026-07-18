using System.Text;

namespace QuickShell.Services;

/// <summary>
/// Typed command descriptor and factory for QuickShell CmdPal deep-link IDs.
/// Owns the full ID construction, decoding, and schema constants so callers do not
/// build command strings by hand.
/// </summary>
/// <param name="Id">Raw command ID from the host.</param>
/// <param name="Kind">Resolved command category.</param>
/// <param name="WorkspaceId">Shortcut/workspace id for open/launch/git kinds.</param>
/// <param name="LaunchId">Launch id for <see cref="CommandKind.OpenLaunch"/>.</param>
/// <param name="Directory">Decoded directory for discover-create.</param>
/// <param name="Branch">Git branch name for worktree select.</param>
internal sealed record CommandDescriptor(
    string Id,
    CommandKind Kind,
    string? WorkspaceId = null,
    string? LaunchId = null,
    string? Directory = null,
    string? Branch = null)
{
    // Well-known deep-link IDs
    public const string SettingsId = "com.quickshell.settings";
    public const string ImportConflictId = "com.quickshell.import-conflict";
    public const string PendingShortcutEditId = "com.quickshell.pending-shortcut-edit";
    public const string DiscoverGitReposId = "com.quickshell.discover-git-repos";
    public const string CreateWorkspaceId = "com.quickshell.shortcut-form.create";

    // Deep-link prefixes
    public const string OpenPrefix = "com.quickshell.shortcut.open.";
    public const string LaunchSeparator = ".launch.";
    public const string DiscoverCreatePrefix = "com.quickshell.discover.create.";
    public const string WorkspaceStatusPrefix = "com.quickshell.workspace-status.";
    public const string WorktreeBranchPickerPrefix = "com.quickshell.worktree-branch.picker.page.";
    public const string WorktreeBranchSelectPrefix = "com.quickshell.worktree-branch.select.";
    public const string WorktreeBranchClearPrefix = "com.quickshell.worktree-branch.clear.";

    // In-page command IDs (not parsed as deep-links)
    public const string FavoriteTogglePrefix = "com.quickshell.shortcut.favorite.";
    public const string FavoriteMovePrefix = "com.quickshell.shortcut.move.";

    private const string AdminSuffix = ".admin";
    private const string StandardSuffix = ".standard";

    public static CommandDescriptor Settings() =>
        new(SettingsId, CommandKind.OpenSettings);

    public static CommandDescriptor ImportConflict() =>
        new(ImportConflictId, CommandKind.ImportConflict);

    public static CommandDescriptor PendingShortcutEdit() =>
        new(PendingShortcutEditId, CommandKind.PendingShortcutEdit);

    public static CommandDescriptor DiscoverGitRepos() =>
        new(DiscoverGitReposId, CommandKind.DiscoverGitRepos);

    public static CommandDescriptor CreateWorkspace() =>
        new(CreateWorkspaceId, CommandKind.CreateWorkspace);

    public static CommandDescriptor OpenWorkspace(string workspaceId, bool runAsAdmin = false, bool runAsStandard = false) =>
        new($"{OpenPrefix}{workspaceId}{VariantSuffix(runAsAdmin, runAsStandard)}",
            CommandKind.OpenWorkspace,
            WorkspaceId: workspaceId);

    public static CommandDescriptor OpenLaunch(
        string workspaceId,
        string launchId,
        bool runAsAdmin = false,
        bool runAsStandard = false) =>
        new($"{OpenPrefix}{workspaceId}{LaunchSeparator}{launchId}{VariantSuffix(runAsAdmin, runAsStandard)}",
            CommandKind.OpenLaunch,
            WorkspaceId: workspaceId,
            LaunchId: launchId);

    public static CommandDescriptor DiscoverCreate(string directory) =>
        new($"{DiscoverCreatePrefix}{EncodeDirectoryKey(directory)}",
            CommandKind.DiscoverCreateWorkspace,
            Directory: directory);

    public static CommandDescriptor WorkspaceStatus(string workspaceId) =>
        new($"{WorkspaceStatusPrefix}{workspaceId}",
            CommandKind.WorkspaceStatus,
            WorkspaceId: workspaceId);

    public static CommandDescriptor WorktreeBranchPicker(string workspaceId) =>
        new($"{WorktreeBranchPickerPrefix}{workspaceId}",
            CommandKind.WorktreeBranchPicker,
            WorkspaceId: workspaceId);

    public static CommandDescriptor WorktreeBranchSelect(string workspaceId, string branch) =>
        new($"{WorktreeBranchSelectPrefix}{workspaceId}.{branch}",
            CommandKind.WorktreeBranchSelect,
            WorkspaceId: workspaceId,
            Branch: branch);

    public static CommandDescriptor WorktreeBranchClear(string workspaceId) =>
        new($"{WorktreeBranchClearPrefix}{workspaceId}",
            CommandKind.WorktreeBranchClear,
            WorkspaceId: workspaceId);

    public static CommandDescriptor FavoriteToggle(string shortcutName) =>
        new($"{FavoriteTogglePrefix}{EncodeNameKey(shortcutName)}",
            CommandKind.FavoriteToggle);

    public static CommandDescriptor FavoriteMove(string shortcutId, string moveKind) =>
        new($"{FavoriteMovePrefix}{shortcutId}.{moveKind}",
            CommandKind.FavoriteMove);

    public static bool IsStableId(string key) =>
        key.Length == 32 && key.All(static c => Uri.IsHexDigit(c));

    public static bool TryDecodeLegacyNameKey(string key, out string name)
    {
        name = string.Empty;

        if (string.IsNullOrWhiteSpace(key) || IsStableId(key))
        {
            return false;
        }

        return TryDecodeHexUtf8(key, out name);
    }

    internal static string EncodeNameKey(string name) =>
        Convert.ToHexString(Encoding.UTF8.GetBytes(name)).ToLowerInvariant();

    internal static string EncodeDirectoryKey(string directory)
    {
        var normalized = directory.Trim().TrimEnd('\\', '/');
        try
        {
            normalized = Path.GetFullPath(normalized);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            // Keep trimmed input when full path normalization fails.
        }

        return EncodeNameKey(normalized);
    }

    internal static bool TryDecodeHexUtf8(string encoded, out string value)
    {
        value = string.Empty;

        try
        {
            value = Encoding.UTF8.GetString(Convert.FromHexString(encoded));
            return !string.IsNullOrEmpty(value);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            return false;
        }
    }

    private static string VariantSuffix(bool runAsAdmin, bool runAsStandard)
    {
        if (runAsAdmin && runAsStandard)
        {
            // Debug.Fail is the wrong tool here: xunit's test-host trace listener converts it
            // into a thrown DebugAssertException, but this branch is deliberately exercised by
            // tests to verify the graceful-degrade behavior (admin wins), not an actual bug.
            RepositoryDiagnostics.Report("CommandDescriptor.VariantSuffix", "both-variant-flags-set");
            runAsStandard = false;
        }

        return runAsAdmin ? AdminSuffix : runAsStandard ? StandardSuffix : string.Empty;
    }
}
