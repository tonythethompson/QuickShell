using QuickShell.Models;

namespace QuickShell.Services;

internal static class WorkspaceDevServerActions
{
    public static bool ShouldOpenOnWorkspaceLaunch(TerminalShortcut shortcut) =>
        shortcut.OpenDevServerOnLaunch && !string.IsNullOrWhiteSpace(shortcut.DevServerUrl);

    public static bool TryOpen(TerminalShortcut shortcut, out string error) =>
        WorkspaceLinkActions.TryOpenLink(shortcut.DevServerUrl, out error);
}
