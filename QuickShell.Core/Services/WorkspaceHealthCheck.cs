using QuickShell.Abstractions;
using QuickShell.Models;
using System.Net;
using System.Text.RegularExpressions;

namespace QuickShell.Services;

internal enum WorkspaceHealthSeverity
{
    Info,
    Warning,
    Error,
}

internal enum WorkspaceHealthFindingKind
{
    MissingFolder,
    InvalidLaunch,
    MissingTerminal,
    MissingProfile,
    MissingWslDistro,
    MissingExecutable,
    GitState,
    PortInUse,
    ExistingProcess,
}

internal sealed record WorkspaceHealthFinding(
    WorkspaceHealthSeverity Severity,
    WorkspaceHealthFindingKind Kind,
    string Title,
    string? Detail = null)
{
    public bool IsBlocking => Severity == WorkspaceHealthSeverity.Error;

    public bool IsRunningSignal =>
        Kind is WorkspaceHealthFindingKind.PortInUse or WorkspaceHealthFindingKind.ExistingProcess;
}

internal sealed class WorkspaceHealthResult
{
    public WorkspaceHealthResult(IReadOnlyList<WorkspaceHealthFinding> findings)
    {
        Findings = findings;
    }

    public IReadOnlyList<WorkspaceHealthFinding> Findings { get; }

    public bool HasBlockingErrors => Findings.Any(finding => finding.IsBlocking);

    public bool HasRunningSignal => Findings.Any(finding => finding.IsRunningSignal);

    public IReadOnlyList<WorkspaceHealthFinding> BlockingFindings =>
        Findings.Where(finding => finding.IsBlocking).ToList();

    public IReadOnlyList<WorkspaceHealthFinding> WarningFindings =>
        Findings.Where(finding => finding.Severity == WorkspaceHealthSeverity.Warning).ToList();
}

/// <summary>
/// Production health checks for a workspace. Environment and git IO are injected.
/// </summary>
internal sealed partial class WorkspaceHealthCheck : IWorkspaceHealthChecker
{
    private static readonly string[] ShellBuiltins =
    [
        "cd",
        "cls",
        "copy",
        "del",
        "dir",
        "echo",
        "exit",
        "md",
        "mkdir",
        "move",
        "rd",
        "ren",
        "rmdir",
        "set",
        "start",
        "type",
    ];

    private readonly IWorkspaceEnvironmentProbe _environmentProbe;
    private readonly IWorkspaceGitOperations _gitOperations;
    private readonly ITerminalCatalog _catalog;
    private readonly IWtProfilesService _profiles;

    public WorkspaceHealthCheck(
        IWorkspaceEnvironmentProbe environmentProbe,
        IWorkspaceGitOperations gitOperations,
        ITerminalCatalog catalog,
        IWtProfilesService profiles)
    {
        _environmentProbe = environmentProbe ?? throw new ArgumentNullException(nameof(environmentProbe));
        _gitOperations = gitOperations ?? throw new ArgumentNullException(nameof(gitOperations));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
    }

    /// <summary>
    /// Launch-safety / status snapshot path. Can fan into directory, launch, git, ports, and
    /// process checks. Keep <paramref name="includeVolatile"/> and git off the typing/search
    /// list rebuild path — CmdPal list tags use <see cref="WorkspaceStatusService.TryGetCached"/> only.
    /// </summary>
    public WorkspaceHealthResult Check(
        TerminalShortcut shortcut,
        string terminalApplicationId,
        string defaultProfileId,
        bool includeVolatile = true,
        bool includeGit = true)
    {
        ShortcutLaunchNormalization.EnsureLaunchesFromLegacy(shortcut);
        var findings = new List<WorkspaceHealthFinding>();

        CheckDirectory(shortcut, findings);
        CheckLaunches(shortcut, terminalApplicationId, defaultProfileId, findings);
        if (includeGit)
        {
            CheckGit(shortcut, findings);
        }

        if (includeVolatile)
        {
            CheckPorts(shortcut, findings);
            CheckProcesses(shortcut, findings);
        }

        return new WorkspaceHealthResult(Deduplicate(findings));
    }

