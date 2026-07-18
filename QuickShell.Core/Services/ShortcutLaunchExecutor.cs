using QuickShell.Abstractions;
using QuickShell.Models;
using System.ComponentModel;

namespace QuickShell.Services;

internal readonly record struct ShortcutLaunchOptions(
    bool RunAsAdmin = false,
    bool RunAsStandard = false,
    bool IncludeCompanionApp = true,
    bool IncludeDevServerLink = true,
    bool BlockDirtyBranchSwitch = true,
    bool SeparateWindowsForMultiLaunch = false);

internal sealed class ShortcutLaunchResult
{
    public bool Dismiss { get; init; }

    public string? StayOpenMessage { get; init; }

    public bool MarkUsed { get; init; }

    public LaunchDiagnosticsReport? Diagnostics { get; init; }

    public static ShortcutLaunchResult Dismissed(bool markUsed = true, LaunchDiagnosticsReport? diagnostics = null) =>
        new() { Dismiss = true, MarkUsed = markUsed, Diagnostics = diagnostics };

    public static ShortcutLaunchResult StayOpen(
        string message,
        bool markUsed = false,
        LaunchDiagnosticsReport? diagnostics = null) =>
        new() { Dismiss = false, StayOpenMessage = message, MarkUsed = markUsed, Diagnostics = diagnostics };
}

internal sealed class ShortcutLaunchExecutor : IShortcutLaunchExecutor
{
    private readonly ITerminalLauncher _terminalLauncher;
    private readonly IWorkspaceHealthChecker _healthChecker;
    private readonly ICompanionAppLauncher _companionAppLauncher;
    private readonly WorkspaceGitLaunchGate _gitLaunchGate;

    public ShortcutLaunchExecutor(
        ITerminalLauncher terminalLauncher,
        IWorkspaceHealthChecker healthChecker,
        ICompanionAppLauncher companionAppLauncher,
        WorkspaceGitLaunchGate gitLaunchGate)
    {
        _terminalLauncher = terminalLauncher ?? throw new ArgumentNullException(nameof(terminalLauncher));
        _healthChecker = healthChecker ?? throw new ArgumentNullException(nameof(healthChecker));
        _companionAppLauncher = companionAppLauncher ?? throw new ArgumentNullException(nameof(companionAppLauncher));
        _gitLaunchGate = gitLaunchGate ?? throw new ArgumentNullException(nameof(gitLaunchGate));
    }

    public ShortcutLaunchResult Launch(
        TerminalShortcut shortcut,
        string terminalApplicationId,
        string defaultProfileId,
        ShortcutLaunchOptions options = default)
    {
        ShortcutLaunchNormalization.EnsureLaunchesFromLegacy(shortcut);
        var diagnostics = new LaunchDiagnosticsReport(shortcut.Name, DateTimeOffset.UtcNow);

        WorkspaceHealthResult health;
        using (StartupPerformanceTrace.Measure("launch health check"))
        {
            health = _healthChecker.Check(
                shortcut,
                terminalApplicationId,
                defaultProfileId,
                includeGit: false);
            AddHealthDiagnostics(diagnostics, health);
        }

        if (health.HasBlockingErrors)
        {
            return ShortcutLaunchResult.StayOpen(
                WorkspaceHealthCheck.FormatBlockingSummary(health),
                diagnostics: diagnostics);
        }

        if (!TryGetLaunchDirectory(shortcut.Directory, out var launchDirectory, out var directoryError))
        {
            diagnostics.AddError(LaunchDiagnosticKind.HealthError, "Workspace folder could not be used.", directoryError);
            return ShortcutLaunchResult.StayOpen(directoryError, diagnostics: diagnostics);
        }

        WorkspaceGitLaunchGateResult gitGate;
        using (StartupPerformanceTrace.Measure("launch git gate"))
        {
            gitGate = _gitLaunchGate.EvaluateBeforeLaunch(
                launchDirectory,
                options.BlockDirtyBranchSwitch);
        }

        if (!gitGate.CanProceed)
        {
            diagnostics.AddError(
                LaunchDiagnosticKind.HealthError,
                "Git branch switch was blocked.",
                gitGate.StayOpenMessage);
            return ShortcutLaunchResult.StayOpen(
                gitGate.StayOpenMessage ?? "Git branch switch was blocked.",
                diagnostics: diagnostics);
        }

        var enabledLaunches = ShortcutLaunchNormalization.GetEnabledLaunches(shortcut);
        if (enabledLaunches.Count == 0)
        {
            diagnostics.AddError(LaunchDiagnosticKind.HealthError, "Workspace has no enabled launch entries.");
            return ShortcutLaunchResult.StayOpen(
                "Workspace has no enabled launch entries.",
                diagnostics: diagnostics);
        }

        var companionAttempted = options.IncludeCompanionApp
            && _companionAppLauncher.ShouldLaunchOnWorkspaceOpen(shortcut);

        if (enabledLaunches.Count == 1)
        {
            return LaunchSingle(
                shortcut,
                enabledLaunches[0],
                terminalApplicationId,
                defaultProfileId,
                options,
                companionAttempted,
                health.WarningFindings,
                diagnostics);
        }

        return LaunchAll(
            shortcut,
            enabledLaunches,
            terminalApplicationId,
            defaultProfileId,
            options,
            companionAttempted,
            health.WarningFindings,
            diagnostics);
    }

