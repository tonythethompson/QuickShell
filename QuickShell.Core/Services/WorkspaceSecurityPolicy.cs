using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using QuickShell.Models;

namespace QuickShell.Services;

internal enum WorkspaceAction
{
    LaunchTerminal,
    LaunchEntry,
    StartCompanion,
    OpenUrl,
    OpenDevServer,
    OpenDirectory,
    CopyPath,
    GrantTrust,
    RevokeTrust,
}

internal enum WorkspaceIssueCode
{
    WorkspaceNotFound,
    WorkspaceUntrusted,
    InvalidDirectory,
    DirectoryMissing,
    InvalidCommand,
    InvalidLaunch,
    InvalidUrl,
    InvalidCompanion,
    CompanionExecutableUnavailable,
    DirectoryOpenNotAllowed,
    ActionNotAllowed,
    WorkspaceChangedSinceReview,
}

internal sealed record WorkspaceIssue(WorkspaceIssueCode Code, string Message, bool Blocking = true);

internal sealed record WorkspaceRisk(string Code, string Description);

internal sealed record WorkspaceEffectiveValues(
    string? Directory,
    string? Url,
    string? ExecutablePath,
    string? WorkingDirectory,
    string? Arguments,
    string? Command);

internal sealed record WorkspaceAuthorizationResult(
    bool IsAllowed,
    WorkspaceIssueCode? PrimaryIssueCode,
    IReadOnlyList<WorkspaceIssue> Issues,
    IReadOnlyList<WorkspaceRisk> Risks,
    WorkspaceEffectiveValues EffectiveValues,
    long Revision);

internal sealed record WorkspaceReviewToken(string WorkspaceId, long Revision, string Digest);

internal sealed record WorkspaceReviewSnapshot(
    StoredWorkspace? Workspace,
    WorkspaceReviewToken? Token,
    WorkspaceAuthorizationResult Assessment);

internal enum TrustTransitionStatus
{
    Granted,
    Revoked,
    AlreadyInRequestedState,
    WorkspaceNotFound,
    WorkspaceInvalid,
    WorkspaceChangedSinceReview,
    PersistenceFailed,
}

internal sealed record TrustTransitionResult(TrustTransitionStatus Status, string Message)
{
    public bool Success => Status is TrustTransitionStatus.Granted or TrustTransitionStatus.Revoked
        or TrustTransitionStatus.AlreadyInRequestedState;
}

internal static class WorkspaceSecurityPolicy
{
    public static WorkspaceAuthorizationResult NotFoundResult() =>
        BuildResult(
            false,
            WorkspaceIssueCode.WorkspaceNotFound,
            [new(WorkspaceIssueCode.WorkspaceNotFound, "Workspace was not found.")],
            [],
            null,
            null,
            null,
            null,
            null,
            0);

