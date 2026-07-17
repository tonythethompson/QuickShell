using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Commands;
using QuickShell.Pages;

namespace QuickShell.Services;

internal static class QuickShellPageActions
{
    public static IEnumerable<IListItem> BuildItems(QuickShellPageContext context)
    {
        yield return new ListItem(context.CreateShortcut)
        {
            Title = Strings.PageActions_CreateWorkspace_Title,
            Subtitle = Strings.PageActions_CreateWorkspace_Subtitle,
            Icon = new IconInfo("\uE710"),
            MoreCommands =
            [
                ..ShortcutContextCommands.BuildUndoRedoCommands(context.Services, context.ReloadRootPages),
                ShortcutContextCommands.CreateSettingsItem(context.Services),
            ],
        };

        yield return new ListItem(new OpenDiscoverGitReposCommand(context))
        {
            Title = Strings.PageActions_DiscoverRepos_Title,
            Subtitle = Strings.PageActions_DiscoverRepos_Subtitle,
            Icon = new IconInfo(ShortcutGlyphs.Discover),
            MoreCommands =
            [
                ..ShortcutContextCommands.BuildUndoRedoCommands(context.Services, context.ReloadRootPages),
                ShortcutContextCommands.CreateSettingsItem(context.Services),
            ],
        };

        yield return CreateSettingsRow(context);
    }

    public static ListItem CreateSettingsRow(QuickShellPageContext context) =>
        new(context.Settings.SettingsPage)
        {
            Title = QuickShellBrand.SettingsTitle,
            Icon = new IconInfo("\uE713"),
            MoreCommands = ShortcutContextCommands.BuildUndoRedoCommands(context.Services, context.ReloadRootPages),
        };
}
