using QuickShell.Models;
using QuickShell.Services;

namespace QuickShell.Abstractions;

internal interface IShortcutLaunchExecutor
{
    ShortcutLaunchResult Launch(
        TerminalShortcut shortcut,
        string terminalApplicationId,
        string defaultProfileId,
        ShortcutLaunchOptions options = default);

    ShortcutLaunchResult LaunchEntry(
        TerminalShortcut shortcut,
        WorkspaceEntry launch,
        string terminalApplicationId,
        string defaultProfileId,
        ShortcutLaunchOptions options = default);
}