    public static WorkspaceAuthorizationResult Authorize(
        StoredWorkspace workspace,
        WorkspaceAction action)
    {
        var content = workspace.Content;
        var issues = new List<WorkspaceIssue>();
        var risks = new List<WorkspaceRisk>();
        string? normalizedDirectory = null;
        string? normalizedUrl = null;
        string? executablePath = null;
        string? arguments = null;
        var rawDirectory = content.Directory ?? string.Empty;

        if (rawDirectory.Length > ShortcutValidation.MaxDirectoryLength
            || rawDirectory.IndexOfAny(['\r', '\n', '\0']) >= 0
            || !ShortcutValidation.TryNormalizeDirectory(rawDirectory, out normalizedDirectory, out _))
        {
            issues.Add(new(WorkspaceIssueCode.InvalidDirectory, "Workspace directory is not a valid rooted path."));
        }
        else if ((action is WorkspaceAction.LaunchTerminal or WorkspaceAction.LaunchEntry or WorkspaceAction.GrantTrust) &&
                 !ShortcutValidation.DirectoryExists(normalizedDirectory))
        {
            issues.Add(new(WorkspaceIssueCode.DirectoryMissing, "Workspace directory does not exist."));
        }

        if (!string.IsNullOrEmpty(content.Command))
        {
            risks.Add(new("command", "This workspace contains a command that can execute arbitrary code."));
        }

        if (action != WorkspaceAction.CopyPath)
        {
            if (string.IsNullOrWhiteSpace(content.Name) || content.Name.Length > ShortcutValidation.MaxNameLength)
            {
                issues.Add(new(WorkspaceIssueCode.InvalidLaunch, "Workspace name is missing or exceeds the limit."));
            }

            if (!ShortcutLaunchNormalization.TryValidateLaunches(content, out var launchesError))
            {
                issues.Add(new(WorkspaceIssueCode.InvalidLaunch, launchesError));
            }
        }

        if (content.RunAsAdmin || content.Launches.Any(launch => launch.RunAsAdmin))
        {
            risks.Add(new("elevation", "This workspace can request an elevated process and UAC."));
        }

        if (!ShortcutValidation.TryValidateCommand(content.Command, out _))
        {
            issues.Add(new(WorkspaceIssueCode.InvalidCommand, "Workspace command contains invalid control characters or exceeds the limit."));
        }

        foreach (var launch in content.Launches ?? [])
        {
            if (!ShortcutValidation.TryValidateCommand(launch.Command, out _)
                || !ShortcutValidation.TryValidateWtProfile(launch.WtProfile, out _))
            {
                issues.Add(new(WorkspaceIssueCode.InvalidLaunch, $"Launch '{launch.Label}' contains invalid command or profile data."));
            }
        }

        if (!ShortcutValidation.TryValidateOptionalLinkUrl(content.DevServerUrl, out _, out normalizedUrl))
        {
            issues.Add(new(WorkspaceIssueCode.InvalidUrl, "Dev-server URL must be an absolute HTTP(S) URL."));
        }

        if (!ShortcutValidation.TryValidateOptionalLinkUrl(content.RepoUrl, out _, out _))
        {
            issues.Add(new(WorkspaceIssueCode.InvalidUrl, "Repository URL must be an absolute HTTP(S) URL."));
        }

        var configuredCompanions = CompanionAppNormalization.GetConfigured(content);
        if (configuredCompanions.Count > 0)
        {
            risks.Add(new("companions", $"This workspace can start {configuredCompanions.Count} companion process(es)."));
        }

        foreach (var companion in configuredCompanions)
        {
            if (string.IsNullOrWhiteSpace(companion.Path))
            {
                issues.Add(new(WorkspaceIssueCode.InvalidCompanion, "A companion executable path is empty."));
                continue;
            }

            if (companion.Path.Length > ShortcutValidation.MaxCompanionAppPathLength
                || companion.Path.IndexOfAny(['\r', '\n', '\0']) >= 0
                || (!string.IsNullOrEmpty(companion.Arguments)
                    && (companion.Arguments.Length > ShortcutValidation.MaxCompanionAppArgumentsLength
                        || companion.Arguments.IndexOfAny(['\r', '\n', '\0']) >= 0)))
            {
                issues.Add(new(WorkspaceIssueCode.InvalidCompanion, "Companion executable or arguments contain invalid characters or exceed the limit."));
                continue;
            }

            if (!CompanionAppCatalog.TryResolveExecutablePath(companion.Path, out var resolved))
            {
                issues.Add(new(WorkspaceIssueCode.CompanionExecutableUnavailable, $"Companion executable was not found: {companion.Path}"));
            }
            else if (executablePath is null)
            {
                executablePath = resolved;
                arguments = CompanionAppLauncher.ExpandArguments(
                    companion.Arguments,
                    normalizedDirectory ?? string.Empty);
            }
        }

        if (content.OpenDevServerOnLaunch && !string.IsNullOrWhiteSpace(content.DevServerUrl))
        {
            risks.Add(new("dev-server", "This workspace opens a configured URL after launch."));
        }

        if (!workspace.Security.IsTrusted)
        {
            if (action is WorkspaceAction.LaunchTerminal
                or WorkspaceAction.LaunchEntry
                or WorkspaceAction.StartCompanion
                or WorkspaceAction.OpenUrl
                or WorkspaceAction.OpenDevServer
                or WorkspaceAction.OpenDirectory)
            {
                issues.Add(new(WorkspaceIssueCode.WorkspaceUntrusted, "Trust this workspace before starting external processes or opening it."));
            }
        }

        if (action == WorkspaceAction.OpenDirectory)
        {
            if (workspace.Security.IsTrusted && !IsLocalDirectoryPath(normalizedDirectory))
            {
                issues.Add(new(WorkspaceIssueCode.DirectoryOpenNotAllowed, "Only existing rooted local drive directories can be opened in Explorer."));
            }
            else if (!workspace.Security.IsTrusted)
            {
                issues.Add(new(WorkspaceIssueCode.DirectoryOpenNotAllowed, "Untrusted workspaces cannot open directories."));
            }
        }

        if (action is WorkspaceAction.OpenUrl or WorkspaceAction.OpenDevServer && normalizedUrl is null)
        {
            issues.Add(new(WorkspaceIssueCode.InvalidUrl, "No valid HTTP(S) URL is configured."));
        }

        if (action == WorkspaceAction.GrantTrust && workspace.Security.IsTrusted)
        {
            return BuildResult(true, null, issues, risks, normalizedDirectory, normalizedUrl, executablePath, arguments, content.Command, workspace.Revision);
        }

        var primary = GetPrimaryIssue(issues, action);
        var allowed = action switch
        {
            WorkspaceAction.CopyPath => !issues.Any(issue => issue.Code == WorkspaceIssueCode.InvalidDirectory),
            WorkspaceAction.RevokeTrust => true,
            WorkspaceAction.GrantTrust => issues.Count == 0,
            _ => issues.Count == 0,
        };
        return BuildResult(allowed, primary, issues, risks, normalizedDirectory, normalizedUrl, executablePath, arguments, content.Command, workspace.Revision);
    }

