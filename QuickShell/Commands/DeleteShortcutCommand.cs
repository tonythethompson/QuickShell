using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Services;

namespace QuickShell.Commands;

internal sealed partial class DeleteShortcutCommand : InvokableCommand
{
    private readonly IQuickShellServices _services;
    private readonly string _name;
    private readonly Action _onDeleted;

    public DeleteShortcutCommand(
        string name,
        Action onDeleted,
        IQuickShellServices services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _name = name;
        _onDeleted = onDeleted;
        Name = Strings.Command_Delete_Name;
        Icon = new IconInfo("\uE74D");
    }

    public override CommandResult Invoke()
    {
        var deleted = _services.Shortcuts.Delete(_name);
        if (deleted)
        {
            _onDeleted();
            return QuickShellNavigation.StayOpen(Strings.DeletedWorkspaceConfirmedFormat(_name));
        }

        return QuickShellNavigation.StayOpen(Strings.WorkspaceNotFoundNamedFormat(_name));
    }
}
