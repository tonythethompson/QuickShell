using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Commands;
using QuickShell.Models;

namespace QuickShell.Services;

internal static class ShortcutListItems
{
    public static ListItem CreateOpen(
        TerminalShortcut shortcut,
        QuickShellSettingsManager settings,
        Action? onChanged = null,
        CreateShortcutCommand? createShortcutCommand = null)
    {
        var item = new ListItem(new OpenTerminalShortcutCommand(shortcut, settings))
        {
            Title = shortcut.Name,
            Subtitle = ShortcutDisplay.BuildSubtitle(shortcut),
        };

        var tags = ShortcutDisplay.BuildTags(shortcut);
        if (tags is not null)
        {
            item.Tags = tags;
        }

        if (onChanged is not null)
        {
            item.MoreCommands = ShortcutContextCommands.BuildForHomePin(
                shortcut,
                onChanged,
                settings,
                createShortcutCommand);
        }

        return item;
    }

    public static ListItem CreateNewShortcut(CreateShortcutCommand command) =>
        new(command)
        {
            Title = "Create new shortcut",
            Subtitle = "Directory and optional command",
        };
}