    public static WorkspaceAuthorizationResult AuthorizeUrl(
        StoredWorkspace workspace,
        string? url,
        WorkspaceAction action = WorkspaceAction.OpenUrl)
    {
        var content = WorkspaceClone.Clone(workspace.Content);
        content.DevServerUrl = url;
        content.RepoUrl = null;
        return Authorize(
            workspace with { Content = content },
            action);
    }

    private static WorkspaceIssueCode? GetPrimaryIssue(
        List<WorkspaceIssue> issues,
        WorkspaceAction action)
    {
        if (issues.Count == 0)
        {
            return null;
        }

        var precedence = action == WorkspaceAction.CopyPath
            ? new[] { WorkspaceIssueCode.InvalidDirectory }
            : new[]
            {
                WorkspaceIssueCode.WorkspaceNotFound,
                WorkspaceIssueCode.InvalidDirectory,
                WorkspaceIssueCode.DirectoryMissing,
                WorkspaceIssueCode.InvalidCommand,
                WorkspaceIssueCode.InvalidLaunch,
                WorkspaceIssueCode.InvalidUrl,
                WorkspaceIssueCode.InvalidCompanion,
                WorkspaceIssueCode.CompanionExecutableUnavailable,
                WorkspaceIssueCode.WorkspaceUntrusted,
                WorkspaceIssueCode.DirectoryOpenNotAllowed,
                WorkspaceIssueCode.ActionNotAllowed,
            };

        return precedence.FirstOrDefault(code => issues.Any(issue => issue.Code == code));
    }

    public static WorkspaceReviewToken CreateReviewToken(StoredWorkspace workspace) =>
        new(workspace.Content.Id, workspace.Revision, ComputeDigest(workspace.Content));

    public static bool MatchesReviewToken(StoredWorkspace workspace, WorkspaceReviewToken token) =>
        string.Equals(workspace.Content.Id, token.WorkspaceId, StringComparison.OrdinalIgnoreCase)
        && workspace.Revision == token.Revision
        && string.Equals(ComputeDigest(workspace.Content), token.Digest, StringComparison.Ordinal);

    public static string ComputeDigest(TerminalShortcut workspace)
    {
        var payload = JsonSerializer.Serialize(new
        {
            workspace.Id,
            workspace.Name,
            workspace.Directory,
            workspace.Command,
            workspace.Terminal,
            workspace.WtProfile,
            workspace.RunAsAdmin,
            workspace.Launches,
            workspace.DevServerUrl,
            workspace.OpenDevServerOnLaunch,
            workspace.RepoUrl,
            workspace.CompanionApps,
            workspace.OpenCompanionAppOnLaunch,
            workspace.CompanionAppPath,
            workspace.CompanionAppArguments,
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    private static WorkspaceAuthorizationResult BuildResult(
        bool allowed,
        WorkspaceIssueCode? primary,
        IReadOnlyList<WorkspaceIssue> issues,
        IReadOnlyList<WorkspaceRisk> risks,
        string? directory,
        string? url,
        string? executable,
        string? arguments,
        string? command,
        long revision) =>
        new(
            allowed,
            primary,
            issues,
            risks,
            new WorkspaceEffectiveValues(directory, url, executable, directory, arguments, command),
            revision);

    private static bool IsLocalDirectoryPath(string? directory) =>
        !string.IsNullOrWhiteSpace(directory)
        && directory.Length >= 3
        && char.IsLetter(directory[0])
        && directory[1] == ':'
        && (directory[2] == '\\' || directory[2] == '/')
        && !directory.StartsWith("\\\\", StringComparison.Ordinal)
        && !directory.Contains('%', StringComparison.Ordinal)
        && Directory.Exists(directory);
}
