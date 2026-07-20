using QuickShell.Models;

namespace QuickShell.Services;

internal static class WorkspaceDevServerActions
{
    private static readonly AsyncLocal<Func<TerminalShortcut, bool>?> OverrideLocal = new();
    private static readonly AsyncLocal<bool> LastOpenAttemptedLocal = new();

    /// <summary>
    /// Test seam: AsyncLocal so parallel tests do not share override state.
    /// </summary>
    internal static Func<TerminalShortcut, bool>? TryOpenOverride
    {
        get => OverrideLocal.Value;
        set => OverrideLocal.Value = value;
    }

    internal static bool LastOpenAttempted
    {
        get => LastOpenAttemptedLocal.Value;
        private set => LastOpenAttemptedLocal.Value = value;
    }

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
