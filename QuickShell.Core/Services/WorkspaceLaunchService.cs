using QuickShell.Abstractions;
using QuickShell.Models;

namespace QuickShell.Services;

internal sealed class WorkspaceLaunchService : IWorkspaceLaunchService
{
    private readonly IShortcutRepository _repository;
    private readonly IShortcutLaunchExecutor _launchExecutor;
    private readonly ICompanionAppLauncher _companionLauncher;

    public WorkspaceLaunchService(
        IShortcutRepository repository,
        IShortcutLaunchExecutor launchExecutor,
        ICompanionAppLauncher companionLauncher)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _launchExecutor = launchExecutor ?? throw new ArgumentNullException(nameof(launchExecutor));
        _companionLauncher = companionLauncher ?? throw new ArgumentNullException(nameof(companionLauncher));
    }

    public WorkspaceAuthorizationResult Authorize(string workspaceId, WorkspaceAction action)
    {
        var workspace = _repository.GetStoredWorkspace(workspaceId);
        if (workspace is null)
        {
            return WorkspaceSecurityPolicy.NotFoundResult();
        }

        return WorkspaceSecurityPolicy.Authorize(workspace, action);
    }

    public ShortcutLaunchResult Launch(
        string workspaceId,
        string terminalApplicationId,
        string defaultProfileId,
        ShortcutLaunchOptions options = default)
    {
        var workspace = _repository.GetStoredWorkspace(workspaceId);
        if (workspace is null)
        {
            return ShortcutLaunchResult.StayOpen("Workspace was not found.");
        }

        var authorization = WorkspaceSecurityPolicy.Authorize(workspace, WorkspaceAction.LaunchTerminal);
        if (!authorization.IsAllowed)
        {
            return Denied(authorization);
        }

        // The policy canonicalizes the directory once. The executor receives a
        // detached content copy so it cannot launch a stale host snapshot.
        var content = WorkspaceClone.Clone(workspace.Content);
        if (!string.IsNullOrWhiteSpace(authorization.EffectiveValues.Directory))
        {
            content.Directory = authorization.EffectiveValues.Directory!;
        }
        var effectiveOptions = options;
        if (effectiveOptions.IncludeCompanionApp)
        {
            var authorizedCompanions = new List<CompanionAppEntry>();
            var openOnLaunchCompanions = _companionLauncher.ShouldLaunchOnWorkspaceOpen(content)
                ? CompanionAppNormalization.GetOpenOnLaunch(content)
                : [];
            foreach (var companion in openOnLaunchCompanions)
            {
                var companionAuthorization = WorkspaceSecurityPolicy.AuthorizeCompanion(
                    workspace,
                    companion);
                if (!companionAuthorization.IsAllowed)
                {
                    continue;
                }

                var effectiveCompanion = BuildEffectiveCompanion(
                    companion,
                    companionAuthorization);
                if (effectiveCompanion is not null)
                {
                    authorizedCompanions.Add(effectiveCompanion);
                }
            }

            content.CompanionApps = authorizedCompanions;
            CompanionAppNormalization.MirrorLegacyFieldsFromPrimary(content);
            if (content.CompanionApps.Count == 0)
            {
                effectiveOptions = effectiveOptions with { IncludeCompanionApp = false };
            }
        }

        if (effectiveOptions.IncludeDevServerLink
            && WorkspaceDevServerActions.ShouldOpenOnWorkspaceLaunch(content))
        {
            var devServerAuthorization = WorkspaceSecurityPolicy.AuthorizeUrl(
                workspace,
                content.DevServerUrl,
                WorkspaceAction.OpenDevServer);
            if (!devServerAuthorization.IsAllowed)
            {
                effectiveOptions = effectiveOptions with { IncludeDevServerLink = false };
            }
            else if (!string.IsNullOrWhiteSpace(devServerAuthorization.EffectiveValues.Url))
            {
                content.DevServerUrl = devServerAuthorization.EffectiveValues.Url;
            }
        }

        return _launchExecutor.Launch(
            content,
            terminalApplicationId,
            defaultProfileId,
            effectiveOptions);
    }

    public ShortcutLaunchResult LaunchEntry(
        string workspaceId,
        string launchId,
        string terminalApplicationId,
        string defaultProfileId,
        ShortcutLaunchOptions options = default)
    {
        var workspace = _repository.GetStoredWorkspace(workspaceId);
        if (workspace is null)
        {
            return ShortcutLaunchResult.StayOpen("Workspace was not found.");
        }

        var launch = workspace.Content.Launches.FirstOrDefault(candidate =>
            candidate.Id.Equals(launchId, StringComparison.OrdinalIgnoreCase)
            && candidate.IsEnabled);
        if (launch is null)
        {
            return ShortcutLaunchResult.StayOpen("That launch entry was not found.");
        }

        var authorization = WorkspaceSecurityPolicy.AuthorizeLaunchEntry(workspace, launch);
        if (!authorization.IsAllowed)
        {
            return Denied(authorization);
        }

        var content = WorkspaceClone.Clone(workspace.Content);
        if (!string.IsNullOrWhiteSpace(authorization.EffectiveValues.Directory))
        {
            content.Directory = authorization.EffectiveValues.Directory!;
        }

        var effectiveLaunch = content.Launches.FirstOrDefault(candidate =>
            candidate.Id.Equals(launchId, StringComparison.OrdinalIgnoreCase));
        return effectiveLaunch is null
            ? ShortcutLaunchResult.StayOpen("That launch entry was not found.")
            : _launchExecutor.LaunchEntry(
                content,
                effectiveLaunch,
                terminalApplicationId,
                defaultProfileId,
                options);
    }

    private static ShortcutLaunchResult Denied(WorkspaceAuthorizationResult authorization)
    {
        var message = authorization.PrimaryIssueCode switch
        {
            WorkspaceIssueCode.WorkspaceUntrusted => "Trust this workspace before launching it.",
            WorkspaceIssueCode.InvalidDirectory or WorkspaceIssueCode.DirectoryMissing => "Repair the workspace directory before launching it.",
            WorkspaceIssueCode.InvalidCommand or WorkspaceIssueCode.InvalidLaunch => "Repair the workspace command or launch entries before launching it.",
            WorkspaceIssueCode.InvalidCompanion or WorkspaceIssueCode.CompanionExecutableUnavailable => "Repair the companion app configuration before launching it.",
            _ => "Workspace launch was blocked by its security policy.",
        };

        return ShortcutLaunchResult.StayOpen(message);
    }

    private static CompanionAppEntry? BuildEffectiveCompanion(
        CompanionAppEntry source,
        WorkspaceAuthorizationResult authorization)
    {
        if (string.IsNullOrWhiteSpace(authorization.EffectiveValues.ExecutablePath))
        {
            return null;
        }

        var companion = CompanionAppNormalization.CloneEntry(source);
        companion.Path = authorization.EffectiveValues.ExecutablePath!;
        companion.Arguments = authorization.EffectiveValues.Arguments;
        return companion;
    }
}
