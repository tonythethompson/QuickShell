using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Services;

namespace QuickShell.Commands;

internal sealed partial class RedoShortcutCommand : InvokableCommand
{
    private readonly Action _onChanged;

    public RedoShortcutCommand(Action onChanged)
    {
        _onChanged = onChanged;
        Name = Strings.Command_Redo_Name;
        Icon = new IconInfo("\uE7A6");
    }

    public override CommandResult Invoke()
    {
        if (!QuickShellRuntimeServices.Shortcuts.Redo())
        {
            return QuickShellNavigation.StayOpen(Strings.Redo_NothingToRedo);
        }

        _onChanged();
        return QuickShellNavigation.StayOpen(Strings.Redo_Confirmation);
    }
}
