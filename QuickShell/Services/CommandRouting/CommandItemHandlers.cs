using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Commands;
using QuickShell.Models;
using QuickShell.Pages;
using QuickShell.Services;

namespace QuickShell.Services.CommandRouting;

internal sealed class OpenSettingsCommandHandler : ICommandItemHandler
{
    public CommandKind Kind => CommandKind.OpenSettings;

    public ICommandItem? Create(CommandDescriptor descriptor, QuickShellPageContext context) =>
        new CommandItem(context.Settings.SettingsPage)
        {
            Title = context.Settings.SettingsPage.Title,
            Icon = context.Settings.SettingsPage.Icon,
        };
}

internal sealed class ImportConflictCommandHandler : ICommandItemHandler
{
    public CommandKind Kind => CommandKind.ImportConflict;

    public ICommandItem? Create(CommandDescriptor descriptor, QuickShellPageContext context) =>
        new CommandItem(new ImportConflictPage(context.Services, context.ReloadRootPages))
        {
            Title = Strings.ImportConflictPage_Title,
            Icon = new IconInfo("\uE8FD"),
        };
}

internal sealed class PendingShortcutEditCommandHandler : ICommandItemHandler
{
    public CommandKind Kind => CommandKind.PendingShortcutEdit;

    public ICommandItem? Create(CommandDescriptor descriptor, QuickShellPageContext context) =>
        new CommandItem(new PendingShortcutEditPage(context.Services, context.ReloadRootPages))
        {
            Title = Strings.PendingEdit_Title,
            Icon = new IconInfo("\uE7BA"),
        };
}

internal sealed class CreateWorkspaceCommandHandler : ICommandItemHandler
{
    public CommandKind Kind => CommandKind.CreateWorkspace;

    public ICommandItem? Create(CommandDescriptor descriptor, QuickShellPageContext context) =>
        new CommandItem(context.CreateShortcut)
        {
            Title = "Create workspace",
            Subtitle = "Folder and terminal launches",
            Icon = new IconInfo("\uE710"),
        };
}

internal sealed class DiscoverCreateWorkspaceCommandHandler : ICommandItemHandler
{
    public CommandKind Kind => CommandKind.DiscoverCreateWorkspace;

    public ICommandItem? Create(CommandDescriptor descriptor, QuickShellPageContext context)
    {
        var discoverDirectory = descriptor.Directory!;
        var seed = WorkspaceSeedFactory.FromGitRepoDirectory(discoverDirectory, context.Services.ProjectAnalysis);
        return new CommandItem(new CreateShortcutCommand(context.ReloadRootPages, seed, context.Services))
        {
            Title = seed.Name,
            Subtitle = DiscoverGitRepoListItems.BuildSubtitleForNew(new GitRepoCandidate
            {
                Directory = discoverDirectory,
                Name = seed.Name,
                RemoteUrl = seed.RepoUrl,
                Classification = context.Services.ProjectAnalysis.Classify(discoverDirectory),
            }),
            Icon = new IconInfo(ShortcutGlyphs.Add),
        };
    }
}

internal sealed class DiscoverGitReposCommandHandler : ICommandItemHandler
{
    public CommandKind Kind => CommandKind.DiscoverGitRepos;

    public ICommandItem? Create(CommandDescriptor descriptor, QuickShellPageContext context) =>
        new CommandItem(new OpenDiscoverGitReposCommand(context))
        {
            Title = "Discover git repos",
            Icon = new IconInfo(ShortcutGlyphs.Discover),
        };
}

internal sealed class OpenLaunchCommandHandler : ICommandItemHandler
{
    public CommandKind Kind => CommandKind.OpenLaunch;

