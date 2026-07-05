using QuickShell.Models;
using System.ComponentModel;

namespace QuickShell.Services;

internal readonly record struct ShortcutLaunchOptions(
    bool RunAsAdmin = false,
    bool RunAsStandard = false,
    bool IncludeCompanionApp = true,
    bool IncludeDevServerLink = true);

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

internal static class ShortcutLaunchExecutor
{
    public static ShortcutLaunchResult Launch(
        TerminalShortcut shortcut,
        string terminalApplicationId,
        string defaultProfileId,
        ShortcutLaunchOptions options = default)
    {
        ShortcutLaunchNormalization.EnsureLaunchesFromLegacy(shortcut);
        var diagnostics = new LaunchDiagnosticsReport(shortcut.Name, DateTimeOffset.UtcNow);

        var health = WorkspaceHealthCheck.Check(shortcut, terminalApplicationId, defaultProfileId);
        AddHealthDiagnostics(diagnostics, health);
        if (health.HasBlockingErrors)
        {
            return ShortcutLaunchResult.StayOpen(
                WorkspaceHealthCheck.FormatBlockingSummary(health),
                diagnostics: diagnostics);
        }

        if (!TryGetLaunchDirectory(shortcut.Directory, out _, out var directoryError))
        {
            diagnostics.AddError(LaunchDiagnosticKind.HealthError, "Workspace folder could not be used.", directoryError);
            return ShortcutLaunchResult.StayOpen(directoryError, diagnostics: diagnostics);
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
            && CompanionAppLauncher.ShouldLaunchOnWorkspaceOpen(shortcut);
        string? companionError = null;
        var companionSucceeded = !companionAttempted
            || CompanionAppLauncher.TryLaunch(shortcut, onDemand: false, out companionError);

        if (enabledLaunches.Count == 1)
        {
            return LaunchSingle(
                shortcut,
                enabledLaunches[0],
                terminalApplicationId,
                defaultProfileId,
                options,
                companionAttempted,
                companionSucceeded,
                companionError,
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
            companionSucceeded,
            companionError,
            health.WarningFindings,
            diagnostics);
    }

    public static ShortcutLaunchResult LaunchEntry(
        TerminalShortcut shortcut,
        WorkspaceEntry launch,
        string terminalApplicationId,
        string defaultProfileId,
        ShortcutLaunchOptions options = default)
    {
        var diagnostics = new LaunchDiagnosticsReport($"{shortcut.Name} - {launch.Label}", DateTimeOffset.UtcNow);
        var health = WorkspaceHealthCheck.CheckEntry(shortcut, launch, terminalApplicationId, defaultProfileId);
        AddHealthDiagnostics(diagnostics, health);
        if (health.HasBlockingErrors)
        {
            return ShortcutLaunchResult.StayOpen(
                WorkspaceHealthCheck.FormatBlockingSummary(health),
                diagnostics: diagnostics);
        }

        if (!TryGetLaunchDirectory(shortcut.Directory, out _, out var directoryError))
        {
            diagnostics.AddError(LaunchDiagnosticKind.HealthError, "Workspace folder could not be used.", directoryError);
            return ShortcutLaunchResult.StayOpen(directoryError, diagnostics: diagnostics);
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
            companionSucceeded: true,
            companionError: null,
            health.WarningFindings,
            diagnostics);
    }

    private static ShortcutLaunchResult LaunchSingle(
        TerminalShortcut shortcut,
        WorkspaceEntry launch,
        string terminalApplicationId,
        string defaultProfileId,
        ShortcutLaunchOptions options,
        bool companionAttempted,
        bool companionSucceeded,
        string? companionError,
        IReadOnlyList<WorkspaceHealthFinding> preflightWarnings,
        LaunchDiagnosticsReport diagnostics)
    {
        try
        {
            var launchShortcut = ShortcutLaunchNormalization.ToLaunchShortcut(launch, shortcut);
            var attempt = TerminalLauncher.Open(
                launchShortcut,
                terminalApplicationId,
                defaultProfileId,
                options.RunAsAdmin,
                options.RunAsStandard);
            AddTerminalSuccessDiagnostics(diagnostics, launch, launchShortcut, attempt);

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
    }

    private readonly record struct EntryPlan(
        WorkspaceEntry Entry,
        ResolvedLaunch Resolved,
        bool EffectiveElevation);

    private static ShortcutLaunchResult LaunchAll(
        TerminalShortcut shortcut,
        IReadOnlyList<WorkspaceEntry> enabledLaunches,
        string terminalApplicationId,
        string defaultProfileId,
        ShortcutLaunchOptions options,
        bool companionAttempted,
        bool companionSucceeded,
        string? companionError,
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
<<<<<<< Updated upstream
                var resolved = TerminalLauncher.Resolve(launchShortcut, terminalApplicationId, defaultProfileId);
                var effectiveElevation = !options.RunAsStandard && (options.RunAsAdmin || launch.RunAsAdmin);
                plans.Add(new EntryPlan(launch, resolved, effectiveElevation));
=======
                var attempt = TerminalLauncher.Open(
                    launchShortcut,
                    terminalApplicationId,
                    defaultProfileId,
                    options.RunAsAdmin,
                    options.RunAsStandard);
                AddTerminalSuccessDiagnostics(diagnostics, launch, launchShortcut, attempt);
                opened++;
>>>>>>> Stashed changes
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
<<<<<<< Updated upstream
        }

        var groups = GroupPlans(plans);
        var openedCommands = 0;

        foreach (var group in groups)
        {
            try
            {
                if (group.Count == 1)
                {
                    TerminalLauncher.OpenResolved(group[0].Resolved, group[0].EffectiveElevation);
                }
                else
                {
                    TerminalLauncher.OpenGroup(
                        group.Select(p => p.Resolved).ToList(),
                        group[0].EffectiveElevation);
                }

                openedCommands += group.Count;
            }
            catch (Win32Exception)
            {
                lastFailureLabel = group[^1].Entry.Label;
            }
            catch (InvalidOperationException)
            {
                lastFailureLabel = group[^1].Entry.Label;
=======
            catch (Win32Exception ex)
            {
                lastFailureLabel = launch.Label;
                diagnostics.AddError(
                    LaunchDiagnosticKind.TerminalLaunchFailed,
                    $"{launch.Label} terminal was canceled or blocked.",
                    ex.Message);
>>>>>>> Stashed changes
            }
        }

        if (openedCommands == 0)
        {
            return ShortcutLaunchResult.StayOpen(
                lastFailureLabel is null
<<<<<<< Updated upstream
                    ? "Workspace could not launch any commands."
                    : $"{lastFailureLabel} could not be launched.");
=======
                    ? "Workspace could not launch any terminals."
                    : $"{lastFailureLabel} could not be launched.",
                diagnostics: diagnostics);
>>>>>>> Stashed changes
        }

        var successPrefix = openedCommands == enabledLaunches.Count
            ? "Workspace launched"
            : $"Workspace partially launched: {openedCommands} of {enabledLaunches.Count} commands launched";

        if (opened < enabledLaunches.Count)
        {
            diagnostics.AddWarning(
                LaunchDiagnosticKind.PartialLaunch,
                "Workspace partially launched.",
                $"{opened} of {enabledLaunches.Count} terminals opened.");
        }

        return BuildPostLaunchResult(
            shortcut,
            options,
            companionAttempted,
            companionSucceeded,
            companionError,
            successPrefix,
<<<<<<< Updated upstream
            partialLaunch: openedCommands < enabledLaunches.Count);
    }

    private static List<List<EntryPlan>> GroupPlans(List<EntryPlan> plans)
    {
        var groups = new List<List<EntryPlan>>();
        var groupIndexByKey = new Dictionary<(string Host, bool Elevated), int>();

        foreach (var plan in plans)
        {
            var isTabCapable = plan.Resolved.Target.Kind is
                LaunchTargetKind.WindowsTerminal or LaunchTargetKind.IntelligentTerminal;

            if (!isTabCapable)
            {
                groups.Add([plan]);
                continue;
            }

            var key = ((plan.Resolved.Target.HostExecutable ?? string.Empty).ToUpperInvariant(), plan.EffectiveElevation);
            if (groupIndexByKey.TryGetValue(key, out var index))
            {
                groups[index].Add(plan);
            }
            else
            {
                groupIndexByKey[key] = groups.Count;
                groups.Add([plan]);
            }
        }

        return groups;
=======
            preflightWarnings,
            diagnostics,
            partialLaunch: opened < enabledLaunches.Count);
>>>>>>> Stashed changes
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