    public ShortcutLaunchResult LaunchEntry(
        TerminalShortcut shortcut,
        WorkspaceEntry launch,
        string terminalApplicationId,
        string defaultProfileId,
        ShortcutLaunchOptions options = default)
    {
        var diagnostics = new LaunchDiagnosticsReport($"{shortcut.Name} - {launch.Label}", DateTimeOffset.UtcNow);
        WorkspaceHealthResult health;
        using (StartupPerformanceTrace.Measure("launch entry health check"))
        {
            health = _healthChecker.CheckEntry(
                shortcut,
                launch,
                terminalApplicationId,
                defaultProfileId,
                includeGit: false);
            AddHealthDiagnostics(diagnostics, health);
        }

        if (health.HasBlockingErrors)
        {
            return ShortcutLaunchResult.StayOpen(
                WorkspaceHealthCheck.FormatBlockingSummary(health),
                diagnostics: diagnostics);
        }

        if (!TryGetLaunchDirectory(shortcut.Directory, out var launchDirectory, out var directoryError))
        {
            diagnostics.AddError(LaunchDiagnosticKind.HealthError, "Workspace folder could not be used.", directoryError);
            return ShortcutLaunchResult.StayOpen(directoryError, diagnostics: diagnostics);
        }

        WorkspaceGitLaunchGateResult gitGate;
        using (StartupPerformanceTrace.Measure("launch entry git gate"))
        {
            gitGate = _gitLaunchGate.EvaluateBeforeLaunch(
                launchDirectory,
                options.BlockDirtyBranchSwitch);
        }

        if (!gitGate.CanProceed)
        {
            diagnostics.AddError(
                LaunchDiagnosticKind.HealthError,
                "Git branch switch was blocked.",
                gitGate.StayOpenMessage);
            return ShortcutLaunchResult.StayOpen(
                gitGate.StayOpenMessage ?? "Git branch switch was blocked.",
                diagnostics: diagnostics);
        }

        var entryOptions = new ShortcutLaunchOptions(
            options.RunAsAdmin,
            options.RunAsStandard,
            IncludeCompanionApp: false,
            IncludeDevServerLink: false);

        return LaunchSingle(
            shortcut,
            launch,
            terminalApplicationId,
            defaultProfileId,
            entryOptions,
            companionAttempted: false,
            health.WarningFindings,
            diagnostics);
    }