    public ICommandItem? Create(CommandDescriptor descriptor, QuickShellPageContext context)
    {
        var shortcutId = descriptor.WorkspaceId!;
        var launchId = descriptor.LaunchId!;
        var shortcut = context.Shortcuts.GetByIdReadOnly(shortcutId);
        if (shortcut is null || ShortcutHealth.WouldNeedRepair(shortcut))
        {
            return null;
        }

        TerminalShortcut workspace = shortcut;
        if (shortcut.Launches.Count == 0)
        {
            workspace = context.Shortcuts.GetById(shortcutId)!;
            ShortcutLaunchNormalization.EnsureLaunchesFromLegacy(workspace);
        }

        var launch = workspace.Launches.FirstOrDefault(entry =>
            entry.Id.Equals(launchId, StringComparison.OrdinalIgnoreCase));
        if (launch is null || !launch.IsEnabled)
        {
            return null;
        }

        var action = new WorkspaceTaskAction
        {
            Workspace = workspace,
            Launch = launch,
            Score = 0,
        };
        return ShortcutTaskActionListItems.Create(context, action, context.ReloadRootPages);
    }
}

internal sealed class OpenWorkspaceCommandHandler : ICommandItemHandler
{
    public CommandKind Kind => CommandKind.OpenWorkspace;

    public ICommandItem? Create(CommandDescriptor descriptor, QuickShellPageContext context)
    {
        var shortcut = context.Shortcuts.ResolveForOpenCommand(descriptor.WorkspaceId!);
        return shortcut is null
            ? null
            : ShortcutListItems.CreateOpen(context, shortcut, context.ReloadRootPages);
    }
}

internal sealed class WorkspaceStatusCommandHandler : ICommandItemHandler
{
    public CommandKind Kind => CommandKind.WorkspaceStatus;

    public ICommandItem? Create(CommandDescriptor descriptor, QuickShellPageContext context)
    {
        var shortcut = context.Shortcuts.GetById(descriptor.WorkspaceId!);
        return shortcut is null
            ? null
            : new CommandItem(new WorkspaceStatusPage(context.Services, shortcut, context.ReloadRootPages))
            {
                Title = shortcut.Name,
                Icon = new IconInfo("\uE9D9"),
            };
    }
}

internal sealed class WorktreeBranchPickerCommandHandler : ICommandItemHandler
{
    public CommandKind Kind => CommandKind.WorktreeBranchPicker;

    public ICommandItem? Create(CommandDescriptor descriptor, QuickShellPageContext context)
    {
        var shortcut = context.Shortcuts.GetById(descriptor.WorkspaceId!);
        if (shortcut is null)
        {
            return null;
        }

        WorkspaceGitStatus? status = null;
        string? target = null;
        if (context.Services.GitOperations.TryGetStatus(shortcut.Directory, out var gitStatus))
        {
            status = gitStatus;
            target = context.Services.TargetStore.GetTargetForDirectory(
                shortcut.Directory,
                context.Services.GitOperations);
        }

        return new CommandItem(new WorktreeBranchPickerPage(
            context.Services,
            shortcut.Id,
            context.ReloadRootPages,
            status,
            target))
        {
            Title = Strings.Menu_SwitchBranch,
            Icon = new IconInfo("\uE8AB"),
        };
    }
}

internal sealed class WorktreeBranchSelectCommandHandler : ICommandItemHandler
{
    public CommandKind Kind => CommandKind.WorktreeBranchSelect;

    public ICommandItem? Create(CommandDescriptor descriptor, QuickShellPageContext context)
    {
        var shortcut = context.Shortcuts.GetById(descriptor.WorkspaceId!);
        if (shortcut is null)
        {
            return null;
        }

        return new CommandItem(new SelectWorktreeBranchCommand(
            shortcut.Id,
            descriptor.Branch!,
            context.Services,
            context.ReloadRootPages))
        {
            Title = descriptor.Branch!,
            Icon = new IconInfo("\uE8AB"),
        };
    }
}

internal sealed class WorktreeBranchClearCommandHandler : ICommandItemHandler
{
    public CommandKind Kind => CommandKind.WorktreeBranchClear;

    public ICommandItem? Create(CommandDescriptor descriptor, QuickShellPageContext context)
    {
        var shortcut = context.Shortcuts.GetById(descriptor.WorkspaceId!);
        return shortcut is null
            ? null
            : new CommandItem(new UseCurrentWorktreeBranchCommand(shortcut.Id, context.ReloadRootPages, context.Services))
            {
                Title = Strings.Menu_UseCurrentBranch,
                Icon = new IconInfo("\uE894"),
            };
    }
}
