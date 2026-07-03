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
        CreateShortcutCommand? createShortcutCommand = null)
    {
        var needsRepair = ShortcutHealth.NeedsRepair(shortcut);
        ICommand primaryCommand = needsRepair
            ? new ShortcutFormPage(shortcut, onChanged)
            : new OpenTerminalShortcutCommand(shortcut, settings);

        var item = new ListItem(primaryCommand)
        {
            Title = shortcut.Name,
            Subtitle = ShortcutHealth.BuildListSubtitle(shortcut),
            Icon = new IconInfo(ShortcutHealth.GetListGlyph(shortcut)),
        };

        var tags = ShortcutDisplayTags.BuildTags(shortcut);
        if (tags is not null)
        {
            item.Tags = tags;
        }

        if (onChanged is not null)
        {
            item.MoreCommands = needsRepair
                ? ShortcutContextCommands.BuildRepairOnly(shortcut, onChanged)
                : createShortcutCommand is not null
                    ? ShortcutContextCommands.BuildForHomePin(
                        shortcut,
                        onChanged,
                        settings,
                        createShortcutCommand)
                    : item.MoreCommands;
        }

        return item;
    }

    public static ListItem CreateNewShortcut(CreateShortcutCommand command) =>
        new(command)
        {
            Title = "Create new workspace",
            Subtitle = "Directory and optional command",
        };
}