    private ShortcutLaunchResult LaunchSingle(
        TerminalShortcut shortcut,
        WorkspaceEntry launch,
        string terminalApplicationId,
        string defaultProfileId,
        ShortcutLaunchOptions options,
        bool companionAttempted,
        IReadOnlyList<WorkspaceHealthFinding> preflightWarnings,
        LaunchDiagnosticsReport diagnostics)
    {
        TerminalLaunchAttempt attempt;
        try
        {
            var launchShortcut = ShortcutLaunchNormalization.ToLaunchShortcut(launch, shortcut);
            using (StartupPerformanceTrace.Measure("launch terminal open"))
            {
                attempt = _terminalLauncher.Open(
                    launchShortcut,
                    terminalApplicationId,
                    defaultProfileId,
                    options.RunAsAdmin,
                    options.RunAsStandard);
            }

            AddTerminalSuccessDiagnostics(diagnostics, launch, launchShortcut, attempt);
            diagnostics.RecordProcessStart(attempt.HostExecutable);
        }
        catch (DirectoryNotFoundException ex)
        {
            diagnostics.AddError(
                LaunchDiagnosticKind.TerminalLaunchFailed,
                $"{launch.Label} terminal failed before launch.",
                ex.Message);
            return ShortcutLaunchResult.StayOpen(
                "Failed to open terminal: the folder path could not be found.",
                diagnostics: diagnostics);
        }
        catch (InvalidOperationException ex)
        {
            diagnostics.AddError(
                LaunchDiagnosticKind.TerminalLaunchFailed,
                $"{launch.Label} terminal could not be launched.",
                ex.Message);
            return ShortcutLaunchResult.StayOpen(
                "Failed to open terminal: check the workspace settings and try again.",
                diagnostics: diagnostics);
        }
        catch (Win32Exception ex)
        {
            diagnostics.AddError(
                LaunchDiagnosticKind.TerminalLaunchFailed,
                $"{launch.Label} terminal was canceled or blocked.",
                ex.Message);
            return ShortcutLaunchResult.StayOpen(
                "Failed to open terminal: launch was canceled or blocked by the system.",
                diagnostics: diagnostics);
        }

        var (companionSucceeded, companionError) = TryLaunchCompanion(
            shortcut,
            companionAttempted,
            diagnostics);

        return BuildPostLaunchResult(
            shortcut,
            options,
            companionAttempted,
            companionSucceeded,
            companionError,
            "Workspace opened",
            preflightWarnings,
            diagnostics);
    }

    private readonly record struct EntryPlan(
        WorkspaceEntry Entry,
        ResolvedLaunch Resolved,
        bool EffectiveElevation);

    private sealed class EntryPlanGroup(List<EntryPlan> entries, string? tabHostExecutable)
    {
        public List<EntryPlan> Entries { get; } = entries;

        public string? TabHostExecutable { get; } = tabHostExecutable;
    }

