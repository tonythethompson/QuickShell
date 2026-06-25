using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Services;

namespace QuickShell.Commands;

internal sealed partial class TogglePinShortcutCommand : InvokableCommand
{
    private readonly string _name;
    private readonly Action _onChanged;

    public TogglePinShortcutCommand(string name, Action onChanged, bool isPinned)
    {
        _name = name;
        _onChanged = onChanged;
        Name = isPinned ? "Unfavorite" : "Favorite";
        Icon = new IconInfo(isPinned ? "\uE735" : "\uE734");
    }

    public override CommandResult Invoke()
    {
        var favorited = ShortcutStore.TogglePinned(_name);
        _onChanged();
        return QuickShellNavigation.StayOpen(favorited ? $"Favorited '{_name}'." : $"Removed '{_name}' from favorites.");
    }
}
