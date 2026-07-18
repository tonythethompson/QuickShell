using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Commands;

namespace QuickShell.Services;

internal static class ShortcutTaskActionListItems
{
    public static ListItem Create(
        QuickShellPageContext context,
        WorkspaceTaskAction action,
        Action onChanged)
    {
        ArgumentNullException.ThrowIfNull(context);

        var item = new ListItem(new OpenShortcutLaunchCommand(action.Workspace, action.Launch, context.Services))
        {
            Title = BuildTitle(action),
            Subtitle = BuildSubtitle(action),
            Icon = new IconInfo(TerminalLaunchGlyphs.GetForList(action.Launch)),
            MoreCommands = BuildMoreCommands(context, action, onChanged),
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
        QuickShellPageContext context,
        WorkspaceTaskAction action,
        Action onChanged)
    {
        var services = context.Services;
        var needsRepair = ShortcutHealth.WouldNeedRepair(action.Workspace, requireDirectoryExists: false);
        var items = new List<CommandContextItem>
        {
            new(new OpenTerminalShortcutCommand(action.Workspace, services))
            {
                Title = Strings.TaskActions_OpenWorkspace,
                Icon = new IconInfo(ShortcutHealth.GetListGlyph(action.Workspace, needsRepair)),
            },
        };

        if (action.Launch.RunAsAdmin)
        {
            items.Add(new CommandContextItem(new OpenShortcutLaunchCommand(action.Workspace, action.Launch, services, runAsStandard: true))
            {
                Title = Strings.TaskActions_RunTaskNormally,
                Icon = new IconInfo(TerminalLaunchGlyphs.GetForList(action.Launch)),
            });
        }
        else
        {
            items.Add(new CommandContextItem(new OpenShortcutLaunchCommand(action.Workspace, action.Launch, services, runAsAdmin: true))
            {
                Title = Strings.TaskActions_RunTaskAsAdmin,
                Icon = new IconInfo(ShortcutGlyphs.AdminLaunch),
            });
        }

        items.AddRange(ShortcutContextCommands.Build(context, action.Workspace, onChanged, includeEdit: true, needsRepair: needsRepair));

        return items.ToArray();
    }
}
