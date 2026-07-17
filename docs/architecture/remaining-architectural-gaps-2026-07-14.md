# Remaining Architectural Gaps — Code-Grounded Analysis

**As of:** 2026-07-14  
**Method:** Full code inspection of `QuickShell.Core`, `QuickShell`, and `QuickShell.Core.Tests` across all four partially-addressed proposal areas.  
**Precedes:** [proposal-status.md](./proposal-status.md) (high-level status)  
**Supersedes:** [QuickShell-Architectural-Audit-2026-07-08.md](./QuickShell-Architectural-Audit-2026-07-08.md) for implementation-state claims (the audit is still the reference for architectural recommendations; this doc tracks what has and hasn't been built).

---

## Status Summary

| Proposal | Audit Title | Landed | The Gap |
|----------|-------------|--------|---------|
| #0001 | DI + Composition Root | **Partial** | 6 static-to-wrapper pass-throughs; 27 host files on `QuickShellServices.Current` service locator |
| #0003 | Typed Command Routing | **Mostly Landed** | ID builders (`ShortcutCommandIds`) and constants scattered alongside new router; 4-site contract split |
| #0004 | Service Consolidation / Registry | **Progress** | Classifier half done (13 `IProjectClassifier` impls); suggestion/companion half not started |
| #0005 | Dispose / Cancellation / Tests | **Partial** | No root `CancellationTokenSource`; `QuickShellExtension.Dispose()` only signals COM event, never calls provider dispose; 4+ fire-and-forget `Task.Run` sites |

---

## #0001 — DI: The Service-Locator-Led Hollowing

### Landed

`AddQuickShellCore` in `Composition/` registers ~30 services via `Microsoft.Extensions.DependencyInjection`:

- `IAtomicFileWriter → AtomicFileWriter`
- `IShortcutRepository → ShortcutRepository`
- `IDraftStore → ShortcutDraftStore`
- `ICommandIdParser → CommandIdParser`
- `ITerminalLauncher → TerminalLauncherService`
- `ITerminalProfileResolver → TerminalProfileResolverService`
- `IWorkspaceMapper → WorkspaceMapperService`
- `IGitRepoIndex → GitRepoIndexService`
- `IWorkspaceHealthChecker → WorkspaceHealthCheckerService`
- `IProjectLayoutAnalyzer → ProjectLayoutAnalyzer`
- `IProjectClassifier → (13 implementations)`
- `ICompanionAppDetector → CompanionAppDetector`
- `IDevServerDetector → DevServerDetector`
- `IProjectAnalysisService → ProjectAnalysisService`

`QuickShellServices` (DI-seeded facade) replaces the old `QuickShellRuntimeServices` static hub. `ServiceProvider` is built in the provider constructor and disposed on shutdown. Composition tests exist (`QuickShellCompositionRootTests`).

### The Pattern Gap

The `QuickShellCommandsProvider` constructor builds DI, extracts four singletons, then parks them in `QuickShellServices.Bind()` — a static service locator. **Everything after that point resolves through `QuickShellServices.Current`**.

Twenty-seven host files use `QuickShellServices.Current`:

```
QuickShellCommandsProvider.cs
QuickShellFallback.cs
Commands/DeleteShortcutCommand.cs
Commands/DuplicateShortcutCommand.cs
Commands/ExportShortcutsCommand.cs
Commands/ImportShortcutsCommand.cs
Commands/MoveFavoriteShortcutCommand.cs
Commands/OpenShortcutLaunchCommand.cs
Commands/RedoShortcutCommand.cs
Commands/UndoShortcutCommand.cs
Commands/OpenTerminalShortcutCommand.cs
Commands/ToggleFavoriteShortcutCommand.cs
Commands/ResetTransferCommands.cs
Commands/WorkspaceFormUndoCommands.cs
Commands/WorkspaceUtilityCommands.cs
Commands/WorktreeBranchCommands.cs
Pages/QuickShellPage.cs
Pages/QuickShellExtensionSettingsPage.cs
Pages/QuickShellFallbackPage.cs
Pages/ShortcutFormPage.cs
Pages/ShortcutTransferSettingsForm.cs
Pages/WorktreeBranchPickerPage.cs
Pages/DiscoverGitReposPage.cs
Pages/ImportConflictPage.cs
Pages/PendingShortcutEditPage.cs
Services/ShortcutFilePickerService.cs
Services/CommandRouting/CommandItemHandlers.cs
```

### The 6 Pass-Through Wrappers

| Interface | Service | Delegates to static |
|---|---|---|
| `IGitRepoIndex` | `GitRepoIndexService` | `GitRepoIndex` |
| `ITerminalLauncher` | `TerminalLauncherService` | `TerminalLauncher` |
| `ITerminalProfileResolver` | `TerminalProfileResolverService` | `TerminalProfileResolver` |
| `IWorkspaceMapper` | `WorkspaceMapperService` | `WorkspaceMapper` |
| `IWorkspaceGitOperations` | `WorkspaceGitOperationsService` | `WorkspaceGitOperations` |
| `IWorkspaceHealthChecker` | `WorkspaceHealthCheckerService` | `WorkspaceHealthCheck` |

Each delegates every method to the static class. Tests against the wrapper test nothing.

### Finish Criteria

| Step | Files | Risk |
|---|---|---|
| Inline 6 wrappers — merge static logic into instance, delete static | ~6 files + ~20 call sites | Medium |
| Remove `QuickShellServices.Current` from 27 files; inject via constructor | 27 files | Medium — `ReloadPages` callback pattern |
| Delete `QuickShellServices.Bind/Unbind/Current` | ~5 files | Low |
| Architecture test: no assembly uses `QuickShellServices.Current` | 1 test file | Low |

---

## #0003 — Command Routing: Parse Path Clean, Build Path Scattered

### Landed

`CommandItem(id) → _commandRouter.TryHandle(id) → CommandItemHandlers` backed by `CommandIdParser.TryParse(rawId) → CommandDescriptor`. Handles 11 command kinds. `AddQuickShellCommandRouting` registers everything via DI.

### The Split Contract

The ID format is split across **four** files:

| Site | Purpose |
|---|---|
| `QuickShellDeepLinkIds` | 12 prefix constants (schema definition) |
| `ShortcutCommandIds` | 8 ID-building methods (builder) |
| `CommandIdParser` | 6 `TryParse*` + 4 helpers (parser) |
| `CommandIdEncoding` | Encode/decode serialization |

The `.admin`/`.standard` suffix stripping is in `CommandIdParser`; the builder side does not append these suffixes — they're added ad-hoc by `ShortcutFieldButtonFactory` call sites. No `CommandDescriptor.ForOpenWorkspace(id)` factory exists.

### Finish Criteria

| Step | Files | Risk |
|---|---|---|
| Add `CommandDescriptor` static factories | `CommandDescriptor.cs` | Low |
| Rebuild `ShortcutCommandIds` call sites | ~15 call sites | Low |
| Delete `ShortcutCommandIds`, `QuickShellDeepLinkIds`, `CommandIdEncoding` | 3 files | Low |
| Add frozen ID contract doc | 1 doc file | Low |

---

## #0004 — Service Consolidation: Half the Prize

### Landed (Classifier Half)

`Abstractions/Classification/` with `IProjectClassifier` (`IEnumerable<T>` ready), `IProjectAnalysisService`, `ICompanionAppDetector`, `IDevServerDetector`, `IProjectLayoutAnalyzer`. 13 concrete classifiers in `Classification/Classifiers/`. `ProjectAnalysisService` orchestrates `IEnumerable<IProjectClassifier>`.

### Not Started (Suggestion/Companion Half)

12 files remain static with no interface:

- `CommandSuggestionService.cs` — static
- `TaskTypeCommandSuggestion.cs` — static, 500+ loc
- `TaskTypeCandidateBuilder.cs` — static
- `TaskTypeCatalog.cs` — static catalog
- `SuggestionPillPresentation.cs` — static
- `SuggestCommandLineArgs.cs` — static
- `CompanionAppCatalog.cs` — static catalog
- `CompanionAppDetection.cs` — static (DUPLICATE — `Classification/Detectors/` has the DI version)
- `CompanionAppLauncher.cs` — static
- `WorkspaceCompanionSignals.cs` — static
- `WorkspaceSetupSuggestion.cs` — static
- `ProjectClassificationCache.cs` — static `ConcurrentDictionary`

`ProjectClassifier` static bridges to `IProjectAnalysisService` but `TaskTypeCommandSuggestion` et al call it directly instead of using DI.

### Finish Criteria

| Step | Files | Risk |
|---|---|---|
| Define `ITaskSuggestionProvider` interface | New file in `Abstractions/` | Low |
| Extract pill providers from `TaskTypeCandidateBuilder` | ~5 new files + rewrite | Medium |
| Register via `AddSingleton<ITaskSuggestionProvider, ...>` | 1 file | Low |
| Delete static `CompanionAppDetection`; redirect call sites | 2–3 sites + delete | Low |
| Delete `ProjectClassifier` static; redirect callers | 1 file + ~3 sites | Low |

---

## #0005 — Dispose/Cancellation: The Gap

### Extension Dispose Is a No-Op for the Provider

`QuickShellExtension.Dispose()` (file: `QuickShell.cs`) only signals `_extensionDisposedEvent.Set()`. It never calls `_provider.Dispose()`. The provider's real dispose chain — unsubscribing settings, disposing pages, unbinding services, disposing `ServiceProvider` — is dead code.

### Background Work That Outlives Shutdown

| Site | Pattern | Problem |
|---|---|---|
| `KickoffGitRepoIndexPrewarm()` (ctor) | `_ = Task.Run(() => GitRepoIndex.Prewarm(...))` | Fire-and-forget in constructor |
| `GitRepoIndex.StartRefreshLocked()` | `Task.Run(DiscoverForRefresh)` | No token; `ContinueWith(..., CancellationToken.None)` |
| `GitRepoDiscovery.Discover()` | `Task.Run(Worker)`, `Task.WaitAll(workers)` | No token in worker loop |
| `QuickShellServices.BeginShortcutPreload()` | `_ = PreloadShortcutsAsync()` | Passes `default(CancellationToken)` |

### Static Mutable State

**`GitRepoIndex`** — 6 static fields (`_cache`, `_cacheRootKey`, `_refreshedUtc`, `_hasCompletedRefreshForRoot`, `_refreshInFlight`, `RefreshCompletedHandlers`). Survive across provider instances.

**`ProjectClassificationCache`** — static `ConcurrentDictionary` + `Queue`. Never cleared.

### Finish Criteria

| Step | Files | Risk |
|---|---|---|
| Create `QuickShellLifetime : IDisposable` (root CTS) | New file | Low |
| Wire into `QuickShellExtension` → provider | `QuickShell.cs`, `QuickShellCommandsProvider.cs` | Low |
| Thread `CancellationToken` through `IGitRepoIndex.Prewarm(token)` → `Task.Run(action, token)` | 3 files | Low |
| Add token to `GitRepoDiscovery.Discover(roots, token)` → `ShouldStop()` | 1 file | Medium |
| Pass token to `PreloadShortcutsAsync(token)` | `QuickShellServices.cs` | Low |
| Wire extension dispose: cancel CTS → dispose provider → set event | `QuickShell.cs` | Low |
| Clear static caches on dispose | 3 files | Low |
| Integration test: dispose mid-background-work, assert clean | New test file | Low |

---

## Ordering and Dependencies

All four gaps are **independent** — no ordering required. Can be worked in parallel or any order.

---

## Risk Table

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| `QuickShellServices.Current` removal breaks late-init pages | Medium | Medium | Lazy-resolve overload or transitional `IServiceProvider` on facade |
| `ITaskSuggestionProvider` extraction loses ordering | Low | Medium | `IOrderedEnumerable<T>` or `[Order]` attribute |
| `CancellationToken` on `Task.Run` doesn't stop filesystem recursion | Medium | Low | Token cancels scheduled task; scan is bounded by depth |
| `GitRepoIndex` static race across provider instances | Low | High | `ReferenceEquals` guard already in `CompleteRefresh`