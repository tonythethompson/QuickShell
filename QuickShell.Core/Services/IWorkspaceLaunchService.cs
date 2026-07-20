using QuickShell.Models;

namespace QuickShell.Services;

/// <summary>
/// Authoritative launch boundary. Callers provide an ID; the service reloads the
/// current repository-owned workspace and authorizes the requested effect before
/// handing the approved content to the terminal/companion/url launchers.
/// </summary>
internal interface IWorkspaceLaunchService
{
    ShortcutLaunchResult Launch(
        string workspaceId,
        string terminalApplicationId,
        string defaultProfileId,
        ShortcutLaunchOptions options = default);

    ShortcutLaunchResult LaunchEntry(
        string workspaceId,
        string launchId,
        string terminalApplicationId,
        string defaultProfileId,
        ShortcutLaunchOptions options = default);

    WorkspaceAuthorizationResult Authorize(string workspaceId, WorkspaceAction action);
}

