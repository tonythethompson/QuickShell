using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using TerminalShortcuts.Models;
using TerminalShortcuts.Services;

namespace TerminalShortcuts.Commands;

internal sealed partial class OpenTerminalShortcutCommand : InvokableCommand
{
    private readonly TerminalShortcut _shortcut;

    public OpenTerminalShortcutCommand(TerminalShortcut shortcut)
    {
        _shortcut = shortcut;
        Name = shortcut.Name;
        Icon = new IconInfo("\uE756");
    }

    public override CommandResult Invoke()
    {
        try
        {
            TerminalLauncher.Open(_shortcut);
            return CommandResult.Dismiss();
        }
        catch (Exception ex)
        {
            return CommandResult.ShowToast($"Failed to open terminal: {ex.Message}");
        }
    }
}
