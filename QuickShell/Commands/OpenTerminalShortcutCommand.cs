using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Models;
using QuickShell.Services;

namespace QuickShell.Commands;

internal sealed partial class OpenTerminalShortcutCommand : InvokableCommand
{
    private readonly TerminalShortcut _shortcut;
    private readonly bool _runAsAdmin;

    public OpenTerminalShortcutCommand(TerminalShortcut shortcut, bool runAsAdmin = false)
    {
        _shortcut = shortcut;
        _runAsAdmin = runAsAdmin;
        Name = shortcut.Name;
        Icon = new IconInfo(runAsAdmin || shortcut.RunAsAdmin ? "\uE946" : "\uE756");
    }

    public override CommandResult Invoke()
    {
        try
        {
            TerminalLauncher.Open(_shortcut, _runAsAdmin);
            return CommandResult.Dismiss();
        }
        catch (Exception ex)
        {
            return CommandResult.ShowToast($"Failed to open terminal: {ex.Message}");
        }
    }
}
