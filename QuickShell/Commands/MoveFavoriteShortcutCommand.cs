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
    private readonly string _id;
    private readonly string _name;
    private readonly FavoriteMoveKind _move;
    private readonly Action _onChanged;

    public MoveFavoriteShortcutCommand(
        string id,
        string name,
        FavoriteMoveKind move,
        Action onChanged)
    {
        _id = id ?? string.Empty;
        _name = name;
        _move = move;
        _onChanged = onChanged;
        Id = ShortcutCommandIds.FavoriteMove(id, move.ToString());

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
        var moved = TryMove();

        // Rebuild list immediately so favorites order / menus repaint (deferred Reload left the UI stuck).
        try
        {
            _onChanged();
        }
        catch
        {
            // Best-effort UI refresh; repository already applied a successful move.
        }

        if (!moved)
        {
            return QuickShellNavigation.StayOpen("Favorite cannot be moved further.");
        }

        return QuickShellNavigation.StayOpen(_move switch
        {
            FavoriteMoveKind.Up => $"Moved '{_name}' up in favorites.",
            FavoriteMoveKind.Down => $"Moved '{_name}' down in favorites.",
            FavoriteMoveKind.ToTop => $"Moved '{_name}' to the top of favorites.",
            FavoriteMoveKind.ToBottom => $"Moved '{_name}' to the bottom of favorites.",
            _ => $"Moved '{_name}' in favorites.",
        });
    }

    private bool TryMove()
    {
        var repo = QuickShellServices.Current.Shortcuts;
        if (!string.IsNullOrWhiteSpace(_id))
        {
            return _move switch
            {
                FavoriteMoveKind.Up => repo.MovePinnedById(_id, -1),
                FavoriteMoveKind.Down => repo.MovePinnedById(_id, +1),
                FavoriteMoveKind.ToTop => repo.MovePinnedToEdgeById(_id, toTop: true),
                FavoriteMoveKind.ToBottom => repo.MovePinnedToEdgeById(_id, toTop: false),
                _ => false,
            };
        }

        // Fallback for callers without an id.
        return _move switch
        {
            FavoriteMoveKind.Up => repo.MovePinned(_name, -1),
            FavoriteMoveKind.Down => repo.MovePinned(_name, +1),
            FavoriteMoveKind.ToTop => repo.MovePinnedToEdge(_name, toTop: true),
            FavoriteMoveKind.ToBottom => repo.MovePinnedToEdge(_name, toTop: false),
            _ => false,
        };
    }
}
