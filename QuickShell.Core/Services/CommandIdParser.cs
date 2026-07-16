namespace QuickShell.Services;

/// <summary>
/// Default command-ID parser for CmdPal deep links.
/// </summary>
internal sealed class CommandIdParser : ICommandIdParser
{
    public bool TryParse(string rawId, out CommandDescriptor descriptor)
    {
        descriptor = null!;

        if (string.IsNullOrWhiteSpace(rawId))
        {
            return false;
        }

        if (string.Equals(rawId, CommandDescriptor.SettingsId, StringComparison.Ordinal))
        {
            descriptor = new CommandDescriptor(rawId, CommandKind.OpenSettings);
            return true;
        }

        if (string.Equals(rawId, CommandDescriptor.ImportConflictId, StringComparison.Ordinal))
        {
            descriptor = new CommandDescriptor(rawId, CommandKind.ImportConflict);
            return true;
        }

        if (string.Equals(rawId, CommandDescriptor.PendingShortcutEditId, StringComparison.Ordinal))
        {
            descriptor = new CommandDescriptor(rawId, CommandKind.PendingShortcutEdit);
            return true;
        }

        if (string.Equals(rawId, CommandDescriptor.CreateWorkspaceId, StringComparison.Ordinal))
        {
            descriptor = new CommandDescriptor(rawId, CommandKind.CreateWorkspace);
            return true;
        }

        if (TryDecodeDiscoverCreateDirectory(rawId, out var discoverDirectory)
            && !string.IsNullOrWhiteSpace(discoverDirectory))
        {
            descriptor = new CommandDescriptor(
                rawId,
                CommandKind.DiscoverCreateWorkspace,
                Directory: discoverDirectory);
            return true;
        }

        if (string.Equals(rawId, CommandDescriptor.DiscoverGitReposId, StringComparison.Ordinal))
        {
            descriptor = new CommandDescriptor(rawId, CommandKind.DiscoverGitRepos);
            return true;
        }

        if (TryParseWorktreeBranchClear(rawId, out var clearShortcutId))
        {
            descriptor = new CommandDescriptor(
                rawId,
                CommandKind.WorktreeBranchClear,
                WorkspaceId: clearShortcutId);
            return true;
        }

        if (TryParseWorktreeBranchSelect(rawId, out var selectShortcutId, out var branch))
        {
            descriptor = new CommandDescriptor(
                rawId,
                CommandKind.WorktreeBranchSelect,
                WorkspaceId: selectShortcutId,
                Branch: branch);
            return true;
        }

        if (TryParseWorktreeBranchPicker(rawId, out var pickerShortcutId))
        {
            descriptor = new CommandDescriptor(
                rawId,
                CommandKind.WorktreeBranchPicker,
                WorkspaceId: pickerShortcutId);
            return true;
        }

        if (TryParseWorkspaceStatus(rawId, out var statusShortcutId))
        {
            descriptor = new CommandDescriptor(
                rawId,
                CommandKind.WorkspaceStatus,
                WorkspaceId: statusShortcutId);
            return true;
        }

        // OpenLaunch must be checked before Open (Open's key would otherwise swallow ".launch.").
        if (TryParseOpenLaunch(rawId, out var shortcutId, out var launchId))
        {
            descriptor = new CommandDescriptor(
                rawId,
                CommandKind.OpenLaunch,
                WorkspaceId: shortcutId,
                LaunchId: launchId);
            return true;
        }

        if (TryParseOpen(rawId, out var openKey))
        {
            descriptor = new CommandDescriptor(
                rawId,
                CommandKind.OpenWorkspace,
                WorkspaceId: openKey);
            return true;
        }

        return false;
    }

    private static bool TryDecodeDiscoverCreateDirectory(string commandId, out string directory)
    {
        directory = string.Empty;

        if (!commandId.StartsWith(CommandDescriptor.DiscoverCreatePrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var directoryKey = commandId[CommandDescriptor.DiscoverCreatePrefix.Length..];
        return CommandDescriptor.TryDecodeHexUtf8(directoryKey, out directory);
    }

    private static bool TryParseOpen(string commandId, out string key)
    {
        key = string.Empty;

        if (!commandId.StartsWith(CommandDescriptor.OpenPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        key = commandId[CommandDescriptor.OpenPrefix.Length..];
        if (key.EndsWith(".admin", StringComparison.Ordinal))
        {
            key = key[..^".admin".Length];
        }
        else if (key.EndsWith(".standard", StringComparison.Ordinal))
        {
            key = key[..^".standard".Length];
        }

        return !string.IsNullOrWhiteSpace(key);
    }

    private static bool TryParseOpenLaunch(string commandId, out string shortcutId, out string launchId)
    {
        shortcutId = string.Empty;
        launchId = string.Empty;

        if (!commandId.StartsWith(CommandDescriptor.OpenPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var value = commandId[CommandDescriptor.OpenPrefix.Length..];
        var launchSeparatorIndex = value.IndexOf(CommandDescriptor.LaunchSeparator, StringComparison.Ordinal);
        if (launchSeparatorIndex <= 0)
        {
            return false;
        }

        shortcutId = value[..launchSeparatorIndex];
        launchId = value[(launchSeparatorIndex + CommandDescriptor.LaunchSeparator.Length)..];
        if (launchId.EndsWith(".admin", StringComparison.Ordinal))
        {
            launchId = launchId[..^".admin".Length];
        }
        else if (launchId.EndsWith(".standard", StringComparison.Ordinal))
        {
            launchId = launchId[..^".standard".Length];
        }

        return CommandDescriptor.IsStableId(shortcutId) && CommandDescriptor.IsStableId(launchId);
    }

    private static bool TryParseWorkspaceStatus(string commandId, out string shortcutId)
    {
        shortcutId = string.Empty;

        if (!commandId.StartsWith(CommandDescriptor.WorkspaceStatusPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        shortcutId = commandId[CommandDescriptor.WorkspaceStatusPrefix.Length..];
        return CommandDescriptor.IsStableId(shortcutId);
    }

    private static bool TryParseWorktreeBranchPicker(string commandId, out string shortcutId)
    {
        shortcutId = string.Empty;

        if (!commandId.StartsWith(CommandDescriptor.WorktreeBranchPickerPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        shortcutId = commandId[CommandDescriptor.WorktreeBranchPickerPrefix.Length..];
        return CommandDescriptor.IsStableId(shortcutId);
    }

    private static bool TryParseWorktreeBranchClear(string commandId, out string shortcutId)
    {
        shortcutId = string.Empty;

        if (!commandId.StartsWith(CommandDescriptor.WorktreeBranchClearPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        shortcutId = commandId[CommandDescriptor.WorktreeBranchClearPrefix.Length..];
        return CommandDescriptor.IsStableId(shortcutId);
    }

    private static bool TryParseWorktreeBranchSelect(string commandId, out string shortcutId, out string branch)
    {
        shortcutId = string.Empty;
        branch = string.Empty;

        if (!commandId.StartsWith(CommandDescriptor.WorktreeBranchSelectPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var remainder = commandId[CommandDescriptor.WorktreeBranchSelectPrefix.Length..];
        var separatorIndex = remainder.IndexOf('.', StringComparison.Ordinal);
        if (separatorIndex <= 0)
        {
            return false;
        }

        shortcutId = remainder[..separatorIndex];
        branch = remainder[(separatorIndex + 1)..];
        return CommandDescriptor.IsStableId(shortcutId) && !string.IsNullOrWhiteSpace(branch);
    }
}
