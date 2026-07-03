using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Models;
using QuickShell.Pages;
using QuickShell.Services;

namespace QuickShell.Commands;

internal sealed partial class CopyShortcutPathCommand : InvokableCommand
{
    private readonly string _shortcutId;

    public CopyShortcutPathCommand(string shortcutId)
    {
        _shortcutId = shortcutId;
        Name = "Copy path";
        Icon = new IconInfo(ShortcutGlyphs.CopyPath);
    }

    public override CommandResult Invoke()
    {
        var shortcut = QuickShellRuntimeServices.Shortcuts.GetById(_shortcutId);
        if (shortcut is null)
        {
            return QuickShellNavigation.StayOpen("That workspace was not found.");
        }

        if (!FolderPathActions.TryCopyPath(shortcut.Directory, out var error))
        {
            return QuickShellNavigation.StayOpen(error);
        }

        return QuickShellNavigation.StayOpen("Path copied to clipboard.");
    }
}

internal sealed partial class OpenShortcutFolderInExplorerCommand : InvokableCommand
{
    private readonly string _shortcutId;

    public OpenShortcutFolderInExplorerCommand(string shortcutId)
    {
        _shortcutId = shortcutId;
        Name = "Open in File Explorer";
        Icon = new IconInfo("\uE838");
    }

    public override CommandResult Invoke()
    {
        var shortcut = QuickShellRuntimeServices.Shortcuts.GetById(_shortcutId);
        if (shortcut is null)
        {
            return QuickShellNavigation.StayOpen("That workspace was not found.");
        }

        if (!FolderPathActions.TryOpenInExplorer(shortcut.Directory, out var error))
        {
            return QuickShellNavigation.StayOpen(error);
        }

        return CommandResult.Dismiss();
    }
}

internal sealed partial class OpenDirectoryInExplorerCommand : InvokableCommand
{
    private readonly string _directory;

    public OpenDirectoryInExplorerCommand(string directory)
    {
        _directory = directory;
        Name = "Open directory";
        Icon = new IconInfo("\uE838");
    }

    public override CommandResult Invoke()
    {
        if (!FolderPathActions.TryOpenInExplorer(_directory, out var error))
        {
            return QuickShellNavigation.StayOpen(error);
        }

        return CommandResult.Dismiss();
    }
}

internal enum WorkspaceLinkKind
{
    DevServer,
    Repo,
}

internal sealed partial class OpenWorkspaceLinkCommand : InvokableCommand
{
    private readonly string _shortcutId;
    private readonly WorkspaceLinkKind _kind;

    public OpenWorkspaceLinkCommand(string shortcutId, WorkspaceLinkKind kind)
    {
        _shortcutId = shortcutId;
        _kind = kind;
        Name = kind switch
        {
            WorkspaceLinkKind.DevServer => "Open dev server",
            WorkspaceLinkKind.Repo => "Open repository",
            _ => "Open link",
        };
        Icon = new IconInfo(
            kind == WorkspaceLinkKind.Repo ? ShortcutGlyphs.OpenRepository : "\uE774");
    }

    public override CommandResult Invoke()
    {
        var shortcut = QuickShellRuntimeServices.Shortcuts.GetById(_shortcutId);
        if (shortcut is null)
        {
            return QuickShellNavigation.StayOpen("That workspace was not found.");
        }

        var url = _kind == WorkspaceLinkKind.DevServer ? shortcut.DevServerUrl : shortcut.RepoUrl;
        if (!WorkspaceLinkActions.TryOpenLink(url, out var error))
        {
            return QuickShellNavigation.StayOpen(error);
        }

        return CommandResult.Dismiss();
    }
}

internal sealed partial class OpenCompanionAppCommand : InvokableCommand
{
    private readonly string _shortcutId;

    public OpenCompanionAppCommand(TerminalShortcut shortcut)
    {
        _shortcutId = shortcut.Id;
        Name = $"Open {CompanionAppCatalog.GetDisplayName(shortcut.CompanionAppPath)}";
        Icon = new IconInfo(CompanionAppCatalog.GetContextMenuIcon(shortcut.CompanionAppPath));
    }

    public override CommandResult Invoke()
    {
        var shortcut = QuickShellRuntimeServices.Shortcuts.GetById(_shortcutId);
        if (shortcut is null)
        {
            return QuickShellNavigation.StayOpen("That workspace was not found.");
        }

        if (!CompanionAppLauncher.TryLaunch(shortcut, onDemand: true, out var error))
        {
            return QuickShellNavigation.StayOpen(error ?? "Companion app could not be launched.");
        }

        return CommandResult.Dismiss();
    }
}

internal sealed partial class OpenDiscoverGitReposCommand : DiscoverGitReposPage
{
    public OpenDiscoverGitReposCommand(Action onReload)
        : base(onReload)
    {
    }
}
