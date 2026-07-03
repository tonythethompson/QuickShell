using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Services;

namespace QuickShell.Commands;

internal sealed partial class RedoShortcutCommand : InvokableCommand
{
    private readonly Action _onChanged;

    public RedoShortcutCommand(Action onChanged)
    {
        _onChanged = onChanged;
        Name = "Redo";
        Icon = new IconInfo("\uE7A6");
    }

    public override CommandResult Invoke()
    {
        if (!QuickShellRuntimeServices.Shortcuts.Redo())
        {
            return QuickShellNavigation.StayOpen("Nothing to redo.");
        }

        _onChanged();
        return QuickShellNavigation.StayOpen("Redid the last workspace change.");
    }
}
