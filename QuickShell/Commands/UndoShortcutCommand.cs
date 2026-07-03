using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Services;

namespace QuickShell.Commands;

internal sealed partial class UndoShortcutCommand : InvokableCommand
{
    private readonly Action _onChanged;

    public UndoShortcutCommand(Action onChanged)
    {
        _onChanged = onChanged;
        Name = "Undo";
        Icon = new IconInfo("\uE7A7");
    }

    public override CommandResult Invoke()
    {
        if (!QuickShellRuntimeServices.Shortcuts.Undo())
        {
            return QuickShellNavigation.StayOpen("Nothing to undo.");
        }

        _onChanged();
        return QuickShellNavigation.StayOpen("Undid the last workspace change.");
    }
}
