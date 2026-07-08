namespace QuickShell.Services;

/// <summary>
/// Default command-ID parser; delegates payload extraction to <see cref="ShortcutCommandIds"/>.
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

        if (string.Equals(rawId, QuickShellDeepLinkIds.Settings, StringComparison.Ordinal)
            || string.Equals(rawId, QuickShellDeepLinkIds.ImportConflict, StringComparison.Ordinal)
            || string.Equals(rawId, QuickShellDeepLinkIds.PendingShortcutEdit, StringComparison.Ordinal))
        {
            descriptor = new CommandDescriptor(rawId, CommandKind.OpenSettings);
            return true;
        }

        if (string.Equals(rawId, ShortcutCommandIds.CreateShortcut, StringComparison.Ordinal))
        {
            descriptor = new CommandDescriptor(rawId, CommandKind.CreateWorkspace);
            return true;
        }

        if (ShortcutCommandIds.TryDecodeDiscoverCreateDirectory(rawId, out var discoverDirectory)
            && !string.IsNullOrWhiteSpace(discoverDirectory))
        {
            descriptor = new CommandDescriptor(
                rawId,
                CommandKind.DiscoverCreateWorkspace,
                Directory: discoverDirectory);
            return true;
        }

        if (string.Equals(rawId, QuickShellDeepLinkIds.DiscoverGitRepos, StringComparison.Ordinal))
        {
            descriptor = new CommandDescriptor(rawId, CommandKind.DiscoverGitRepos);
            return true;
        }

        // OpenLaunch must be checked before Open (Open's key would otherwise swallow ".launch.").
        if (ShortcutCommandIds.TryParseOpenLaunch(rawId, out var shortcutId, out var launchId))
        {
            descriptor = new CommandDescriptor(
                rawId,
                CommandKind.OpenLaunch,
                WorkspaceId: shortcutId,
                LaunchId: launchId);
            return true;
        }

        if (ShortcutCommandIds.TryParseOpen(rawId, out var openKey))
        {
            descriptor = new CommandDescriptor(
                rawId,
                CommandKind.OpenWorkspace,
                WorkspaceId: openKey);
            return true;
        }

        return false;
    }
}