    private ShortcutLaunchResult LaunchAll(
        TerminalShortcut shortcut,
        IReadOnlyList<WorkspaceEntry> enabledLaunches,
        string terminalApplicationId,
        string defaultProfileId,
        ShortcutLaunchOptions options,
        bool companionAttempted,
        IReadOnlyList<WorkspaceHealthFinding> preflightWarnings,
        LaunchDiagnosticsReport diagnostics)
    {
        var plans = new List<EntryPlan>();
        string? lastFailureLabel = null;

        foreach (var launch in enabledLaunches)
        {
            try
            {
                var launchShortcut = ShortcutLaunchNormalization.ToLaunchShortcut(launch, shortcut);
                var resolved = _terminalLauncher.Resolve(launchShortcut, terminalApplicationId, defaultProfileId);
                var effectiveElevation = !options.RunAsStandard && (options.RunAsAdmin || launch.RunAsAdmin);
                plans.Add(new EntryPlan(launch, resolved, effectiveElevation));
            }
            catch (DirectoryNotFoundException ex)
            {
                lastFailureLabel = launch.Label;
                diagnostics.AddError(
                    LaunchDiagnosticKind.TerminalLaunchFailed,
                    $"{launch.Label} terminal failed before launch.",
                    ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                lastFailureLabel = launch.Label;
                diagnostics.AddError(
                    LaunchDiagnosticKind.TerminalLaunchFailed,
                    $"{launch.Label} terminal could not be launched.",
                    ex.Message);
            }
        }

        var groups = options.SeparateWindowsForMultiLaunch
            ? plans.Select(plan => new EntryPlanGroup([plan], tabHostExecutable: null)).ToList()
            : GroupPlans(plans, terminalApplicationId);
        var openedCommands = 0;

        using (StartupPerformanceTrace.Measure("launch terminal open"))
        {
            foreach (var group in groups)
            {
                var entries = group.Entries;
                try
                {
                    if (entries.Count == 1)
                    {
                        var attempt = _terminalLauncher.OpenResolved(entries[0].Resolved, entries[0].EffectiveElevation);
                        AddTerminalSuccessDiagnostics(
                            diagnostics,
                            entries[0].Entry,
                            entries[0].Resolved.Shortcut,
                            attempt);
                        diagnostics.RecordProcessStart(attempt.HostExecutable);
                    }
                    else
                    {
                        var attempts = _terminalLauncher.OpenGroup(
                            entries.Select(p => p.Resolved).ToList(),
                            entries[0].EffectiveElevation,
                            group.TabHostExecutable);
                        for (var i = 0; i < entries.Count; i++)
                        {
                            AddTerminalSuccessDiagnostics(
                                diagnostics,
                                entries[i].Entry,
                                entries[i].Resolved.Shortcut,
                                attempts[i]);
                        }

                        if (attempts.Count > 0)
                        {
                            diagnostics.RecordProcessStart(attempts[0].HostExecutable);
                        }
                    }

                    openedCommands += entries.Count;
                }
                catch (Win32Exception ex)
                {
                    lastFailureLabel = entries[^1].Entry.Label;
                    diagnostics.AddError(
                        LaunchDiagnosticKind.TerminalLaunchFailed,
                        $"{lastFailureLabel} terminal was canceled or blocked.",
                        ex.Message);
                }
                catch (InvalidOperationException ex)
                {
                    lastFailureLabel = entries[^1].Entry.Label;
                    diagnostics.AddError(
                        LaunchDiagnosticKind.TerminalLaunchFailed,
                        $"{lastFailureLabel} terminal could not be launched.",
                        ex.Message);
                }
            }
        }

        if (openedCommands == 0)
        {
            return ShortcutLaunchResult.StayOpen(
                lastFailureLabel is null
                    ? "Workspace could not launch any commands."
                    : $"{lastFailureLabel} could not be launched.",
                diagnostics: diagnostics);
        }

        var (companionSucceeded, companionError) = TryLaunchCompanion(
            shortcut,
            companionAttempted,
            diagnostics);

        var successPrefix = openedCommands == enabledLaunches.Count
            ? "Workspace launched"
            : $"Workspace partially launched: {openedCommands} of {enabledLaunches.Count} commands launched";

        if (openedCommands < enabledLaunches.Count)
        {
            diagnostics.AddWarning(
                LaunchDiagnosticKind.PartialLaunch,
                "Workspace partially launched.",
                $"{openedCommands} of {enabledLaunches.Count} commands launched.");
        }

        return BuildPostLaunchResult(
            shortcut,
            options,
            companionAttempted,
            companionSucceeded,
            companionError,
            successPrefix,
            preflightWarnings,
            diagnostics,
            partialLaunch: openedCommands < enabledLaunches.Count);
    }

    private static List<EntryPlanGroup> GroupPlans(List<EntryPlan> plans, string terminalApplicationId)
    {
        var groups = new List<EntryPlanGroup>();
        var groupIndexByKey = new Dictionary<(string Host, bool Elevated), int>();
        var workspaceTabHostExecutable = GetWorkspaceTabHostExecutable(terminalApplicationId);

        foreach (var plan in plans)
        {
            if (!TryGetTabHostExecutable(plan.Resolved.Target, workspaceTabHostExecutable, out var tabHostExecutable))
            {
                groups.Add(new EntryPlanGroup([plan], tabHostExecutable: null));
                continue;
            }

            var key = (tabHostExecutable.ToUpperInvariant(), plan.EffectiveElevation);
            if (groupIndexByKey.TryGetValue(key, out var index))
            {
                groups[index].Entries.Add(plan);
            }
            else
            {
                groupIndexByKey[key] = groups.Count;
                groups.Add(new EntryPlanGroup([plan], tabHostExecutable));
            }
        }

        return groups;
    }

    private static string? GetWorkspaceTabHostExecutable(string terminalApplicationId)
    {
        if (!TerminalHostIds.UsesWindowsTerminalProfiles(terminalApplicationId))
        {
            return null;
        }

        return TerminalHostIds.HostExecutable(terminalApplicationId);
    }

    private static bool TryGetTabHostExecutable(
        LaunchTarget target,
        string? workspaceTabHostExecutable,
        out string tabHostExecutable)
    {
        if (target.Kind is LaunchTargetKind.WindowsTerminal or LaunchTargetKind.IntelligentTerminal)
        {
            tabHostExecutable = target.HostExecutable;
            return !string.IsNullOrWhiteSpace(tabHostExecutable);
        }

        if (workspaceTabHostExecutable is not null
            && target.Kind is LaunchTargetKind.PowerShell or LaunchTargetKind.Pwsh or LaunchTargetKind.Cmd or LaunchTargetKind.Wsl)
        {
            tabHostExecutable = workspaceTabHostExecutable;
            return true;
        }

        tabHostExecutable = string.Empty;
        return false;
    }

    private static ShortcutLaunchResult BuildPostLaunchResult(
        TerminalShortcut shortcut,
        ShortcutLaunchOptions options,
        bool companionAttempted,
        bool companionSucceeded,
        string? companionError,
        string successPrefix,
        IReadOnlyList<WorkspaceHealthFinding> preflightWarnings,
        LaunchDiagnosticsReport diagnostics,
        bool partialLaunch = false)
    {
        var warnings = new List<string>();

        if (preflightWarnings.Count > 0)
        {
            warnings.Add(WorkspaceHealthCheck.FormatWarningSummary(new WorkspaceHealthResult(preflightWarnings)));
        }

        if (companionAttempted && !companionSucceeded)
        {
            warnings.Add(FormatLaunchWarning("Companion app could not be launched.", companionError));
            diagnostics.AddWarning(
                LaunchDiagnosticKind.CompanionAppUnavailable,
                "Companion app could not be launched.",
                companionError);
        }
        else if (companionAttempted)
        {
            diagnostics.AddInfo(LaunchDiagnosticKind.CompanionAppLaunched, "Companion app launch was requested.");
        }

        if (options.IncludeDevServerLink
            && WorkspaceDevServerActions.ShouldOpenOnWorkspaceLaunch(shortcut))
        {
            if (WorkspaceDevServerActions.TryOpen(shortcut, out var devServerError))
            {
                diagnostics.AddInfo(
                    LaunchDiagnosticKind.DevServerUrlOpened,
                    "Dev server URL was opened.",
                    shortcut.DevServerUrl);
            }
            else
            {
                warnings.Add(FormatLaunchWarning("Dev server link could not be opened.", devServerError));
                diagnostics.AddWarning(
                    LaunchDiagnosticKind.DevServerUrlUnavailable,
                    "Dev server link could not be opened.",
                    devServerError);
            }
        }

        if (warnings.Count == 0 && !partialLaunch)
        {
            return ShortcutLaunchResult.Dismissed(diagnostics: diagnostics);
        }

        if (warnings.Count == 0)
        {
            return ShortcutLaunchResult.StayOpen($"{successPrefix}.", markUsed: true, diagnostics: diagnostics);
        }

        return ShortcutLaunchResult.StayOpen(
            $"{successPrefix}, but {string.Join(" ", warnings)}",
            markUsed: true,
            diagnostics: diagnostics);
    }

    private (bool Succeeded, string? Error) TryLaunchCompanion(
        TerminalShortcut shortcut,
        bool companionAttempted,
        LaunchDiagnosticsReport diagnostics)
    {
        if (!companionAttempted)
        {
            return (true, null);
        }

        using (StartupPerformanceTrace.Measure("launch companion app"))
        {
            var companionSucceeded = _companionAppLauncher.TryLaunch(shortcut, onDemand: false, out var companionError);
            diagnostics.RecordProcessStart("companion");
            return (companionSucceeded, companionError);
        }
    }

    private static void AddHealthDiagnostics(LaunchDiagnosticsReport diagnostics, WorkspaceHealthResult health)
    {
        foreach (var finding in health.Findings)
        {
            if (finding.Severity == WorkspaceHealthSeverity.Error)
            {
                diagnostics.AddError(LaunchDiagnosticKind.HealthError, finding.Title, finding.Detail);
            }
            else if (finding.Severity == WorkspaceHealthSeverity.Warning)
            {
                diagnostics.AddWarning(LaunchDiagnosticKind.HealthWarning, finding.Title, finding.Detail);
            }
        }
    }

    private static void AddTerminalSuccessDiagnostics(
        LaunchDiagnosticsReport diagnostics,
        WorkspaceEntry launch,
        TerminalShortcut launchShortcut,
        TerminalLaunchAttempt attempt)
    {
        diagnostics.AddInfo(
            LaunchDiagnosticKind.TerminalLaunched,
            $"{launch.Label} terminal handed off to {attempt.HostExecutable}.",
            FormatTargetDetail(attempt));

        if (!string.IsNullOrWhiteSpace(attempt.FallbackReason))
        {
            diagnostics.AddWarning(
                LaunchDiagnosticKind.ProfileFallback,
                "Terminal fallback occurred.",
                attempt.FallbackReason);
        }

        if (!string.IsNullOrWhiteSpace(launchShortcut.Command))
        {
            diagnostics.AddInfo(
                LaunchDiagnosticKind.CommandHandoff,
                $"{launch.Label} command was handed off.",
                launchShortcut.Command);
            diagnostics.AddInfo(
                LaunchDiagnosticKind.CommandStatusUnavailable,
                "Command exit status is not monitored.",
                "Quick Shell starts the terminal process and does not capture terminal output or command exit codes.");
        }
    }

    private static string FormatTargetDetail(TerminalLaunchAttempt attempt)
    {
        var parts = new List<string> { attempt.TargetDisplayName };
        if (!string.IsNullOrWhiteSpace(attempt.ProfileOrDistro))
        {
            parts.Add($"profile/distro: {attempt.ProfileOrDistro}");
        }

        if (attempt.RunAsAdmin)
        {
            parts.Add("elevated");
        }

        if (!string.IsNullOrWhiteSpace(attempt.Arguments))
        {
            parts.Add($"arguments: {attempt.Arguments}");
        }

        return string.Join("; ", parts);
    }

    private static bool TryGetLaunchDirectory(string? directory, out string normalizedDirectory, out string error)
    {
        if (!ShortcutValidation.TryNormalizeDirectory(directory ?? string.Empty, out normalizedDirectory, out error))
        {
            error = $"Workspace could not launch: {error}";
            return false;
        }

        if (!ShortcutValidation.DirectoryExists(normalizedDirectory))
        {
            error = $"Workspace could not launch: folder not found at {normalizedDirectory}.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static string FormatLaunchWarning(string summary, string? detail) =>
        string.IsNullOrWhiteSpace(detail)
            ? summary
            : $"{summary} {detail}";
}
