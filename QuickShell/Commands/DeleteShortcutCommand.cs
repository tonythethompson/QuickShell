using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Services;

namespace QuickShell.Commands;

internal sealed partial class DeleteShortcutCommand : InvokableCommand
{
    private readonly string _name;
    private readonly Action _onDeleted;

    public DeleteShortcutCommand(string name, Action onDeleted)
    {
        _name = name;
        _onDeleted = onDeleted;
        Name = "Delete";
        Icon = new IconInfo("\uE74D");
    }

    public override CommandResult Invoke()
    {
        var deleted = QuickShellRuntimeServices.Shortcuts.Delete(_name);
        if (deleted)
        {
            _onDeleted();
            return QuickShellNavigation.StayOpen($"Deleted workspace '{_name}'.");
        }

        return QuickShellNavigation.StayOpen($"Workspace '{_name}' was not found.");
    }
}
