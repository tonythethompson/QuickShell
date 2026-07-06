using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Commands;

namespace QuickShell.Services;

internal static class ShortcutTaskActionListItems
{
    public static ListItem Create(
        WorkspaceTaskAction action,
        QuickShellSettingsManager settings,
        Action onChanged,
        CreateShortcutCommand? createShortcutCommand = null)
    {
        var item = new ListItem(new OpenShortcutLaunchCommand(action.Workspace, action.Launch, settings))
        {
            Title = BuildTitle(action),
            Subtitle = BuildSubtitle(action),
            Icon = new IconInfo(TerminalLaunchGlyphs.GetForLaunch(action.Launch)),
            MoreCommands = BuildMoreCommands(action, settings, onChanged, createShortcutCommand),
        };

        return item;
    }

    private static string BuildTitle(WorkspaceTaskAction action)
    {
        var taskTitle = ShortcutDisplay.GetLaunchContextMenuTitle(
            action.Launch,
            ShortcutLaunchNormalization.GetEnabledLaunches(action.Workspace));
        return $"{action.Workspace.Name} - {taskTitle}";
    }

    private static string BuildSubtitle(WorkspaceTaskAction action)
    {
        var parts = new List<string>
        {
            ShortcutDisplay.BuildLaunchEntrySubtitle(action.Launch),
            ShortcutDisplay.ShortenPathForDisplay(action.Workspace.Directory),
        };

        if (!string.IsNullOrWhiteSpace(action.Workspace.Abbreviation))
        {
            parts.Add($"home · {action.Workspace.Abbreviation}");
        }

        return string.Join(" · ", parts);
    }

    private static CommandContextItem[] BuildMoreCommands(
        WorkspaceTaskAction action,
        QuickShellSettingsManager settings,
        Action onChanged,
        CreateShortcutCommand? createShortcutCommand)
    {
        var items = new List<CommandContextItem>
        {
            new(new OpenTerminalShortcutCommand(action.Workspace, settings))
            {
                Title = Strings.TaskActions_OpenWorkspace,
                Icon = new IconInfo(ShortcutHealth.GetListGlyph(action.Workspace)),
            },
        };

        if (action.Launch.RunAsAdmin)
        {
            items.Add(new CommandContextItem(new OpenShortcutLaunchCommand(action.Workspace, action.Launch, settings, runAsStandard: true))
            {
                Title = Strings.TaskActions_RunTaskNormally,
                Icon = new IconInfo(TerminalLaunchGlyphs.GetForLaunch(action.Launch)),
            });
        }
        else
        {
            items.Add(new CommandContextItem(new OpenShortcutLaunchCommand(action.Workspace, action.Launch, settings, runAsAdmin: true))
            {
                Title = Strings.TaskActions_RunTaskAsAdmin,
                Icon = new IconInfo(ShortcutGlyphs.AdminLaunch),
            });
        }

        items.AddRange(ShortcutContextCommands.Build(
            action.Workspace,
            onChanged,
            settings,
            createShortcutCommand));

        return items.ToArray();
    }
}
