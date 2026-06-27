using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Commands;

namespace QuickShell.Services;

internal static class QuickShellPageActions
{
    public static IEnumerable<IListItem> BuildItems(
        CreateShortcutCommand createShortcutCommand,
        QuickShellSettingsManager settings,
        Action onReload)
    {
        yield return new ListItem(createShortcutCommand)
        {
            Title = "Create shortcut",
            Subtitle = "Ctrl+N",
            Icon = new IconInfo("\uE710"),
            MoreCommands =
            [
                ..ShortcutContextCommands.BuildUndoRedoCommands(onReload),
                ShortcutContextCommands.CreateSettingsItem(settings),
            ],
        };

        yield return CreateSettingsRow(settings, onReload);
    }

    public static ListItem CreateSettingsRow(
        QuickShellSettingsManager settings,
        Action onReload) =>
        new(settings.SettingsPage)
        {
            Title = "Quick Shell settings",
            Subtitle = "Terminal, import/export, undo (Ctrl+Z) / redo (Ctrl+Y)",
            Icon = new IconInfo("\uE713"),
            MoreCommands = ShortcutContextCommands.BuildUndoRedoCommands(onReload),
        };
}
