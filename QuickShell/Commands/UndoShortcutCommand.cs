using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Services;

namespace QuickShell.Commands;

internal sealed partial class UndoShortcutCommand : InvokableCommand
{
    private readonly Action _onChanged;

    public UndoShortcutCommand(Action onChanged)
    {
        _onChanged = onChanged;
        Name = Strings.Command_Undo_Name;
        Icon = new IconInfo("\uE7A7");
    }

    public override CommandResult Invoke()
    {
        if (!QuickShellRuntimeServices.Shortcuts.Undo())
        {
            return QuickShellNavigation.StayOpen(Strings.Undo_NothingToUndo);
        }

        _onChanged();
        return QuickShellNavigation.StayOpen(Strings.Undo_Confirmation);
    }
}
