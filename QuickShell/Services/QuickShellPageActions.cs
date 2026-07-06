using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Commands;
using QuickShell.Pages;
using QuickShell.Services;

namespace QuickShell.Services;

internal static class QuickShellPageActions
{
    public static IEnumerable<IListItem> BuildItems(
        CreateShortcutCommand createShortcutCommand,
        OpenDiscoverGitReposCommand discoverGitReposCommand,
        QuickShellSettingsManager settings,
        Action onReload)
    {
        yield return new ListItem(createShortcutCommand)
        {
            Title = Strings.PageActions_CreateWorkspace_Title,
            Subtitle = Strings.PageActions_CreateWorkspace_Subtitle,
            Icon = new IconInfo("\uE710"),
            MoreCommands =
            [
                ..ShortcutContextCommands.BuildUndoRedoCommands(onReload),
                ShortcutContextCommands.CreateSettingsItem(settings),
            ],
        };

        yield return new ListItem(discoverGitReposCommand)
        {
            Title = Strings.PageActions_DiscoverRepos_Title,
            Subtitle = Strings.PageActions_DiscoverRepos_Subtitle,
            Icon = new IconInfo(ShortcutGlyphs.Discover),
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
            Title = QuickShellBrand.SettingsTitle,
            Subtitle = Strings.PageActions_Settings_Subtitle,
            Icon = new IconInfo("\uE713"),
            MoreCommands = ShortcutContextCommands.BuildUndoRedoCommands(onReload),
        };
}
