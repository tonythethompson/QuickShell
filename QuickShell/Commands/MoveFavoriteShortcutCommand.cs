using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Services;

namespace QuickShell.Commands;

internal enum FavoriteMoveKind
{
    Up,
    Down,
    ToTop,
    ToBottom,
}

internal sealed partial class MoveFavoriteShortcutCommand : InvokableCommand
{
    private readonly string _name;
    private readonly FavoriteMoveKind _move;
    private readonly Action _onChanged;

    public MoveFavoriteShortcutCommand(string name, FavoriteMoveKind move, Action onChanged)
    {
        _name = name;
        _move = move;
        _onChanged = onChanged;

        Name = move switch
        {
            FavoriteMoveKind.Up => "Move up",
            FavoriteMoveKind.Down => "Move down",
            FavoriteMoveKind.ToTop => "Move to top",
            FavoriteMoveKind.ToBottom => "Move to bottom",
            _ => "Move",
        };

        Icon = new IconInfo(move switch
        {
            FavoriteMoveKind.Up => "\uE70E",
            FavoriteMoveKind.Down => "\uE70D",
            FavoriteMoveKind.ToTop => "\uE74A",
            FavoriteMoveKind.ToBottom => "\uE74B",
            _ => "\uE70E",
        });
    }

    public override CommandResult Invoke()
    {
        var moved = _move switch
        {
            FavoriteMoveKind.Up => QuickShellRuntimeServices.Shortcuts.MovePinned(_name, -1),
            FavoriteMoveKind.Down => QuickShellRuntimeServices.Shortcuts.MovePinned(_name, +1),
            FavoriteMoveKind.ToTop => QuickShellRuntimeServices.Shortcuts.MovePinnedToEdge(_name, toTop: true),
            FavoriteMoveKind.ToBottom => QuickShellRuntimeServices.Shortcuts.MovePinnedToEdge(_name, toTop: false),
            _ => false,
        };

        if (!moved)
        {
            return QuickShellNavigation.StayOpen("Favorite cannot be moved further.");
        }

        _onChanged();
        return QuickShellNavigation.StayOpen(_move switch
        {
            FavoriteMoveKind.Up => $"Moved '{_name}' up in favorites.",
            FavoriteMoveKind.Down => $"Moved '{_name}' down in favorites.",
            FavoriteMoveKind.ToTop => $"Moved '{_name}' to the top of favorites.",
            FavoriteMoveKind.ToBottom => $"Moved '{_name}' to the bottom of favorites.",
            _ => $"Moved '{_name}' in favorites.",
        });
    }
}
