using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Commands;
using QuickShell.Models;
using QuickShell.Pages;

namespace QuickShell.Services;

internal static class ShortcutListItems
{
    public static ListItem CreateOpen(
        QuickShellPageContext context,
        TerminalShortcut shortcut,
        Action? onChanged = null,
        PinnedMoveVisibility moveVisibility = default,
        bool includeEdit = true,
        Action? onFavoritesReordered = null,
        bool useHomePinContextMenu = false,
        bool? needsRepairOverride = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        var services = context.Services;
        var settings = context.Settings;
        const bool requireDirectoryExists = false;
        var needsRepair = needsRepairOverride ?? ShortcutHealth.WouldNeedRepair(shortcut, requireDirectoryExists);
        ICommand primaryCommand = needsRepair
            ? new ShortcutFormPage(services, shortcut, onChanged)
            : new OpenTerminalShortcutCommand(shortcut, services);

        var item = new ListItem(primaryCommand)
        {
            Title = shortcut.Name,
            Subtitle = ShortcutHealth.BuildListSubtitle(shortcut, requireDirectoryExists),
            Icon = new IconInfo(ShortcutHealth.GetListGlyph(shortcut, needsRepair)),
        };

        var tags = ShortcutDisplayTags.BuildTags(
            shortcut,
            settings.TerminalApplicationId,
            settings.DefaultProfileId,
            services.HealthChecker,
            services.GitOperations);
        if (tags is not null)
        {
            item.Tags = tags;
        }

        if (onChanged is not null)
        {
            item.MoreCommands = needsRepair
                ? ShortcutContextCommands.BuildRepairOnly(context, shortcut, onChanged)
                : useHomePinContextMenu
                    ? ShortcutContextCommands.BuildForHomePin(
                        context,
                        shortcut,
                        onChanged,
                        needsRepair,
                        moveVisibility)
                    : ShortcutContextCommands.Build(
                        context,
                        shortcut,
                        onChanged,
                        includeEdit,
                        moveVisibility,
                        onFavoritesReordered: onFavoritesReordered,
                        includePageCommands: true,
                        needsRepair: needsRepair);
        }

        return item;
    }

    public static ListItem CreateNewShortcut(CreateShortcutCommand command) =>
        new(command)
        {
            Title = Strings.CreateNewWorkspace_Title,
            Subtitle = Strings.CreateNewWorkspace_Subtitle,
        };
}
