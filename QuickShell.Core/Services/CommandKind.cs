namespace QuickShell.Services;

/// <summary>
/// Categories for deep-link command IDs handled by <c>GetCommandItem</c>.
/// </summary>
internal enum CommandKind
{
    OpenSettings,
    CreateWorkspace,
    DiscoverCreateWorkspace,
    DiscoverGitRepos,
    OpenLaunch,
    OpenWorkspace,
}
