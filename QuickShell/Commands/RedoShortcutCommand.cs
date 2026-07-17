using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Services;

namespace QuickShell.Commands;

internal sealed partial class RedoShortcutCommand : InvokableCommand
{
    private readonly IQuickShellServices _services;
    private readonly Action _onChanged;

    public RedoShortcutCommand(Action onChanged, IQuickShellServices services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _onChanged = onChanged;
        Name = Strings.Command_Redo_Name;
        Icon = new IconInfo("\uE7A6");
    }

    public override CommandResult Invoke()
    {
        if (!_services.Shortcuts.Redo())
        {
            return QuickShellNavigation.StayOpen(Strings.Redo_NothingToRedo);
        }

        _onChanged();
        return QuickShellNavigation.StayOpen(Strings.Redo_Confirmation);
    }
}
