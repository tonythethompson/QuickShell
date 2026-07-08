using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Commands;
using QuickShell.Models;
using QuickShell.Pages;

namespace QuickShell.Services;

/// <summary>
/// CmdPal-facing router: parse via Core <see cref="ICommandIdParser"/>, create
/// items with existing list/command factories.
/// </summary>
internal sealed class CommandRouter : ICommandRouter
{
    private readonly ICommandIdParser _parser;
    private readonly IShortcutRepository _shortcuts;
    private readonly QuickShellSettingsManager _settingsManager;
    private readonly CreateShortcutCommand _createShortcutCommand;
    private readonly Action _reloadPages;

    public CommandRouter(
        ICommandIdParser parser,
        IShortcutRepository shortcuts,
        QuickShellSettingsManager settingsManager,
        CreateShortcutCommand createShortcutCommand,
        Action reloadPages)
    {
        ArgumentNullException.ThrowIfNull(parser);
        ArgumentNullException.ThrowIfNull(shortcuts);
        ArgumentNullException.ThrowIfNull(settingsManager);
        ArgumentNullException.ThrowIfNull(createShortcutCommand);
        ArgumentNullException.ThrowIfNull(reloadPages);

        _parser = parser;
        _shortcuts = shortcuts;
        _settingsManager = settingsManager;
        _createShortcutCommand = createShortcutCommand;
        _reloadPages = reloadPages;
    }

    public bool TryHandle(string id, out ICommandItem? item)
    {
        item = null;

        if (!_parser.TryParse(id, out var descriptor))
        {
            return false;
        }

        item = CreateItem(descriptor);
        return true;
    }

    private ICommandItem? CreateItem(CommandDescriptor descriptor)
    {
        switch (descriptor.Kind)
        {
            case CommandKind.OpenSettings:
                return new CommandItem(_settingsManager.SettingsPage)
                {
                    Title = _settingsManager.SettingsPage.Title,
                    Icon = _settingsManager.SettingsPage.Icon,
                };

            case CommandKind.CreateWorkspace:
                return new CommandItem(new CreateShortcutCommand(_reloadPages))
                {
                    Title = "Create workspace",
                    Subtitle = "Folder and terminal launches",
                    Icon = new IconInfo("\uE710"),
                };

            case CommandKind.DiscoverCreateWorkspace:
                return CreateDiscoverCreateItem(descriptor.Directory!);

            case CommandKind.DiscoverGitRepos:
                return new CommandItem(new OpenDiscoverGitReposCommand(_reloadPages))
                {
                    Title = "Discover git repos",
                    Icon = new IconInfo(ShortcutGlyphs.Discover),
                };

            case CommandKind.OpenLaunch:
                return CreateOpenLaunchItem(descriptor.WorkspaceId!, descriptor.LaunchId!);

            case CommandKind.OpenWorkspace:
                return CreateOpenWorkspaceItem(descriptor.WorkspaceId!);

            default:
                return null;
        }
    }

    private CommandItem CreateDiscoverCreateItem(string discoverDirectory)
    {
        var seed = WorkspaceSeedFactory.FromGitRepoDirectory(discoverDirectory);
        return new CommandItem(new CreateShortcutCommand(_reloadPages, seed))
        {
            Title = seed.Name,
            Subtitle = DiscoverGitRepoListItems.BuildSubtitleForNew(new GitRepoCandidate
            {
                Directory = discoverDirectory,
                Name = seed.Name,
                RemoteUrl = seed.RepoUrl,
                Classification = ProjectClassifier.Classify(discoverDirectory),
            }),
            Icon = new IconInfo(ShortcutGlyphs.Add),
        };
    }

    private ListItem? CreateOpenLaunchItem(string shortcutId, string launchId)
    {
        var shortcut = _shortcuts.GetByIdReadOnly(shortcutId);
        if (shortcut is null || ShortcutHealth.WouldNeedRepair(shortcut))
        {
            return null;
        }

        TerminalShortcut workspace = shortcut;
        if (shortcut.Launches.Count == 0)
        {
            workspace = _shortcuts.GetById(shortcutId)!;
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
        return ShortcutTaskActionListItems.Create(action, _settingsManager, _reloadPages, _createShortcutCommand);
    }

    private ListItem? CreateOpenWorkspaceItem(string openKey)
    {
        var shortcut = _shortcuts.ResolveForOpenCommand(openKey);
        if (shortcut is null)
        {
            return null;
        }

        return ShortcutListItems.CreateOpen(shortcut, _settingsManager, _reloadPages, _createShortcutCommand);
    }
}
