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
    bool SeparateWindowsForMultiLaunch = false)
{
    public ShortcutLaunchOptions()
        : this(false, false, true, true, true, false)
    {
    }
}

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
    private readonly IShortcutRepository? _repository;
    private readonly ITerminalCatalog _catalog;
    private readonly IQuickShellEventSource _events;
    private readonly WorkspaceLaunchPlanCache _planCache = new();

    /// <summary>
    /// Creates an executor for resolving and launching terminal shortcuts.
    /// </summary>
    /// <param name="repository">The optional shortcut repository used to resolve the latest workspace state.</param>
    /// <param name="catalog">The optional terminal catalog used to resolve launch targets.</param>
    /// <param name="events">The optional event source used to record launch-plan cache activity.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required service dependency is <see langword="null"/>.</exception>
    public ShortcutLaunchExecutor(
        ITerminalLauncher terminalLauncher,
        IWorkspaceHealthChecker healthChecker,
        ICompanionAppLauncher companionAppLauncher,
        WorkspaceGitLaunchGate gitLaunchGate,
        IShortcutRepository? repository = null,
        ITerminalCatalog? catalog = null,
        IQuickShellEventSource? events = null)
    {
        _terminalLauncher = terminalLauncher ?? throw new ArgumentNullException(nameof(terminalLauncher));
        _healthChecker = healthChecker ?? throw new ArgumentNullException(nameof(healthChecker));
        _companionAppLauncher = companionAppLauncher ?? throw new ArgumentNullException(nameof(companionAppLauncher));
        _gitLaunchGate = gitLaunchGate ?? throw new ArgumentNullException(nameof(gitLaunchGate));
        _repository = repository;
        _catalog = catalog ?? new TerminalCatalog(new WtProfilesService());
        _events = events ?? QuickShellEventSource.Log;
    }

    /// <summary>
    /// Launches all enabled entries in a workspace after completing health, directory, and Git checks.
    /// </summary>
    /// <param name="shortcut">The workspace shortcut to launch.</param>
    /// <param name="terminalApplicationId">The terminal application used to launch the workspace.</param>
    /// <param name="defaultProfileId">The default terminal profile used when an entry does not specify one.</param>
    /// <param name="options">Optional launch behavior settings.</param>
    /// <returns>The launch result, including whether the UI should close and any diagnostics.</returns>
    public ShortcutLaunchResult Launch(
        TerminalShortcut shortcut,
        string terminalApplicationId,
        string defaultProfileId,
        ShortcutLaunchOptions? options = null)
    {
        var opts = options ?? new ShortcutLaunchOptions();
        var (freshShortcut, repositoryVersion) = ResolveShortcut(shortcut);
        if (freshShortcut is null)
        {
            return WorkspaceNotFound(shortcut.Id);
        }

        var effectiveApp = TerminalHostIds.ResolveEffectiveApplication(terminalApplicationId);
        var key = BuildCacheKey(freshShortcut, repositoryVersion, effectiveApp, defaultProfileId, null, opts);

        var diagnostics = new LaunchDiagnosticsReport(freshShortcut.Name, DateTimeOffset.UtcNow);

        var plan = _planCache.GetOrBuild(
            key,
            () => BuildPlan(freshShortcut, repositoryVersion, effectiveApp, defaultProfileId, opts, null),
            onHit: () =>
            {
                diagnostics.AddInfo(LaunchDiagnosticKind.PlanCacheHit, "Launch plan cache hit.", FormatCacheKeyDimensions(key));
                _events.WritePlanCache(LaunchDiagnosticKind.PlanCacheHit.ToString());
            },
            onMiss: () =>
            {
                diagnostics.AddInfo(LaunchDiagnosticKind.PlanCacheMiss, "Launch plan cache miss.", FormatCacheKeyDimensions(key));
                _events.WritePlanCache(LaunchDiagnosticKind.PlanCacheMiss.ToString());
            },
            onBuild: () =>
            {
                diagnostics.AddInfo(LaunchDiagnosticKind.PlanCacheBuild, "Launch plan cache build.", FormatCacheKeyDimensions(key));
                _events.WritePlanCache(LaunchDiagnosticKind.PlanCacheBuild.ToString());
            },
            onEvicted: () =>
            {
                diagnostics.AddInfo(LaunchDiagnosticKind.PlanCacheEvicted, "Launch plan cache evicted.");
                _events.WritePlanCache(LaunchDiagnosticKind.PlanCacheEvicted.ToString());
            });

        WorkspaceHealthResult health;
        using (StartupPerformanceTrace.Measure("launch health check"))
        {
            health = _healthChecker.Check(freshShortcut, effectiveApp, defaultProfileId, includeGit: false);
            AddHealthDiagnostics(diagnostics, health);
        }

        if (health.HasBlockingErrors)
        {
            return ShortcutLaunchResult.StayOpen(
                WorkspaceHealthCheck.FormatBlockingSummary(health),
                diagnostics: diagnostics);
        }

        if (!TryGetLaunchDirectory(freshShortcut.Directory, out var launchDirectory, out var directoryError))
        {
            diagnostics.AddError(LaunchDiagnosticKind.HealthError, "Workspace folder could not be used.", directoryError);
            return ShortcutLaunchResult.StayOpen(directoryError, diagnostics: diagnostics);
        }

        WorkspaceGitLaunchGateResult gitGate;
        using (StartupPerformanceTrace.Measure("launch git gate"))
        {
            gitGate = _gitLaunchGate.EvaluateBeforeLaunch(
                launchDirectory,
                opts.BlockDirtyBranchSwitch);
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

        return ExecutePlan(
            freshShortcut,
            plan,
            opts,
            health.WarningFindings,
            diagnostics,
            singleSuccessPrefix: "Workspace opened",
            multiSuccessPrefix: "Workspace launched");
    }

    /// <summary>
    /// Launches a specific enabled entry from a workspace.
    /// </summary>
    /// <param name="launch">The workspace entry to launch.</param>
    /// <returns>
    /// The launch result, including diagnostics and any message when the UI should remain open.
    /// </returns>
    public ShortcutLaunchResult LaunchEntry(
        TerminalShortcut shortcut,
        WorkspaceEntry launch,
        string terminalApplicationId,
        string defaultProfileId,
        ShortcutLaunchOptions? options = null)
    {
        var opts = options ?? new ShortcutLaunchOptions();
        var (freshShortcut, repositoryVersion) = ResolveShortcut(shortcut);
        if (freshShortcut is null)
        {
            return WorkspaceNotFound(shortcut.Id);
        }

        var freshLaunch = freshShortcut.Launches.FirstOrDefault(entry =>
            entry.Id.Equals(launch.Id, StringComparison.OrdinalIgnoreCase));
        if (freshLaunch is null || !freshLaunch.IsEnabled)
        {
            var notFoundDiagnostics = new LaunchDiagnosticsReport($"{shortcut.Name} - {launch.Label}", DateTimeOffset.UtcNow);
            notFoundDiagnostics.AddError(LaunchDiagnosticKind.HealthError, "Launch entry not found or disabled.");
            return ShortcutLaunchResult.StayOpen(
                "That launch entry was not found or is disabled.",
                diagnostics: notFoundDiagnostics);
        }

        var effectiveApp = TerminalHostIds.ResolveEffectiveApplication(terminalApplicationId);
        var key = BuildCacheKey(freshShortcut, repositoryVersion, effectiveApp, defaultProfileId, freshLaunch.Id, opts);

        var diagnostics = new LaunchDiagnosticsReport($"{freshShortcut.Name} - {freshLaunch.Label}", DateTimeOffset.UtcNow);

        var plan = _planCache.GetOrBuild(
            key,
            () => BuildPlan(freshShortcut, repositoryVersion, effectiveApp, defaultProfileId, opts, freshLaunch.Id),
            onHit: () =>
            {
                diagnostics.AddInfo(LaunchDiagnosticKind.PlanCacheHit, "Launch plan cache hit.", FormatCacheKeyDimensions(key));
                _events.WritePlanCache(LaunchDiagnosticKind.PlanCacheHit.ToString());
            },
            onMiss: () =>
            {
                diagnostics.AddInfo(LaunchDiagnosticKind.PlanCacheMiss, "Launch plan cache miss.", FormatCacheKeyDimensions(key));
                _events.WritePlanCache(LaunchDiagnosticKind.PlanCacheMiss.ToString());
            },
            onBuild: () =>
            {
                diagnostics.AddInfo(LaunchDiagnosticKind.PlanCacheBuild, "Launch plan cache build.", FormatCacheKeyDimensions(key));
                _events.WritePlanCache(LaunchDiagnosticKind.PlanCacheBuild.ToString());
            },
            onEvicted: () =>
            {
                diagnostics.AddInfo(LaunchDiagnosticKind.PlanCacheEvicted, "Launch plan cache evicted.");
                _events.WritePlanCache(LaunchDiagnosticKind.PlanCacheEvicted.ToString());
            });

        WorkspaceHealthResult health;
        using (StartupPerformanceTrace.Measure("launch entry health check"))
        {
            health = _healthChecker.CheckEntry(
                freshShortcut,
                freshLaunch,
                effectiveApp,
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

        if (!TryGetLaunchDirectory(freshShortcut.Directory, out var launchDirectory, out var directoryError))
        {
            diagnostics.AddError(LaunchDiagnosticKind.HealthError, "Workspace folder could not be used.", directoryError);
            return ShortcutLaunchResult.StayOpen(directoryError, diagnostics: diagnostics);
        }

        WorkspaceGitLaunchGateResult gitGate;
        using (StartupPerformanceTrace.Measure("launch entry git gate"))
        {
            gitGate = _gitLaunchGate.EvaluateBeforeLaunch(
                launchDirectory,
                opts.BlockDirtyBranchSwitch);
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

        var entryOptions = opts with
        {
            IncludeCompanionApp = false,
            IncludeDevServerLink = false,
        };

        return ExecutePlan(
            freshShortcut,
            plan,
            entryOptions,
            health.WarningFindings,
            diagnostics,
            singleSuccessPrefix: "Workspace entry opened",
            multiSuccessPrefix: "Workspace entry opened");
    }

    private (TerminalShortcut? Shortcut, long Version) ResolveShortcut(TerminalShortcut shortcut)
    {
        if (_repository is null)
        {
            return (shortcut, 0);
        }

        var snapshot = _repository.GetSnapshot();
        var fresh = _repository.GetById(shortcut.Id);
        if (fresh is null)
        {
            return (null, snapshot.StructuralVersion);
        }

        ShortcutLaunchNormalization.EnsureLaunchesFromLegacy(fresh);
        // Key the launch plan cache on structural changes only: Version also bumps on
        // usage-only updates (MarkUsed), which would otherwise bust the cache on every
        // repeat launch instead of just when launch-affecting fields actually change.
        return (fresh, snapshot.StructuralVersion);
    }

    private static ShortcutLaunchResult WorkspaceNotFound(string workspaceId)
    {
        var diagnostics = new LaunchDiagnosticsReport("Workspace", DateTimeOffset.UtcNow);
        diagnostics.AddError(LaunchDiagnosticKind.HealthError, "Workspace not found.", workspaceId);
        return ShortcutLaunchResult.StayOpen("Workspace not found.", diagnostics: diagnostics);
    }

    private LaunchPlanCacheKey BuildCacheKey(
        TerminalShortcut shortcut,
        long repositoryVersion,
        string effectiveTerminalApplicationId,
        string defaultProfileId,
        string? launchEntryId,
        ShortcutLaunchOptions options)
    {
        var settingsFingerprint = BuildSettingsFingerprint(effectiveTerminalApplicationId, defaultProfileId, options);
        var terminalFingerprint = _catalog.GetFingerprint();
        return new LaunchPlanCacheKey(
            shortcut.Id,
            repositoryVersion,
            settingsFingerprint,
            terminalFingerprint,
            launchEntryId,
            options.RunAsAdmin,
            options.RunAsStandard);
    }

    private static string BuildSettingsFingerprint(string effectiveTerminalApplicationId, string defaultProfileId, ShortcutLaunchOptions options) =>
        $"{effectiveTerminalApplicationId}|{defaultProfileId}|{options.SeparateWindowsForMultiLaunch}";

    private static string FormatCacheKeyDimensions(LaunchPlanCacheKey key)
    {
        return $"workspace={key.WorkspaceId}, version={key.RepositoryVersion}, launch={(key.LaunchEntryId ?? "(all)")}, runAsAdmin={key.RunAsAdmin}, runAsStandard={key.RunAsStandard}, settings={key.SettingsFingerprint}, catalog={key.TerminalCatalogFingerprint}";
    }

    private ResolvedWorkspaceLaunchPlan BuildPlan(
        TerminalShortcut shortcut,
        long repositoryVersion,
        string effectiveTerminalApplicationId,
        string defaultProfileId,
        ShortcutLaunchOptions options,
        string? launchEntryId)
    {
        // Work on a private copy so normalization and plan construction do not leak mutable state.
        shortcut = ShortcutRepository.Clone(shortcut);
        ShortcutLaunchNormalization.NormalizeShortcut(shortcut);

        var enabled = ShortcutLaunchNormalization.GetEnabledLaunches(shortcut).ToList();

        if (launchEntryId is not null)
        {
            var selected = enabled.FirstOrDefault(entry =>
                entry.Id.Equals(launchEntryId, StringComparison.OrdinalIgnoreCase));
            enabled = selected is null ? [] : [selected];
        }

        var planEntries = new List<ResolvedLaunchPlanEntry>();
        foreach (var launch in enabled)
        {
            var launchShortcut = ShortcutLaunchNormalization.ToLaunchShortcut(launch, shortcut);
            if (!ShortcutValidation.TryNormalizeDirectory(
                    launchShortcut.Directory,
                    out var normalizedDirectory,
                    out var directoryError))
            {
                throw new InvalidOperationException(directoryError);
            }

            launchShortcut.Directory = normalizedDirectory;
            var target = _catalog.ResolveForShortcut(launchShortcut, effectiveTerminalApplicationId, defaultProfileId);
            var resolved = new ResolvedLaunch(launchShortcut, target);
            var effectiveElevation = !options.RunAsStandard && (options.RunAsAdmin || launch.RunAsAdmin);
            planEntries.Add(new ResolvedLaunchPlanEntry(launch, resolved, effectiveElevation, launch.Order));
        }

        var groups = BuildResolvedGroups(planEntries, effectiveTerminalApplicationId, options.SeparateWindowsForMultiLaunch);
        var companions = BuildCompanionDescriptors(shortcut);

        return new ResolvedWorkspaceLaunchPlan(shortcut.Id, repositoryVersion, planEntries, groups, companions);
    }

    private static List<ResolvedLaunchGroup> BuildResolvedGroups(
        IReadOnlyList<ResolvedLaunchPlanEntry> entries,
        string terminalApplicationId,
        bool separateWindows)
    {
        if (separateWindows)
        {
            return entries
                .Select(entry => new ResolvedLaunchGroup(
                    new[] { entry },
                    entry.Resolved.Target.HostExecutable,
                    entry.EffectiveElevation))
                .ToList();
        }

        var groups = new List<ResolvedLaunchGroup>();
        var groupIndexByKey = new Dictionary<(string Host, bool Elevated), int>();
        var workspaceTabHostExecutable = GetWorkspaceTabHostExecutable(terminalApplicationId);

        foreach (var entry in entries)
        {
            if (!TryGetTabHostExecutable(entry.Resolved.Target, workspaceTabHostExecutable, out var tabHostExecutable))
            {
                groups.Add(new ResolvedLaunchGroup(
                    new[] { entry },
                    entry.Resolved.Target.HostExecutable,
                    entry.EffectiveElevation));
                continue;
            }

            var key = (tabHostExecutable.ToUpperInvariant(), entry.EffectiveElevation);
            if (groupIndexByKey.TryGetValue(key, out var index))
            {
                var existing = groups[index];
                groups[index] = existing with
                {
                    Entries = existing.Entries.Concat(new[] { entry }).ToList(),
                };
            }
            else
            {
                groupIndexByKey[key] = groups.Count;
                groups.Add(new ResolvedLaunchGroup(
                    new[] { entry },
                    tabHostExecutable,
                    entry.EffectiveElevation));
            }
        }

        return groups;
    }

    private static List<ResolvedCompanionDescriptor> BuildCompanionDescriptors(TerminalShortcut shortcut)
    {
        CompanionAppNormalization.EnsureCompanionsFromLegacy(shortcut);
        return CompanionAppNormalization.GetOpenOnLaunch(shortcut)
            .Select(entry => new ResolvedCompanionDescriptor(
                entry.Path ?? string.Empty,
                CompanionAppLauncher.ExpandArguments(entry.Arguments, shortcut.Directory),
                shortcut.Directory,
                true))
            .ToList();
    }

    private ShortcutLaunchResult ExecutePlan(
        TerminalShortcut shortcut,
        ResolvedWorkspaceLaunchPlan plan,
        ShortcutLaunchOptions options,
        IReadOnlyList<WorkspaceHealthFinding> preflightWarnings,
        LaunchDiagnosticsReport diagnostics,
        string singleSuccessPrefix,
        string multiSuccessPrefix)
    {
        if (plan.Entries.Count == 0)
        {
            diagnostics.AddError(LaunchDiagnosticKind.HealthError, "Workspace has no enabled launch entries.");
            return ShortcutLaunchResult.StayOpen(
                "Workspace has no enabled launch entries.",
                diagnostics: diagnostics);
        }

        var openedCommands = 0;
        var totalEntries = plan.Entries.Count;
        string? lastFailureLabel = null;

        using (StartupPerformanceTrace.Measure("launch terminal open"))
        {
            foreach (var group in plan.Groups)
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
                            entries.Select(entry => entry.Resolved).ToList(),
                            entries[0].EffectiveElevation,
                            group.HostExecutable);
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

        var companionAttempted = options.IncludeCompanionApp
            && _companionAppLauncher.ShouldLaunchOnWorkspaceOpen(shortcut);

        var (companionSucceeded, companionError) = TryLaunchCompanion(
            shortcut,
            companionAttempted,
            diagnostics);

        var partialLaunch = openedCommands < totalEntries;
        var successPrefix = partialLaunch
            ? $"Workspace partially launched: {openedCommands} of {totalEntries} commands launched"
            : totalEntries == 1 ? singleSuccessPrefix : multiSuccessPrefix;

        if (partialLaunch)
        {
            diagnostics.AddWarning(
                LaunchDiagnosticKind.PartialLaunch,
                "Workspace partially launched.",
                $"{openedCommands} of {totalEntries} commands launched.");
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
            partialLaunch);
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
            var result = _companionAppLauncher.Launch(shortcut, onDemand: false);
            foreach (var executable in result.StartedExecutables)
            {
                diagnostics.RecordProcessStart(executable);
            }

            return (result.Success, result.Error);
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
