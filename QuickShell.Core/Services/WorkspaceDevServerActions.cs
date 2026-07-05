using QuickShell.Models;

namespace QuickShell.Services;

internal static class WorkspaceDevServerActions
{
    internal static Func<TerminalShortcut, bool>? TryOpenOverride { get; set; }

    internal static bool LastOpenAttempted { get; private set; }

    public static bool ShouldOpenOnWorkspaceLaunch(TerminalShortcut shortcut) =>
        shortcut.OpenDevServerOnLaunch && !string.IsNullOrWhiteSpace(shortcut.DevServerUrl);

    public static bool TryOpen(TerminalShortcut shortcut, out string error)
    {
        error = string.Empty;
        LastOpenAttempted = false;

        if (TryOpenOverride is { } tryOpenOverride)
        {
            LastOpenAttempted = true;
            error = string.Empty;
            return tryOpenOverride(shortcut);
        }

        return WorkspaceLinkActions.TryOpenLink(shortcut.DevServerUrl, out error);
    }
}