    public WorkspaceHealthResult CheckEntry(
        TerminalShortcut shortcut,
        WorkspaceEntry launch,
        string terminalApplicationId,
        string defaultProfileId,
        bool includeVolatile = true,
        bool includeGit = true)
    {
        var enabled = ShortcutLaunchNormalization.GetEnabledLaunches(shortcut);
        var index = 0;
        for (var i = 0; i < enabled.Count; i++)
        {
            if (string.Equals(enabled[i].Id, launch.Id, StringComparison.Ordinal))
            {
                index = i;
                break;
            }
        }

        var resolved = _catalog.ResolveLaunchEntry(launch, enabled, index);
        var scoped = new TerminalShortcut
        {
            Id = shortcut.Id,
            Name = shortcut.Name,
            Directory = shortcut.Directory,
            Terminal = resolved.Terminal,
            WtProfile = resolved.WtProfile,
            RunAsAdmin = shortcut.RunAsAdmin,
            DevServerUrl = shortcut.DevServerUrl,
            RepoUrl = shortcut.RepoUrl,
            Launches = [resolved],
        };

        return Check(scoped, terminalApplicationId, defaultProfileId, includeVolatile, includeGit);
    }

    public static string FormatBlockingSummary(WorkspaceHealthResult result) =>
        FormatFindings("Workspace needs attention", result.BlockingFindings);

    public static string FormatWarningSummary(WorkspaceHealthResult result) =>
        FormatFindings("Launched with warnings", result.WarningFindings);

    public static string FormatFindingsEvidence(IReadOnlyList<WorkspaceHealthFinding> findings) =>
        findings.Count == 0
            ? "No current issues"
            : string.Join(" · ", findings.Select(FormatFinding));

    public static string FormatDetailedSummary(WorkspaceHealthResult result)
    {
        if (result.Findings.Count == 0)
        {
            return "Workspace health: no issues found.";
        }

        return $"Workspace health: {string.Join(" ", result.Findings.Select(FormatFinding))}";
    }

    private static void CheckDirectory(TerminalShortcut shortcut, List<WorkspaceHealthFinding> findings)
    {
        if (!ShortcutValidation.TryNormalizeDirectory(shortcut.Directory, out var normalized, out var error))
        {
            findings.Add(new WorkspaceHealthFinding(
                WorkspaceHealthSeverity.Error,
                WorkspaceHealthFindingKind.MissingFolder,
                "Workspace folder is invalid.",
                error));
            return;
        }

        if (!ShortcutValidation.DirectoryExists(normalized))
        {
            findings.Add(new WorkspaceHealthFinding(
                WorkspaceHealthSeverity.Error,
                WorkspaceHealthFindingKind.MissingFolder,
                "Workspace folder not found.",
                normalized));
        }
    }

    private void CheckLaunches(
        TerminalShortcut shortcut,
        string terminalApplicationId,
        string defaultProfileId,
        List<WorkspaceHealthFinding> findings)
    {
        if (ShortcutLaunchNormalization.GetEnabledLaunches(shortcut).Count == 0)
        {
            findings.Add(new WorkspaceHealthFinding(
                WorkspaceHealthSeverity.Error,
                WorkspaceHealthFindingKind.InvalidLaunch,
                "Workspace has no enabled launch entries."));
            return;
        }

        if (!ShortcutLaunchNormalization.TryValidateLaunches(shortcut, out var launchError))
        {
            findings.Add(new WorkspaceHealthFinding(
                WorkspaceHealthSeverity.Error,
                WorkspaceHealthFindingKind.InvalidLaunch,
                "Launch entries are invalid.",
                launchError));
            return;
        }

        var enabledLaunches = ShortcutLaunchNormalization.GetEnabledLaunches(shortcut);
        for (var index = 0; index < enabledLaunches.Count; index++)
        {
            var launch = enabledLaunches[index];
            var resolved = _catalog.ResolveLaunchEntry(launch, enabledLaunches, index);
            CheckLaunchTarget(resolved, terminalApplicationId, defaultProfileId, findings);
            CheckCommandExecutable(resolved, findings);
        }
    }

