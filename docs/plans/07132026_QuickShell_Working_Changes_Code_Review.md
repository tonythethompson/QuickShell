High Priority
1. Race condition in QuickShellPage async icon upgrade
Files: QuickShellPage.cs lines 378–480, 483–496
Issue: _pendingIconApplies is written by a background Task.Run and then consumed by ApplyPendingProfileIcons via Interlocked.Exchange. The generation check is not strong enough:
_iconUpgradeGeneration is not volatile/Interlocked, so a stale Task.Run may not see the updated value.
_pendingIconApplies is assigned before the callback is enqueued and without locking. If RefreshItems is called again while a prior Task.Run is still running, the old task can overwrite _pendingIconApplies after the newer task has enqueued its callback. The stale callback returns early, but GetItems then calls ApplyPendingProfileIcons directly and will apply the stale list.
Recommendation: Use Interlocked/lock around both _iconUpgradeGeneration and _pendingIconApplies, and only set _pendingIconApplies after confirming the generation is still current.
2. Duplicate git status probing in WorkspaceStatusPage
Files: WorkspaceStatusPage.cs lines 59–64, 93–135; QuickShell.Core/Services/WorkspaceStatusService.cs lines 112–114
Issue: GetContent calls EnsureGitCommands, which calls WorkspaceGitOperations.TryGetStatus. Then WorkspaceStatusForm constructor calls Refresh(forceRefresh: true), which calls WorkspaceStatusService.Capture and runs WorkspaceGitOperations.TryGetStatus a second time for the same directory.
Recommendation: Share one WorkspaceStatusService.Capture call. The page can capture the snapshot first, build commands from it, and pass the snapshot to WorkspaceStatusForm.
3. ExtensionCallbackQueue.Drain() plus synchronous RefreshItems can re-enter GetItems
Files: SettingsFormHelpers.cs lines 19–20; QuickShellPage.cs lines 90–100; DiscoverGitReposPage.cs lines 31–47
Issue: SettingsFormHelpers.SchedulePostNavigationRefresh is not actually deferred; it calls InvokeSafe(refresh) immediately. The naming and PostNavigationRefreshDelayMs = 1 imply it should delay, but delayMs is never used. Because GetItems now calls ExtensionCallbackQueue.Drain() then SchedulePostNavigationReload/ScheduleRefreshItems, RefreshItems runs inline inside GetItems, which can call RaiseItemsChanged before GetItems returns.
Recommendation: Either rename the helper to make it clear it is synchronous or implement the PostNavigationRefreshDelayMs delay so GetItems returns before heavy refresh work runs.
Medium Priority
4. DiscoverGitReposPage may spin RefreshItems while waiting for the git scan
Files: DiscoverGitReposPage.cs lines 31–47, 109–139
Issue: When RefreshItems waits for an in-flight scan, it sets _awaitingGitRefresh = true; GetItems then suppresses the initial refresh branch and waits for !GitRepoIndex.IsRefreshInFlight before scheduling again.
Recommendation: Remove this item.
5. TerminalListIconCache metadata I/O is not fully guarded
Files: TerminalListIconCache.cs lines 85–132
Issue: The timestamp calls are already inside a try/catch that returns sourcePath on failure, so metadata I/O does not escape CreateOrGetResizedPath as described.
Recommendation: Remove this item.
6. WorkspaceStatusPage Commands are built once and never invalidated
Files: WorkspaceStatusPage.cs lines 43–52
Issue: EnsureGitCommands sets _commandsReady after the first build. If a user switches branches using the context commands and returns to the same WorkspaceStatusPage instance, the branch-picker command still has the old status.
Recommendation: Either refresh Commands when GetContent is re-entered, or ensure a fresh WorkspaceStatusPage is created each time the command is invoked.
Low Priority / Pattern Issues
7. SettingsCardJson.Escape is a fragile manual JSON escaper
Files: SettingsCardJson.cs lines 214–215
Issue: Escape handles the standard control escapes and encodes remaining characters below 0x20 as \uXXXX, so the stated JSON escaping gap is not present.
Note: Remove this item.
8. ShortcutHealth.GetListGlyph uses GetForList but OpenTerminalShortcutCommand also does
Files: ShortcutHealth.cs lines 39–44; QuickShell/Commands/OpenTerminalShortcutCommand.cs lines 137–142
Issue: OpenTerminalShortcutCommand.ResolveLaunchIcon now uses TerminalLaunchGlyphs.GetForList for the runAsStandard case. This is correct for the ListItem first paint, but OpenTerminalShortcutCommand is also used as the context menu command "Run normally". The context menu command’s icon will therefore be the fast fallback glyph, not the actual profile icon. This may be an intentional performance trade-off, but it is a UI regression for the context menu.
Recommendation: If context menu icons should be accurate, only the ListItem path should use GetForList; command icons should use GetForShortcut or the upgraded cache.
9. ShortcutFormPage MergeCommandsFromInputs references LaunchType_{i} that is not in the template
Files: ShortcutFormPage.cs lines 1118–1119; ShortcutFormTemplateJson.cs lines 255–266
Issue: MergeCommandsFromInputs reads data[$"LaunchType_{i}"], but the Adaptive Card template does not contain an input with that id. The value comes from BuildDataJson, so it persists across submits, but the user cannot change task type in the UI. This is pre-existing but worth noting.
Recommendation: If task type is intentionally read-only, add a comment; otherwise expose a LaunchType input in the form.
10. PackageServicingShutdownWatcher Join timeout without fallback
Files: PackageServicingShutdownWatcher.cs lines 77–83
Issue: If the message pump thread does not exit within 2 seconds, Dispose logs and continues. The thread could remain alive and hold the HWND class registration.
Recommendation: If Join times out, abort the thread or TerminateThread? But native message pumps are hard to terminate; at minimum document the behavior.
Pre-existing Issues Still Present
WorkspaceStatusPage performs two git status probes (see #2).
SettingsFormHelpers.SchedulePostNavigationRefresh is synchronous (see #3).
ShortcutFormPage LaunchType input is missing (see #9).
WorkspaceStatusPage commands are cached for the lifetime of the page instance (see #6).