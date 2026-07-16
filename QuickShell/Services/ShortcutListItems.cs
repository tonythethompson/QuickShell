using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Commands;
using QuickShell.Models;
using QuickShell.Pages;

namespace QuickShell.Services;

internal static class ShortcutListItems
{
    public static ListItem CreateOpen(
        TerminalShortcut shortcut,
        QuickShellSettingsManager settings,
        Action? onChanged = null,
        CreateShortcutCommand? createShortcutCommand = null,
        PinnedMoveVisibility moveVisibility = default,
        bool includeEdit = true,
        Action? onFavoritesReordered = null,
        bool useHomePinContextMenu = false,
        IQuickShellServices? services = null)
    {
        const bool requireDirectoryExists = false;
        var needsRepair = ShortcutHealth.WouldNeedRepair(shortcut, requireDirectoryExists);
        ICommand primaryCommand = needsRepair
            ? new ShortcutFormPage(shortcut, onChanged, services: services)
            : new OpenTerminalShortcutCommand(shortcut, settings, services: services);

        var item = new ListItem(primaryCommand)
        {
            Title = shortcut.Name,
            Subtitle = ShortcutHealth.BuildListSubtitle(shortcut, requireDirectoryExists),
            Icon = new IconInfo(ShortcutHealth.GetListGlyph(shortcut, needsRepair)),
        };

        var tags = ShortcutDisplayTags.BuildTags(
            shortcut,
            settings.TerminalApplicationId,
            settings.DefaultProfileId);
        if (tags is not null)
        {
            item.Tags = tags;
        }

        if (onChanged is not null)
        {
            item.MoreCommands = needsRepair
                ? ShortcutContextCommands.BuildRepairOnly(shortcut, onChanged, settings, services)
                : useHomePinContextMenu
                    ? ShortcutContextCommands.BuildForHomePin(
                        shortcut,
                        onChanged,
                        settings,
                        createShortcutCommand,
                        needsRepair,
                        moveVisibility,
                        services)
                    : ShortcutContextCommands.Build(
                        shortcut,
                        onChanged,
                        settings,
                        createShortcutCommand,
                        includeEdit,
                        moveVisibility,
                        onFavoritesReordered,
                        services: services);
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
