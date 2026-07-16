using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Services;

namespace QuickShell.Commands;

internal sealed partial class ToggleFavoriteShortcutCommand : InvokableCommand
{
    private readonly IQuickShellServices _services;
    private readonly string _name;
    private readonly Action _onChanged;

    public ToggleFavoriteShortcutCommand(
        string name,
        Action onChanged,
        bool isFavorite,
        IQuickShellServices? services = null)
    {
        _services = services ?? throw new InvalidOperationException("IQuickShellServices is required.");
        _name = name;
        _onChanged = onChanged;
        Id = ShortcutCommandIds.FavoriteToggle(name);
        Name = isFavorite ? Strings.Command_Unfavorite_Name : Strings.Command_Favorite_Name;
        Icon = new IconInfo(isFavorite ? ShortcutGlyphs.FavoriteFilled : ShortcutGlyphs.FavoriteOutline);
    }

    public override CommandResult Invoke()
    {
        var favorited = _services.Shortcuts.TogglePinned(_name);
        _onChanged();
        return QuickShellNavigation.StayOpen(
            favorited ? $"Favorited '{_name}'." : $"Removed '{_name}' from favorites.");
    }
}
