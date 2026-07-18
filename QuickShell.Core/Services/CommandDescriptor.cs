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
    // Provider and page IDs that are not parsed as deep-links.
    public const string ProviderId = "com.quickshell";
    public const string HomePageId = "com.quickshell.home";
    public const string FallbackCommandId = "com.quickshell.fallback";

    // Well-known deep-link IDs.
    public const string SettingsId = "com.quickshell.settings";
    public const string ImportConflictId = "com.quickshell.import-conflict";
    public const string PendingShortcutEditId = "com.quickshell.pending-shortcut-edit";
    public const string DiscoverGitReposId = "com.quickshell.discover-git-repos";
    public const string CreateWorkspaceId = "com.quickshell.shortcut-form.create";

    // Deep-link prefixes.
    public const string OpenPrefix = "com.quickshell.shortcut.open.";
    public const string LaunchSeparator = ".launch.";
    public const string DiscoverCreatePrefix = "com.quickshell.discover.create.";
    public const string WorkspaceStatusPrefix = "com.quickshell.workspace-status.";
    public const string WorktreeBranchPickerPrefix = "com.quickshell.worktree-branch.picker.page.";
    public const string WorktreeBranchSelectPrefix = "com.quickshell.worktree-branch.select.";
    public const string WorktreeBranchClearPrefix = "com.quickshell.worktree-branch.clear.";

    // In-page command IDs (not parsed as deep-links).
    public const string FavoriteTogglePrefix = "com.quickshell.shortcut.favorite.";
    public const string FavoriteMovePrefix = "com.quickshell.shortcut.move.";

    // Form page ID prefixes (not parsed as deep-links).
    public const string NewWorkspaceFormPagePrefix = "com.quickshell.shortcut-form.create.";
    public const string EditWorkspaceFormPagePrefix = "com.quickshell.shortcut-form.edit.";
    public const string DuplicateWorkspaceFormPagePrefix = "com.quickshell.shortcut-form.duplicate.";
    public const string ShortcutDetailsPagePrefix = "com.quickshell.shortcut.details.";

    internal const string AdminSuffix = ".admin";
    internal const string StandardSuffix = ".standard";

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

    // Page ID factories (not deep-link descriptors).
    public static string NewWorkspaceFormPageId() => $"{NewWorkspaceFormPagePrefix}{Guid.NewGuid():N}";

    public static string EditWorkspaceFormPageId(string workspaceId) => $"{EditWorkspaceFormPagePrefix}{workspaceId}";

    public static string DuplicateWorkspaceFormPageId() => $"{DuplicateWorkspaceFormPagePrefix}{Guid.NewGuid():N}";

    public static string ShortcutDetailsPageId() => $"{ShortcutDetailsPagePrefix}{Guid.NewGuid():N}";

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

    /// <summary>
    /// Parses CmdPal deep-link IDs into typed descriptors. All parsing rules, precedence,
    /// and decoding helpers live here so the schema is defined in a single place.
    /// </summary>
    public static class Parser
    {
        public static bool TryParse(string rawId, out CommandDescriptor descriptor)
        {
            descriptor = null!;

            if (string.IsNullOrWhiteSpace(rawId))
            {
                return false;
            }

            if (TryParseWellKnown(rawId, out descriptor)
                || TryParseDiscoverCreate(rawId, out descriptor)
                || TryParseWorktreeBranchClear(rawId, out descriptor)
                || TryParseWorktreeBranchSelect(rawId, out descriptor)
                || TryParseWorktreeBranchPicker(rawId, out descriptor)
                || TryParseWorkspaceStatus(rawId, out descriptor)
                || TryParseOpenLaunch(rawId, out descriptor)
                || TryParseOpen(rawId, out descriptor))
            {
                return true;
            }

            return false;
        }

        private static bool TryParseWellKnown(string rawId, out CommandDescriptor descriptor)
        {
            descriptor = null!;

            if (string.Equals(rawId, SettingsId, StringComparison.Ordinal))
            {
                descriptor = new CommandDescriptor(rawId, CommandKind.OpenSettings);
                return true;
            }

            if (string.Equals(rawId, ImportConflictId, StringComparison.Ordinal))
            {
                descriptor = new CommandDescriptor(rawId, CommandKind.ImportConflict);
                return true;
            }

            if (string.Equals(rawId, PendingShortcutEditId, StringComparison.Ordinal))
            {
                descriptor = new CommandDescriptor(rawId, CommandKind.PendingShortcutEdit);
                return true;
            }

            if (string.Equals(rawId, CreateWorkspaceId, StringComparison.Ordinal))
            {
                descriptor = new CommandDescriptor(rawId, CommandKind.CreateWorkspace);
                return true;
            }

            if (string.Equals(rawId, DiscoverGitReposId, StringComparison.Ordinal))
            {
                descriptor = new CommandDescriptor(rawId, CommandKind.DiscoverGitRepos);
                return true;
            }

            return false;
        }

        private static bool TryParseDiscoverCreate(string rawId, out CommandDescriptor descriptor)
        {
            descriptor = null!;

            if (!rawId.StartsWith(DiscoverCreatePrefix, StringComparison.Ordinal))
            {
                return false;
            }

            var directoryKey = rawId[DiscoverCreatePrefix.Length..];
            if (!TryDecodeHexUtf8(directoryKey, out var directory) || string.IsNullOrWhiteSpace(directory))
            {
                return false;
            }

            descriptor = new CommandDescriptor(rawId, CommandKind.DiscoverCreateWorkspace, Directory: directory);
            return true;
        }

        private static bool TryParseOpen(string rawId, out CommandDescriptor descriptor)
        {
            descriptor = null!;

            if (!rawId.StartsWith(OpenPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            var key = StripVariantSuffix(rawId[OpenPrefix.Length..]);
            if (string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            descriptor = new CommandDescriptor(rawId, CommandKind.OpenWorkspace, WorkspaceId: key);
            return true;
        }

        private static bool TryParseOpenLaunch(string rawId, out CommandDescriptor descriptor)
        {
            descriptor = null!;

            if (!rawId.StartsWith(OpenPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            var value = rawId[OpenPrefix.Length..];
            var launchSeparatorIndex = value.IndexOf(LaunchSeparator, StringComparison.Ordinal);
            if (launchSeparatorIndex <= 0)
            {
                return false;
            }

            var shortcutId = value[..launchSeparatorIndex];
            var launchId = StripVariantSuffix(value[(launchSeparatorIndex + LaunchSeparator.Length)..]);

            if (!IsStableId(shortcutId) || !IsStableId(launchId))
            {
                return false;
            }

            descriptor = new CommandDescriptor(rawId, CommandKind.OpenLaunch, WorkspaceId: shortcutId, LaunchId: launchId);
            return true;
        }

        private static bool TryParseWorkspaceStatus(string rawId, out CommandDescriptor descriptor)
        {
            descriptor = null!;

            if (!rawId.StartsWith(WorkspaceStatusPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            var shortcutId = rawId[WorkspaceStatusPrefix.Length..];
            if (!IsStableId(shortcutId))
            {
                return false;
            }

            descriptor = new CommandDescriptor(rawId, CommandKind.WorkspaceStatus, WorkspaceId: shortcutId);
            return true;
        }

        private static bool TryParseWorktreeBranchPicker(string rawId, out CommandDescriptor descriptor)
        {
            descriptor = null!;

            if (!rawId.StartsWith(WorktreeBranchPickerPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            var shortcutId = rawId[WorktreeBranchPickerPrefix.Length..];
            if (!IsStableId(shortcutId))
            {
                return false;
            }

            descriptor = new CommandDescriptor(rawId, CommandKind.WorktreeBranchPicker, WorkspaceId: shortcutId);
            return true;
        }

        private static bool TryParseWorktreeBranchClear(string rawId, out CommandDescriptor descriptor)
        {
            descriptor = null!;

            if (!rawId.StartsWith(WorktreeBranchClearPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            var shortcutId = rawId[WorktreeBranchClearPrefix.Length..];
            if (!IsStableId(shortcutId))
            {
                return false;
            }

            descriptor = new CommandDescriptor(rawId, CommandKind.WorktreeBranchClear, WorkspaceId: shortcutId);
            return true;
        }

        private static bool TryParseWorktreeBranchSelect(string rawId, out CommandDescriptor descriptor)
        {
            descriptor = null!;

            if (!rawId.StartsWith(WorktreeBranchSelectPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            var remainder = rawId[WorktreeBranchSelectPrefix.Length..];
            var separatorIndex = remainder.IndexOf('.', StringComparison.Ordinal);
            if (separatorIndex <= 0)
            {
                return false;
            }

            var shortcutId = remainder[..separatorIndex];
            var branch = remainder[(separatorIndex + 1)..];

            if (!IsStableId(shortcutId) || string.IsNullOrWhiteSpace(branch))
            {
                return false;
            }

            descriptor = new CommandDescriptor(rawId, CommandKind.WorktreeBranchSelect, WorkspaceId: shortcutId, Branch: branch);
            return true;
        }
    }

    private static string StripVariantSuffix(string value)
    {
        if (value.EndsWith(AdminSuffix, StringComparison.Ordinal))
        {
            return value[..^AdminSuffix.Length];
        }

        if (value.EndsWith(StandardSuffix, StringComparison.Ordinal))
        {
            return value[..^StandardSuffix.Length];
        }

        return value;
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
