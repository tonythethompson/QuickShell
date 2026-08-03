using System.Linq;
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
    private static readonly WorkspaceIssueCode[] DefaultPrecedence =
    [
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
    ];

    private static readonly WorkspaceIssueCode[] CopyPathPrecedence =
    [
        WorkspaceIssueCode.InvalidDirectory,
    ];

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
        WorkspaceAction action) =>
        AuthorizeCore(workspace, action, null);

    public static WorkspaceAuthorizationResult AuthorizeLaunchEntry(
        StoredWorkspace workspace,
        WorkspaceEntry launch) =>
        AuthorizeCore(workspace, WorkspaceAction.LaunchEntry, launch);

    public static WorkspaceAuthorizationResult AuthorizeCompanion(
        StoredWorkspace workspace,
        CompanionAppEntry companion)
    {
        var content = WorkspaceClone.Clone(workspace.Content);
        content.CompanionApps = [CompanionAppNormalization.CloneEntry(companion)];
        content.CompanionAppPath = null;
        content.CompanionAppArguments = null;
        content.OpenCompanionAppOnLaunch = false;
        return Authorize(
            workspace with { Content = content },
            WorkspaceAction.StartCompanion);
    }

    private static WorkspaceAuthorizationResult AuthorizeCore(
        StoredWorkspace workspace,
        WorkspaceAction action,
        WorkspaceEntry? selectedLaunch)
    {
        var content = workspace.Content;
        var issues = new List<WorkspaceIssue>();
        var risks = new List<WorkspaceRisk>();
        string? normalizedDirectory = null;
        string? normalizedUrl = null;
        string? executablePath = null;
        string? arguments = null;
        var rawDirectory = content.Directory ?? string.Empty;

        if (RequiresDirectory(action))
        {
            ValidateDirectory(
                rawDirectory,
                action is WorkspaceAction.LaunchTerminal or WorkspaceAction.LaunchEntry or WorkspaceAction.GrantTrust,
                issues,
                out normalizedDirectory);
        }

        if (!string.IsNullOrEmpty(content.Command))
        {
            risks.Add(new("command", "This workspace contains a command that can execute arbitrary code."));
        }

        if (content.RunAsAdmin || (content.Launches ?? []).Any(launch => launch.RunAsAdmin))
        {
            risks.Add(new("elevation", "This workspace can request an elevated process and UAC."));
        }

        switch (action)
        {
            case WorkspaceAction.LaunchTerminal:
                ValidateWorkspaceLaunch(content, issues);
                break;
            case WorkspaceAction.LaunchEntry:
                ValidateSelectedLaunch(selectedLaunch, issues);
                break;
            case WorkspaceAction.StartCompanion:
                ValidateCompanions(content, normalizedDirectory, issues, risks, out executablePath, out arguments);
                break;
            case WorkspaceAction.OpenUrl:
            case WorkspaceAction.OpenDevServer:
                ValidateUrl(content.DevServerUrl, true, "No valid HTTP(S) URL is configured.", issues, out normalizedUrl);
                break;
            case WorkspaceAction.GrantTrust:
                ValidateWorkspaceLaunch(content, issues);
                ValidateUrl(content.DevServerUrl, false, "Dev-server URL must be an absolute HTTP(S) URL.", issues, out normalizedUrl);
                ValidateUrl(content.RepoUrl, false, "Repository URL must be an absolute HTTP(S) URL.", issues, out _);
                ValidateCompanions(content, normalizedDirectory, issues, risks, out executablePath, out arguments);
                break;
        }

        AssessAdditionalRisks(content, action, risks);
        ValidateDirectoryTrust(workspace, action, normalizedDirectory, issues);

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

    private static void AssessAdditionalRisks(TerminalShortcut content, WorkspaceAction action, List<WorkspaceRisk> risks)
    {
        var configuredCompanionCount = CompanionAppNormalization.GetConfigured(content).Count;
        if (configuredCompanionCount > 0 && action is not WorkspaceAction.StartCompanion and not WorkspaceAction.GrantTrust)
        {
            risks.Add(new("companions", $"This workspace can start {configuredCompanionCount} companion process(es)."));
        }

        if (content.OpenDevServerOnLaunch && !string.IsNullOrWhiteSpace(content.DevServerUrl))
        {
            risks.Add(new("dev-server", "This workspace opens a configured URL after launch."));
        }
    }

    private static void ValidateDirectoryTrust(StoredWorkspace workspace, WorkspaceAction action, string? normalizedDirectory, List<WorkspaceIssue> issues)
    {
        if (WorkspaceTrustFeatures.Enabled && !workspace.Security.IsTrusted && RequiresTrust(action))
        {
            issues.Add(new(WorkspaceIssueCode.WorkspaceUntrusted, "Trust this workspace before starting external processes or opening it."));
        }

        if (action == WorkspaceAction.OpenDirectory)
        {
            if (WorkspaceTrustFeatures.Enabled && !workspace.Security.IsTrusted)
            {
                issues.Add(new(WorkspaceIssueCode.DirectoryOpenNotAllowed, "Untrusted workspaces cannot open directories."));
            }
            else if (!IsLocalDirectoryPath(normalizedDirectory))
            {
                issues.Add(new(WorkspaceIssueCode.DirectoryOpenNotAllowed, "Only existing rooted local drive directories can be opened in Explorer."));
            }
        }
    }

    private static bool RequiresDirectory(WorkspaceAction action) =>
        action is WorkspaceAction.LaunchTerminal
            or WorkspaceAction.LaunchEntry
            or WorkspaceAction.StartCompanion
            or WorkspaceAction.OpenDirectory
            or WorkspaceAction.CopyPath
            or WorkspaceAction.GrantTrust;

    private static bool RequiresTrust(WorkspaceAction action) =>
        action is WorkspaceAction.LaunchTerminal
            or WorkspaceAction.LaunchEntry
            or WorkspaceAction.StartCompanion
            or WorkspaceAction.OpenUrl
            or WorkspaceAction.OpenDevServer
            or WorkspaceAction.OpenDirectory;

    private static void ValidateDirectory(
        string rawDirectory,
        bool requireExisting,
        List<WorkspaceIssue> issues,
        out string? normalizedDirectory)
    {
        normalizedDirectory = null;
        if (rawDirectory.Length > ShortcutValidation.MaxDirectoryLength
            || rawDirectory.IndexOfAny(['\r', '\n', '\0']) >= 0
            || !ShortcutValidation.TryNormalizeDirectory(rawDirectory, out normalizedDirectory, out _))
        {
            issues.Add(new(WorkspaceIssueCode.InvalidDirectory, "Workspace directory is not a valid rooted path."));
            return;
        }

        if (requireExisting && !ShortcutValidation.DirectoryExists(normalizedDirectory))
        {
            issues.Add(new(WorkspaceIssueCode.DirectoryMissing, "Workspace directory does not exist."));
        }
    }

    private static void ValidateWorkspaceLaunch(
        TerminalShortcut content,
        List<WorkspaceIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(content.Name) || content.Name.Length > ShortcutValidation.MaxNameLength)
        {
            issues.Add(new(WorkspaceIssueCode.InvalidLaunch, "Workspace name is missing or exceeds the limit."));
        }

        if (!ShortcutValidation.TryValidateCommand(content.Command, out _))
        {
            issues.Add(new(WorkspaceIssueCode.InvalidCommand, "Workspace command contains invalid control characters or exceeds the limit."));
        }

        if (!ShortcutLaunchNormalization.TryValidateLaunches(content, out var launchesError))
        {
            issues.Add(new(WorkspaceIssueCode.InvalidLaunch, launchesError));
        }
    }

    private static void ValidateSelectedLaunch(
        WorkspaceEntry? launch,
        List<WorkspaceIssue> issues)
    {
        if (launch is null || !launch.IsEnabled)
        {
            issues.Add(new(WorkspaceIssueCode.InvalidLaunch, "The selected launch entry is unavailable."));
            return;
        }

        if (string.IsNullOrWhiteSpace(launch.Label)
            || launch.Label.Length > ShortcutLaunchNormalization.MaxLabelLength
            || !ShortcutValidation.TryValidateCommand(launch.Command, out _)
            || !ShortcutValidation.TryValidateWtProfile(launch.WtProfile, out _))
        {
            issues.Add(new(WorkspaceIssueCode.InvalidLaunch, $"Launch '{launch.Label}' contains invalid label, command, or profile data."));
        }
    }

    private static void ValidateUrl(
        string? url,
        bool required,
        string message,
        List<WorkspaceIssue> issues,
        out string? normalizedUrl)
    {
        if (!ShortcutValidation.TryValidateOptionalLinkUrl(url, out _, out normalizedUrl)
            || (required && normalizedUrl is null))
        {
            issues.Add(new(WorkspaceIssueCode.InvalidUrl, message));
        }
    }

    private static void ValidateCompanions(
        TerminalShortcut content,
        string? normalizedDirectory,
        List<WorkspaceIssue> issues,
        List<WorkspaceRisk> risks,
        out string? executablePath,
        out string? arguments)
    {
        executablePath = null;
        arguments = null;
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

            var invalidArguments = !string.IsNullOrEmpty(companion.Arguments)
                && (companion.Arguments.Length > ShortcutValidation.MaxCompanionAppArgumentsLength
                    || companion.Arguments.IndexOfAny(['\r', '\n', '\0']) >= 0);
            if (companion.Path.Length > ShortcutValidation.MaxCompanionAppPathLength
                || companion.Path.IndexOfAny(['\r', '\n', '\0']) >= 0
                || invalidArguments)
            {
                issues.Add(new(WorkspaceIssueCode.InvalidCompanion, "Companion executable or arguments contain invalid characters or exceed the limit."));
                continue;
            }

            if (!CompanionAppCatalog.TryResolveExecutablePath(companion.Path, out var resolved))
            {
                issues.Add(new(WorkspaceIssueCode.CompanionExecutableUnavailable, $"Companion executable was not found: {companion.Path}"));
                continue;
            }

            if (executablePath is not null)
            {
                continue;
            }

            executablePath = resolved;
            arguments = CompanionAppLauncher.ExpandArguments(
                companion.Arguments,
                normalizedDirectory ?? string.Empty);
        }
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

    /// <summary>
    /// Picks the highest-precedence issue for messaging. Returns null when
    /// there are no issues, or when none of the issues appear in the action's
    /// precedence list. Callers must treat <see cref="WorkspaceAuthorizationResult.PrimaryIssueCode"/>
    /// as optional; authorization itself is driven by <see cref="WorkspaceAuthorizationResult.IsAllowed"/>
    /// and the full Issues list, not by a fake enum-zero sentinel.
    /// </summary>
    private static WorkspaceIssueCode? GetPrimaryIssue(
        List<WorkspaceIssue> issues,
        WorkspaceAction action)
    {
        if (issues.Count == 0)
        {
            return null;
        }

        var precedence = action == WorkspaceAction.CopyPath
            ? CopyPathPrecedence
            : DefaultPrecedence;

        // Allocation-free nested scan; lists are small (a few issue codes).
        foreach (var code in precedence)
        {
            foreach (var issue in issues)
            {
                if (issue.Code == code)
                {
                    return code;
                }
            }
        }

        return null;
    }

    public static WorkspaceReviewToken CreateReviewToken(StoredWorkspace workspace) =>
        new(workspace.Content.Id, workspace.Revision, ComputeDigest(workspace.Content));

    public static bool MatchesReviewToken(StoredWorkspace workspace, WorkspaceReviewToken token) =>
        string.Equals(workspace.Content.Id, token.WorkspaceId, StringComparison.OrdinalIgnoreCase)
        && workspace.Revision == token.Revision
        && string.Equals(ComputeDigest(workspace.Content), token.Digest, StringComparison.Ordinal);

    /// <summary>
    /// Computes a hexadecimal SHA-256 digest for the workspace content.
    /// </summary>
    /// <param name="workspace">The workspace content to digest.</param>
    /// <returns>The hexadecimal SHA-256 digest of the workspace content.</returns>
    public static string ComputeDigest(TerminalShortcut workspace)
    {
        // Named DTO + QuickShellDigestJsonContext (not QuickShellJsonContext): the digest
        // context mirrors default Serialize(object) bytes so existing WorkspaceReviewToken
        // values keep matching. Field set/order below is load-bearing.
        var digestPayload = new WorkspaceDigestPayload(
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
            workspace.CompanionAppArguments);
        var payload = JsonSerializer.Serialize(
            digestPayload,
            QuickShellDigestJsonContext.Default.WorkspaceDigestPayload);
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
