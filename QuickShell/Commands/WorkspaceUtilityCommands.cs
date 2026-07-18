using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Models;
using QuickShell.Pages;
using QuickShell.Services;

namespace QuickShell.Commands;

internal sealed partial class CopyShortcutPathCommand : InvokableCommand
{
    private readonly IQuickShellServices _services;
    private readonly string _shortcutId;

    public CopyShortcutPathCommand(
        string shortcutId,
        IQuickShellServices services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _shortcutId = shortcutId;
        Name = Strings.Menu_CopyPath;
        Icon = new IconInfo(ShortcutGlyphs.CopyPath);
    }

    public override CommandResult Invoke()
    {
        var workspace = _services.Shortcuts.GetStoredWorkspace(_shortcutId);
        if (workspace is null)
        {
            return QuickShellNavigation.StayOpen(Strings.WorkspaceNotFound);
        }

        var authorization = WorkspaceSecurityPolicy.Authorize(workspace, WorkspaceAction.CopyPath);
        var directory = authorization.EffectiveValues.Directory;
        if (!authorization.IsAllowed || string.IsNullOrWhiteSpace(directory))
        {
            return QuickShellNavigation.StayOpen("The workspace path is invalid.");
        }

        if (!FolderPathActions.TryCopyPath(directory, out var error))
        {
            return QuickShellNavigation.StayOpen(error);
        }

        return QuickShellNavigation.StayOpen(Strings.PathCopiedToClipboard);
    }
}

internal sealed partial class CopyLaunchDiagnosticsCommand : InvokableCommand
{
    public CopyLaunchDiagnosticsCommand()
    {
        Name = "Copy launch diagnostics";
        Icon = new IconInfo(ShortcutGlyphs.CopyDiagnostics);
    }

    public override CommandResult Invoke()
    {
        LaunchDiagnosticsState.TryCopyLastReport(out var message);
        return QuickShellNavigation.StayOpen(message);
    }
}

internal sealed partial class OpenShortcutFolderInExplorerCommand : InvokableCommand
{
    private readonly IQuickShellServices _services;
    private readonly string _shortcutId;

    public OpenShortcutFolderInExplorerCommand(
        string shortcutId,
        IQuickShellServices services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _shortcutId = shortcutId;
        Name = Strings.Menu_OpenInFileExplorer;
        Icon = new IconInfo("\uE838");
    }

    public override CommandResult Invoke()
    {
        var workspace = _services.Shortcuts.GetStoredWorkspace(_shortcutId);
        if (workspace is null)
        {
            return QuickShellNavigation.StayOpen(Strings.WorkspaceNotFound);
        }

        var authorization = WorkspaceSecurityPolicy.Authorize(workspace, WorkspaceAction.OpenDirectory);
        var directory = authorization.EffectiveValues.Directory;
        if (!authorization.IsAllowed || string.IsNullOrWhiteSpace(directory))
        {
            return QuickShellNavigation.StayOpen("Trust this workspace before opening its directory.");
        }

        if (!FolderPathActions.TryOpenInExplorer(directory, out var error))
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
        Name = Strings.OpenDirectory;
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
    private readonly IQuickShellServices _services;
    private readonly string _shortcutId;
    private readonly WorkspaceLinkKind _kind;

    public OpenWorkspaceLinkCommand(
        string shortcutId,
        WorkspaceLinkKind kind,
        IQuickShellServices services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _shortcutId = shortcutId;
        _kind = kind;
        Name = kind switch
        {
            WorkspaceLinkKind.DevServer => Strings.Menu_OpenDevServer,
            WorkspaceLinkKind.Repo => Strings.Menu_OpenRepository,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
        Icon = new IconInfo(
            kind == WorkspaceLinkKind.Repo ? ShortcutGlyphs.OpenRepository : "\uE774");
    }

    public override CommandResult Invoke()
    {
        var workspace = _services.Shortcuts.GetStoredWorkspace(_shortcutId);
        if (workspace is null)
        {
            return QuickShellNavigation.StayOpen(Strings.WorkspaceNotFound);
        }

        var url = _kind == WorkspaceLinkKind.DevServer ? workspace.Content.DevServerUrl : workspace.Content.RepoUrl;
        var authorization = WorkspaceSecurityPolicy.AuthorizeUrl(workspace, url);
        if (!authorization.IsAllowed || string.IsNullOrWhiteSpace(authorization.EffectiveValues.Url))
        {
            return QuickShellNavigation.StayOpen("Trust this workspace before opening external links.");
        }

        if (!WorkspaceLinkActions.TryOpenLink(authorization.EffectiveValues.Url, out var error))
        {
            return QuickShellNavigation.StayOpen(error);
        }

        return CommandResult.Dismiss();
    }
}

internal sealed partial class OpenCompanionAppCommand : InvokableCommand
{
    private readonly IQuickShellServices _services;
    private readonly string _shortcutId;

    public OpenCompanionAppCommand(
        TerminalShortcut shortcut,
        IQuickShellServices services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _shortcutId = shortcut.Id;
        var primaryPath = CompanionAppNormalization.GetPrimary(shortcut)?.Path ?? shortcut.CompanionAppPath;
        Name = Strings.Menu_OpenCompanionAppFormat(services.CompanionApps.BuildDisplaySummary(shortcut));
        Icon = new IconInfo(CompanionAppCatalog.GetContextMenuIcon(primaryPath));
    }

    public override CommandResult Invoke()
    {
        var workspace = _services.Shortcuts.GetStoredWorkspace(_shortcutId);
        if (workspace is null)
        {
            return QuickShellNavigation.StayOpen(Strings.WorkspaceNotFound);
        }

        var authorization = WorkspaceSecurityPolicy.Authorize(workspace, WorkspaceAction.StartCompanion);
        if (!authorization.IsAllowed)
        {
            return QuickShellNavigation.StayOpen("Trust and repair this workspace before starting companion apps.");
        }

        var content = WorkspaceClone.Clone(workspace.Content);
        var configured = CompanionAppNormalization.GetConfigured(content);
        if (configured.Count > 0 && !string.IsNullOrWhiteSpace(authorization.EffectiveValues.ExecutablePath))
        {
            configured[0].Path = authorization.EffectiveValues.ExecutablePath;
            configured[0].Arguments = authorization.EffectiveValues.Arguments;
            CompanionAppNormalization.MirrorLegacyFieldsFromPrimary(content);
        }

        if (!_services.CompanionApps.TryLaunch(content, onDemand: true, out var error))
        {
            return QuickShellNavigation.StayOpen(error ?? Strings.CompanionAppLaunchFailed);
        }

        return CommandResult.Dismiss();
    }
}

internal sealed partial class OpenDiscoverGitReposCommand : DiscoverGitReposPage
{
    public OpenDiscoverGitReposCommand(QuickShellPageContext context)
        : base(context)
    {
        Id = PageId;
        Icon = new IconInfo(ShortcutGlyphs.Discover);
        Title = Strings.Discover_Title;
        Name = Strings.Discover_Name;
        PlaceholderText = Strings.Discover_Placeholder;
    }
}