    private void CheckLaunchTarget(
        WorkspaceEntry launch,
        string terminalApplicationId,
        string defaultProfileId,
        List<WorkspaceHealthFinding> findings)
    {
        var terminal = (launch.Terminal ?? "default").Trim().ToLowerInvariant();
        var profile = launch.WtProfile?.Trim();

        if (terminal == "default")
        {
            CheckDefaultLaunchTarget(terminalApplicationId, defaultProfileId, findings);
            return;
        }

        if (terminal is "wt" or "it")
        {
            if (!_catalog.HasTerminalApplication(terminal))
            {
                findings.Add(new WorkspaceHealthFinding(
                    WorkspaceHealthSeverity.Error,
                    WorkspaceHealthFindingKind.MissingTerminal,
                    $"{TerminalHostIds.SourceLabel(terminal)} was not found."));
            }

            if (!string.IsNullOrWhiteSpace(profile) && _profiles.FindProfileForLaunch(terminal, profile) is null)
            {
                findings.Add(new WorkspaceHealthFinding(
                    WorkspaceHealthSeverity.Error,
                    WorkspaceHealthFindingKind.MissingProfile,
                    $"Terminal profile '{profile}' was not found."));
            }

            return;
        }

        if (terminal == "wsl")
        {
            CheckWsl(profile, findings);
            return;
        }

        if (terminal == "powershell" && !ExecutableExists("powershell.exe"))
        {
            findings.Add(new WorkspaceHealthFinding(
                WorkspaceHealthSeverity.Error,
                WorkspaceHealthFindingKind.MissingTerminal,
                "PowerShell was not found."));
        }
        else if (terminal == "pwsh" && !ExecutableExists("pwsh.exe"))
        {
            findings.Add(new WorkspaceHealthFinding(
                WorkspaceHealthSeverity.Error,
                WorkspaceHealthFindingKind.MissingTerminal,
                "PowerShell 7 was not found."));
        }
        else if (terminal == "cmd" && !ExecutableExists("cmd.exe"))
        {
            findings.Add(new WorkspaceHealthFinding(
                WorkspaceHealthSeverity.Error,
                WorkspaceHealthFindingKind.MissingTerminal,
                "Command Prompt was not found."));
        }
    }

    private void CheckDefaultLaunchTarget(
        string terminalApplicationId,
        string defaultProfileId,
        List<WorkspaceHealthFinding> findings)
    {
        if (!_catalog.HasTerminalApplication(terminalApplicationId))
        {
            findings.Add(new WorkspaceHealthFinding(
                WorkspaceHealthSeverity.Error,
                WorkspaceHealthFindingKind.MissingTerminal,
                $"{TerminalHostIds.SourceLabel(terminalApplicationId)} was not found."));
            return;
        }

        if (_catalog.IsStandaloneShellLaunchTarget(defaultProfileId))
        {
            var normalizedDefault = _catalog.NormalizeLaunchTargetId(defaultProfileId);
            var defaultLaunch = normalizedDefault.StartsWith("wsl:", StringComparison.OrdinalIgnoreCase)
                ? new WorkspaceEntry
                {
                    Label = "Default",
                    Terminal = "wsl",
                    WtProfile = normalizedDefault[4..],
                    IsEnabled = true,
                }
                : new WorkspaceEntry
                {
                    Label = "Default",
                    Terminal = normalizedDefault,
                    IsEnabled = true,
                };

            CheckLaunchTarget(
                defaultLaunch,
                terminalApplicationId,
                TerminalHostIds.DefaultProfile,
                findings);
            return;
        }

        if (defaultProfileId.Equals(TerminalHostIds.DefaultProfile, StringComparison.OrdinalIgnoreCase)
            || !TerminalHostIds.UsesWindowsTerminalProfiles(terminalApplicationId))
        {
            return;
        }

        if (!_profiles.GetProfilesForApplication(terminalApplicationId)
                .Any(profile => profile.Name.Equals(defaultProfileId, StringComparison.OrdinalIgnoreCase)))
        {
            findings.Add(new WorkspaceHealthFinding(
                WorkspaceHealthSeverity.Error,
                WorkspaceHealthFindingKind.MissingProfile,
                $"Default terminal profile '{defaultProfileId}' was not found."));
        }
    }

    private void CheckWsl(string? distro, List<WorkspaceHealthFinding> findings)
    {
        if (!ExecutableExists("wsl.exe"))
        {
            findings.Add(new WorkspaceHealthFinding(
                WorkspaceHealthSeverity.Error,
                WorkspaceHealthFindingKind.MissingTerminal,
                "WSL executable was not found."));
            return;
        }

        if (string.IsNullOrWhiteSpace(distro))
        {
            return;
        }

        if (!GetWslDistroNames().Contains(distro, StringComparer.OrdinalIgnoreCase))
        {
            findings.Add(new WorkspaceHealthFinding(
                WorkspaceHealthSeverity.Error,
                WorkspaceHealthFindingKind.MissingWslDistro,
                $"WSL distro '{distro}' was not found."));
        }
    }

    private void CheckCommandExecutable(WorkspaceEntry launch, List<WorkspaceHealthFinding> findings)
    {
        var executable = TryReadCommandExecutable(launch.Command);
        if (executable is null || ShellBuiltins.Contains(executable, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        if (!ExecutableExists(executable))
        {
            findings.Add(new WorkspaceHealthFinding(
                WorkspaceHealthSeverity.Error,
                WorkspaceHealthFindingKind.MissingExecutable,
                $"Required executable '{executable}' was not found.",
                launch.Label));
        }
    }

    private void CheckGit(TerminalShortcut shortcut, List<WorkspaceHealthFinding> findings)
    {
        if (!_gitOperations.TryGetStatus(shortcut.Directory, out var status))
        {
            return;
        }

        var detail = status.IsDirty
            ? "Working tree has uncommitted changes."
            : "Working tree is clean.";
        findings.Add(new WorkspaceHealthFinding(
            WorkspaceHealthSeverity.Info,
            WorkspaceHealthFindingKind.GitState,
            $"Git branch: {status.Branch}",
            detail));
    }

    private void CheckPorts(TerminalShortcut shortcut, List<WorkspaceHealthFinding> findings)
    {
        foreach (var port in DetectPorts(shortcut).Distinct().Where(port => port is > 0 and <= 65535))
        {
            if (!IsPortInUse(port))
            {
                continue;
            }

            if (!ShouldTreatPortAsRunningSignal(shortcut, port))
            {
                continue;
            }

            findings.Add(new WorkspaceHealthFinding(
                WorkspaceHealthSeverity.Warning,
                WorkspaceHealthFindingKind.PortInUse,
                $"Port {port} is already in use.",
                "The dev server may already be running."));
        }
    }

    private static bool ShouldTreatPortAsRunningSignal(TerminalShortcut shortcut, int port)
    {
        if (shortcut.OpenDevServerOnLaunch
            && Uri.TryCreate(shortcut.DevServerUrl, UriKind.Absolute, out var uri)
            && uri.Port == port)
        {
            return true;
        }

        foreach (var launch in ShortcutLaunchNormalization.GetEnabledLaunches(shortcut))
        {
            var command = launch.Command ?? string.Empty;
            foreach (Match match in CommandPortRegex().Matches(command))
            {
                if (int.TryParse(match.Groups[1].Value, out var commandPort) && commandPort == port)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private void CheckProcesses(TerminalShortcut shortcut, List<WorkspaceHealthFinding> findings)
    {
        var processNames = GetProcessNames();
        if (processNames.Count == 0)
        {
            return;
        }

        foreach (var launch in ShortcutLaunchNormalization.GetEnabledLaunches(shortcut))
        {
            var executable = TryReadCommandExecutable(launch.Command);
            if (executable is null || IsGenericShellExecutable(executable))
            {
                continue;
            }

            var processName = Path.GetFileNameWithoutExtension(executable);
            if (string.IsNullOrWhiteSpace(processName))
            {
                continue;
            }

            if (processNames.Contains(processName, StringComparer.OrdinalIgnoreCase))
            {
                if (!ShouldReportExistingProcess(shortcut, launch, processName))
                {
                    continue;
                }

                findings.Add(new WorkspaceHealthFinding(
                    WorkspaceHealthSeverity.Warning,
                    WorkspaceHealthFindingKind.ExistingProcess,
                    $"Existing '{processName}' process detected.",
                    launch.Label));
            }
        }
    }

    private static List<WorkspaceHealthFinding> Deduplicate(List<WorkspaceHealthFinding> findings)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unique = new List<WorkspaceHealthFinding>();
        foreach (var finding in findings)
        {
            var key = $"{finding.Severity}|{finding.Kind}|{finding.Title}|{finding.Detail}";
            if (seen.Add(key))
            {
                unique.Add(finding);
            }
        }

        return unique;
    }

    private static IEnumerable<int> DetectPorts(TerminalShortcut shortcut)
    {
        if (Uri.TryCreate(shortcut.DevServerUrl, UriKind.Absolute, out var uri)
            && uri.Port > 0
            && (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                || IPAddress.TryParse(uri.Host, out _)))
        {
            yield return uri.Port;
        }

        foreach (var launch in ShortcutLaunchNormalization.GetEnabledLaunches(shortcut))
        {
            var command = launch.Command ?? string.Empty;
            foreach (Match match in CommandPortRegex().Matches(command))
            {
                if (int.TryParse(match.Groups[1].Value, out var port))
                {
                    yield return port;
                }
            }
        }
    }

    private static string? TryReadCommandExecutable(string? command)
    {
        var trimmed = command?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return null;
        }

        if (trimmed.StartsWith('"'))
        {
            var endQuote = trimmed.IndexOf('"', 1);
            return endQuote > 1 ? Path.GetFileName(trimmed[1..endQuote]) : null;
        }

        // Bolt: Performance optimization - use IndexOfAny to avoid string array allocation from Split
        var spaceIndex = trimmed.AsSpan().IndexOfAny(' ', '\t');
        var token = spaceIndex >= 0 ? trimmed[..spaceIndex] : trimmed;

        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        return Path.GetFileName(token);
    }

    private static bool ShouldReportExistingProcess(
        TerminalShortcut shortcut,
        WorkspaceEntry launch,
        string processName)
    {
        if (!IsLikelyGlobalProcessName(processName))
        {
            return true;
        }

        var command = launch.Command ?? string.Empty;
        if (string.IsNullOrWhiteSpace(command))
        {
            return false;
        }

        if (!ShortcutValidation.TryNormalizeDirectory(shortcut.Directory, out var directory, out _))
        {
            return false;
        }

        return command.Contains(directory, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLikelyGlobalProcessName(string processName) =>
        processName.Equals("node", StringComparison.OrdinalIgnoreCase)
        || processName.Equals("npm", StringComparison.OrdinalIgnoreCase)
        || processName.Equals("npx", StringComparison.OrdinalIgnoreCase)
        || processName.Equals("dotnet", StringComparison.OrdinalIgnoreCase)
        || processName.Equals("python", StringComparison.OrdinalIgnoreCase)
        || processName.Equals("py", StringComparison.OrdinalIgnoreCase)
        || processName.Equals("code", StringComparison.OrdinalIgnoreCase)
        || processName.Equals("cursor", StringComparison.OrdinalIgnoreCase)
        || processName.Equals("pwsh", StringComparison.OrdinalIgnoreCase)
        || processName.Equals("powershell", StringComparison.OrdinalIgnoreCase)
        || processName.Equals("cmd", StringComparison.OrdinalIgnoreCase)
        || processName.Equals("WindowsTerminal", StringComparison.OrdinalIgnoreCase)
        || processName.Equals("wt", StringComparison.OrdinalIgnoreCase)
        || processName.Equals("OpenConsole", StringComparison.OrdinalIgnoreCase)
        || processName.Equals("conhost", StringComparison.OrdinalIgnoreCase);

    private static bool IsGenericShellExecutable(string executable)
    {
        var name = Path.GetFileNameWithoutExtension(executable);
        return name.Equals("cmd", StringComparison.OrdinalIgnoreCase)
            || name.Equals("powershell", StringComparison.OrdinalIgnoreCase)
            || name.Equals("pwsh", StringComparison.OrdinalIgnoreCase)
            || name.Equals("wsl", StringComparison.OrdinalIgnoreCase)
            || name.Equals("wt", StringComparison.OrdinalIgnoreCase)
            || name.Equals("wtai", StringComparison.OrdinalIgnoreCase)
            || name.Equals("windowsterminal", StringComparison.OrdinalIgnoreCase)
            || name.Equals("openconsole", StringComparison.OrdinalIgnoreCase)
            || name.Equals("conhost", StringComparison.OrdinalIgnoreCase)
            || name.Equals("git", StringComparison.OrdinalIgnoreCase);
    }

    private bool ExecutableExists(string executable) => _environmentProbe.ExecutableExists(executable);

    private bool IsPortInUse(int port) => _environmentProbe.PortInUse(port);

    private IReadOnlyList<string> GetProcessNames() => _environmentProbe.ProcessNames();

    private IReadOnlyList<string> GetWslDistroNames() => _environmentProbe.WslDistroNames();

    private static string FormatFindings(string prefix, IReadOnlyList<WorkspaceHealthFinding> findings)
    {
        if (findings.Count == 0)
        {
            return $"{prefix}: no issues found.";
        }

        return $"{prefix}: {string.Join(" ", findings.Select(FormatFinding))}";
    }

    private static string FormatFinding(WorkspaceHealthFinding finding) =>
        string.IsNullOrWhiteSpace(finding.Detail)
            ? finding.Title
            : $"{finding.Title} {finding.Detail}";

    [GeneratedRegex(@"(?:localhost:|--port\s+|-p\s+|=)(\d{2,5})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CommandPortRegex();
}
