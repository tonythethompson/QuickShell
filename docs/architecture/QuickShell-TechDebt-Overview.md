# QuickShell — Tech Debt & Architectural Overview

*Windows-only .NET 10 workspace launcher: PowerToys Command Palette extension + PowerToys Run plugin + Raycast extension.*

## 1. Executive Summary

**Overall health: B+ / solid but complex.**

QuickShell is a well-architected, feature-rich launcher built around a clean separation between a reusable .NET `QuickShell.Core` library and two Windows UI hosts (CmdPal, PowerToys Run), plus a parallel TypeScript Raycast extension. It persists user-defined “workspaces” (project folders + terminal launches, companion apps, git branch targets, dev-server URLs), validates them before launch, opens them in the user’s preferred terminal (Windows Terminal / Intelligent Terminal / WSL / classic shells), and can run post-launch companion apps or browser URLs.

The **foundations are strong**: Core has no CmdPal SDK dependency, DI is partially wired, persistence is atomic with a versioned envelope, typed command routing exists, and the launch/health/git pipeline is sophisticated. The main risks are **complexity debt** from the workspace form/editing surface and residual narrow `Services/` helpers, **residual static catalog/builder helpers** in suggestions and companions, and **incomplete consolidation** of a few `Task.Run` fire-and-forget sites. The trust/security model, command-ID contract, launch/row caches, and lifecycle ownership have been addressed or significantly improved.

## 2. Architecture at a Glance

### Solution map

| Project / Area | Role |
| --- | --- |
| `QuickShell.Core` | Domain logic, persistence, launch, health, git, terminals, classification, suggestions. **No CmdPal SDK dependency.** |
| `QuickShell` | PowerToys Command Palette extension (MSIX, out-of-process COM server, Adaptive Card pages, command routing). |
| `QuickShell.Run` | PowerToys Run Wox plugin (`qs` keyword); reuses Core. |
| `QuickShell.Core.Tests` | xUnit tests for Core and some host-adjacent behavior. |
| `QuickShell.Suggest` | Console CLI emitting JSON suggestion pills for Raycast. |
| `QuickShell.Raycast/` | npm/TypeScript Raycast extension; mirrors product concepts but does **not** load Core. |

Stack: .NET 10, `net10.0-windows10.0.26100.0`, Windows App SDK / CsWinRT / MSIX, AOT + trimming, NuGet central package management. Version is pinned in `Directory.Build.props` (`0.2.0.0`).

### Data model

- `TerminalShortcut` = a workspace (name, directory, launches, companion, dev server, pin, abbreviation).
- `WorkspaceEntry` = one terminal row inside a workspace.
- On disk layout is a **versioned envelope** in `%LOCALAPPDATA%\QuickShell\shortcuts.json`:

  { "version": 1, "entries": [ … shortcuts + separators … ] }
  
- `settings.json` stores global terminal / multi-launch / git-launch preferences.

### Launch pipeline (`QuickShell.Core/Services/ShortcutLaunchExecutor.cs`)

  1. WorkspaceGitLaunchGate                       ← branch switch / dirty block
  2. CompanionAppLauncher (full workspace only)
  3. Single row → TerminalLauncher.Open
     Multi     → Resolve → GroupPlans → OpenGroup ("; new-tab")
  4. BuildPostLaunchResult (dev server, warnings, dismiss/stay)

```csharp
public async Task<PostLaunchResult> Execute(WorkspaceShortcut shortcut)
{
  // ... (omitted for brevity)
  var postLaunchResult = await BuildPostLaunchResult(shortcut, launchPlan);
  return postLaunchResult;
}

## 3. Strengths

1. **Clear host/core split.** `QuickShell.Core` can be unit-tested and reused; CmdPal and Run share it.
2. **Modern .NET packaging.** AOT/trimming, source-generated JSON, MSIX with proper identity, Store/WinGet/Release variants.
3. **Rich domain model.** Workspaces, multi-launch rows, companions, git worktrees, health, undo/redo, import/export, section separators — all modeled and persisted.
4. **Atomic persistence.** `ShortcutRepository` writes a temp file then `File.Replace`, with a process-wide named mutex, a `SemaphoreSlim`, backup `.bak`, and versioned envelope.
5. **Typed command routing exists.** `CommandDescriptor` + `CommandKind` + `ICommandRouter` is already in place, replacing much of the earlier string-munging.
6. **Good test seams.** `FakeShortcutRepository`, `LaunchExecutorTestEnvironment`, process override hooks, `InternalsVisibleTo`. No heavy mocking frameworks.
7. **Developer tooling.** Build/deploy scripts, local CmdPal SDK override, CI matrix for CmdPal + Run + Raycast, architecture tours under `docs/architecture/`.

## 4. Tech Debt & Risk Areas

### 4.1 Incomplete DI migration (highest leverage fix)

**Evidence:**

- `AddQuickShellCore` registers core services by interface to real instance services. The wrapper+static-backer table from earlier snapshots is gone; `IWorkspaceLaunchService`, `IWorkspaceRowPresentationCache`, and other new services are registered directly (`QuickShell.Core/Composition/QuickShellServiceCollectionExtensions.cs`).
- `QuickShellServices` is a constructor-injected aggregate implementing `IQuickShellServices`. It has no `Current` static property; a production search for `QuickShellServices.Current` returns **0** hits.
- `ProjectAnalysisAccessor` is only referenced in `RuntimeStaticStateGuardsTests.cs` as a banned substring, not in production code.
- `QuickShell.Core/Services` contains **~116 `.cs` files / ~18.4 kLOC** and **~69 `static class` declarations** across Core.
- Residual static state remains in `RowPresentationDiagnostics` (process-wide counters) and several suggestion/companion catalog/builder helpers.

**Impact:** DI covers the hot path, but residual static helpers and a few `Task.Run` sites still make some pages/commands hard to unit-test in isolation.

**Recommended direction:** Finish extracting the remaining static helpers (`TaskTypeCandidateBuilder`, `WorkspaceSetupSuggestion`, `SuggestionPillPresentation`, `SuggestCommandLineArgs`, `AgentCliCatalog`, `SettingsFormHelpers`) behind instance interfaces registered in `AddQuickShellCore`; give `WorkspaceRowEnrichmentCoordinator` a proper `CancellationToken` instead of the default `Task.Run` scheduler.

### 4.2 Command routing ID contract is now consolidated

**Evidence:**

- `CommandDescriptor` owns the whole deep-link schema: prefix constants, `VariantSuffix`, factories (`OpenWorkspace`, `OpenLaunch`, `DiscoverCreate`, `WorkspaceStatus`, `WorktreeBranchPicker`, `WorktreeBranchSelect`, `WorktreeBranchClear`, `FavoriteToggle`, `FavoriteMove`), and page-ID factories (`NewWorkspaceFormPageId`, `EditWorkspaceFormPageId`, etc.).
- The parsing is centralized in `CommandDescriptor.Parser`; `CommandIdParser` is a thin adapter.
- The earlier `QuickShellDeepLinkIds`, `ShortcutCommandIds`, and `CommandIdEncoding` files no longer exist.

**Impact:** The ID schema is now defined in one place; deep-link stability is easier to reason about and test.

**Recommended direction:** Keep `CommandDescriptor` as the single owner of the contract; add any new kinds or factories there and extend `CommandIdParser` only as an adapter.

### 4.3 Service explosion / static intelligence helpers

**Evidence:**

- `QuickShell.Core/Services` has many narrow `*Discovery`, `*Actions`, `*Form*`, `*Catalog`, and `*Cache` helpers.
- Classification is fully registry-ized through `IEnumerable<IProjectClassifier>` (13 implementations).
- The suggestion/companion path is mostly DI-registered:
  - `CommandSuggestionService` is an instance service consuming `IEnumerable<ITaskSuggestionProvider>`.
  - `TaskTypeCatalog` is `ITaskTypeCatalog`.
  - `CompanionAppCatalog` is `ICompanionAppCatalog`.
  - `CompanionAppLauncher` is `ICompanionAppLauncher`.
  - `ProjectClassificationCache` is `IProjectClassificationCache`.
  - `ICompanionAppDetector`, `ICompanionAppNormalization`, `ICompanionAppArgumentValidation`, and `IInstallDiscovery` are registered as instances.
- Static helpers that still remain:
  - `TaskTypeCandidateBuilder`
  - `WorkspaceSetupSuggestion`
  - `SuggestionPillPresentation`
  - `SuggestCommandLineArgs`
  - `AgentCliCatalog`
  - `WorkspaceCompanionSignals`
  - `SettingsFormHelpers`

**Impact:** Adding a new pill source or companion preset still requires touching several static files; ordering/scoring logic is still partly buried.

**Recommended direction:** Define `ITaskSuggestionProvider` / `ITaskTypeCandidateSource` (or expand the existing registry) and move the static builders into DI-registered providers. Delete duplicate or leftover static companion/suggestion helpers once their logic is subsumed by instance services.

### 4.4 Cancellation, dispose, and lifecycle gaps

**Evidence:**

- `QuickShellLifetime` is registered as `IQuickShellLifetime` and owns the root `CancellationTokenSource` (`QuickShell.Core/Services/QuickShellLifetime.cs`).
- `QuickShellExtension.Dispose()` disposes the provider, which cancels the lifetime and cascades disposal to pages and the service provider (`QuickShell/QuickShell.cs:28-32`).
- `WorkspaceRowEnrichmentCoordinator` is `IDisposable`, uses `IExtensionCallbackQueue` for UI marshaling, discards stale refresh results, and tracks refresh identity (`QuickShell/Services/WorkspaceRowEnrichmentCoordinator.cs`).
- `ProjectClassificationCache` and `GitRepoIndex` are instance services with bounded size/TTL and `Dispose` support; `ShortcutRepository.Dispose` safely drains the persist timer.
- Still un-canceled fire-and-forget `Task.Run` sites:
  - `SettingsFormHelpers.ScheduleRefresh`
  - `QuickShellPage` profile-prewarm / directory-repair probes
  - `WorkspaceRowEnrichmentCoordinator`'s default `Action<Action>` scheduler uses `Task.Run(...)` with no `CancellationToken`
- Mutable static state still exists in `RowPresentationDiagnostics` (process-wide counters) and `SupportDiagnostics` (log-path overrides).

**Impact:** The extension lifecycle and major background services are now controlled, but a few scheduler/test seams still carry process-wide mutable state and can race during reloads.

**Recommended direction:** Replace the remaining `Task.Run` defaults with `IQuickShellLifetime`-aware scheduling; remove static test overrides from diagnostics by making them constructor-injectable or by deleting the static mutable fields.

### 4.5 Raycast / host parity drift

**Evidence:**

- Raycast does not load `QuickShell.Core`; it reimplements storage, schema, launch grouping, health, and now the trust model in TypeScript.
- `QuickShell.Raycast/src/lib/security.ts` mirrors the C# `WorkspaceSecurityPolicy`: per-workspace trust, review tokens, `authorize`, `authorizePostLaunchEffects`, and local-directory/URL guards.
- `launch-executor.ts`, `workspace-health.ts`, `post-launch-actions.ts`, and `open-workspace.tsx` were updated to enforce trust and group launches.
- Still missing in Raycast:
  - `worktree-branch-targets.json` integration and the `WorkspaceGitLaunchGate` dirty/branch block.
  - Reuse of the C# `CompanionAppCatalog` presets (it reimplements executable resolution).
  - Shared on-disk storage with CmdPal/Run; Raycast keeps its own `STORAGE_KEY` blob unless the user manually imports/exports.

**Impact:** Trust, launch grouping, and basic health are now at parity, but git worktree gating, companion-preset reuse, and storage unification remain manual maintenance burdens.

**Recommended direction:** Keep parity explicit through `docs/architecture/parity-matrix.md`. Do not add new Raycast-only launch or security behavior without updating the matrix; consider a shared JSONL or small Core-hosted service for worktree branch targets if Raycast needs the feature.

### 4.6 Form / editing complexity

**Evidence:**

- `ShortcutFormPage.cs`, `ShortcutForm.cs`, `ShortcutDetailsFormPage.cs`, `ShortcutTransferSettingsForm.cs`, and `PendingShortcutEditPage.cs` remain large CmdPal-facing pages.
- `WorkspaceEditor` in `QuickShell/Services/WorkspaceEditor/` has been partially extracted but is still a large partial class (~47 KB) handling draft state, undo/redo, suggestion scanning, save/discard, companion rows, and form-state cloning.
- `ShortcutForm` still mixes Adaptive Card template caching (`ShortcutFormTemplateCache`/`ShortcutFormTemplateJson`), action dispatch, and clipboard/folder parsing.
- Two independent undo models remain: form-local launch-row history in `WorkspaceEditor` and full repository layout history in `ShortcutRepository`.
- Disk drafts for in-progress edits (`shortcut-edit-draft.json`) plus pending-edit pages and import-conflict pages still exist.

**Impact:** The in-palette editor is a differentiator but a large maintenance surface; Adaptive Card SDK churn and duplicated template/data JSON construction increase risk.

**Recommended direction:** Keep the in-palette UX, but move all Adaptive Card JSON construction into a single `ShortcutFormViewBuilder` driven by `WorkspaceEditState`. Reduce `ShortcutForm` to a thin mapper of UI events to `WorkspaceEditor` and `CommandResult`. Consolidate or document the relationship between form-local undo and repository-level undo.

### 4.7 Security / trust surface — addressed

**Status:** Implemented by the repository-owned trust boundary.

Implementation status: addressed by the repository-owned trust boundary, centralized action authorization, revision-bound review confirmation, and host launch audit described in [trust-model.md](./trust-model.md).

**Evidence:**

- `WorkspaceSecurityPolicy` authorizes every external effect (`LaunchTerminal`, `LaunchEntry`, `StartCompanion`, `OpenUrl`, `OpenDevServer`, `OpenDirectory`, `CopyPath`, `GrantTrust`, `RevokeTrust`) and returns issues, risks, and exact effective values (`QuickShell.Core/Services/WorkspaceSecurityPolicy.cs`).
- `StoredWorkspace` pairs portable `TerminalShortcut` content with repository-owned `WorkspaceSecurityMetadata` (`IsTrusted`, monotonic `Revision`) (`QuickShell.Core/Models/WorkspaceSecurityMetadata.cs`).
- `WorkspaceLaunchService` is the ID-based launch chokepoint: it reloads the current workspace, authorizes, clones content via `WorkspaceClone`, and hands the executor only the approved copy (`QuickShell.Core/Services/WorkspaceLaunchService.cs`).
- `GrantWorkspaceTrustCommand` / `RevokeWorkspaceTrustCommand` provide two-phase review-token confirmation (`QuickShell/Commands/WorkspaceTrustCommands.cs`).
- `ShortcutRepository` owns `BeginTrustReview`, `GrantTrust`, and `RevokeTrust` transitions under its lock (`QuickShell.Core/Services/ShortcutRepository.cs`).
- Import/restore/synced/community records start **untrusted**; local/manual/duplicated records start trusted; export omits trust metadata.
- Raycast parity: `QuickShell.Raycast/src/lib/security.ts` implements `authorize`, review tokens, and post-launch effect authorization.
- `docs/architecture/trust-model.md` documents the threat model and non-goals.

**Remaining risk:** Trust is a local-store decision, not a tamper-detecting hash. It does not protect against an attacker who can modify QuickShell persistence, executable replacement after review, or a trusted command that downloads more code.

**Recommended direction:** Keep trust metadata under the repository lock; ensure future form/import changes preserve the trust rules documented in `trust-model.md`.

### 4.8 Performance / responsiveness risks

**Evidence:**

- `WorkspaceRowPresentationCache` provides bounded (`MaxShortcutCount * 3`), version-pruned immutable presentation data; builds avoid icon extraction, git IO, and directory-existence probes (`QuickShell.Core/Services/WorkspaceRowPresentationCache.cs`).
- `WorkspaceRowEnrichmentCoordinator` defers terminal profile icon upgrades off the first-paint path and discards stale results (`QuickShell/Services/WorkspaceRowEnrichmentCoordinator.cs`).
- `WorkspaceLaunchPlanCache` provides bounded (`MaxEntries = 50`), revision-keyed, single-flighted launch plan resolution (`QuickShell.Core/Services/WorkspaceLaunchPlanCache.cs`).
- `ProjectClassificationCache` has a max of 64 entries and signature-based invalidation.
- `GitRepoIndex` uses a TTL-based `CacheLifetime`.
- `CommandSuggestionService` caches results with a 2500 ms TTL.
- Health checks are still used on launch; list-render presentation no longer triggers expensive probes.

**Impact:** The major first-paint and launch hot paths now have explicit cache invalidation; remaining risk is mostly in very large workspace counts or slow git operations on the launch path.

**Recommended direction:** Measure provider ctor, list reload, and discover scan times with real workspace counts before further optimization; add ETW/structured support diagnostics around any remaining slow paths.

### 4.9 Secondary maintainability nits

- `UseWindowsForms` is set in `QuickShell/QuickShell.csproj` and `QuickShell.Run/QuickShell.Run.csproj` to support `FolderPickerService` / `StaClipboard`; `QuickShell.Core` does **not** enable WinForms.
- `Microsoft.Web.WebView2` is not pinned in `Directory.Packages.props` and is not referenced by any project.

## 5. Quantitative Snapshot

| Metric | Value |
| --- | --- |
| Total `.cs` files | ~426 |
| Total C# LOC (desktop projects + tests) | ~48,800 |
| `QuickShell.Core/Services` files | ~116 |
| `QuickShell.Core/Services` LOC | ~18,400 |
| `QuickShell/Pages` files / LOC | 18 / ~4,000 |
| `QuickShell/Commands` files / LOC | 15 / ~850 |
| `QuickShell.Core/Classification` files / LOC | 24 / ~1,400 |
| `static class` declarations in `QuickShell.Core` | ~69 |
| `QuickShellServices.Current` references in `QuickShell/` | 0 |
| `IProjectClassifier` implementations | 13 |
| `Task.Run(` call sites in `QuickShell/` | 3 |
| Abstractions/interfaces in `QuickShell.Core` | ~15 |

## 6. Recommended Roadmap

Based on the existing architecture tours and the `remaining-architectural-gaps` doc, the next decisive work is:

### Tier 0 — Truth (already mostly done)

- Keep `proposal-status.md` and the parity matrix current as code changes.
- Treat `docs/architecture/*` as the as-built source of truth.

### Tier 1 — High-leverage engineering

1. **Finish DI for remaining static helpers.** Move the residual static catalog/builder helpers (`TaskTypeCandidateBuilder`, `WorkspaceSetupSuggestion`, `SuggestionPillPresentation`, `SuggestCommandLineArgs`, `AgentCliCatalog`, `SettingsFormHelpers`) behind instance interfaces registered in `AddQuickShellCore`. Migrate any remaining `QuickShellServices.Current` call sites (currently **0** in production) and keep the `RuntimeStaticStateGuardsTests` banned-substring list current.
2. **Suggestion / companion registry.** Expand `IEnumerable<ITaskSuggestionProvider>` so `TaskTypeCandidateBuilder` logic becomes registered providers; delete leftover static companion/suggestion duplication.
3. **Command ID contract is frozen.** Keep `CommandDescriptor` as the single owner; do not add new `QuickShellDeepLinkIds`/`ShortcutCommandIds`/`CommandIdEncoding` files.
4. **Root lifetime / cancellation.** Replace remaining `Task.Run` defaults with `IQuickShellLifetime`-aware scheduling; remove static mutable fields from `RowPresentationDiagnostics` and `SupportDiagnostics`.

### Tier 2 — Product-quality fixes

- [x] `WorkspaceHealthCheck` resolves every enabled `same-as-previous` row before validating its effective terminal, profile, WSL distro, and executable.
- [x] Companion detection includes current desktop IDEs, including TRAE’s `.trae/` workspace marker and installed executable preset.
- [x] Support diagnostics use bounded, redacted JSONL logs plus a copyable aggregate support bundle; detailed launch diagnostics remain an explicit user action.
- [x] Workspace trust/security model (`WorkspaceSecurityPolicy`, `WorkspaceLaunchService`, `StoredWorkspace` + `WorkspaceSecurityMetadata`, `GrantWorkspaceTrustCommand`/`RevokeWorkspaceTrustCommand`, `docs/architecture/trust-model.md`).
- [x] Immutable row presentation cache + deferred icon enrichment (`WorkspaceRowPresentationCache`, `WorkspaceRowEnrichmentCoordinator`, `IWorkspaceRowPresentationCache`).
- [x] Revision-keyed launch plan cache (`WorkspaceLaunchPlanCache`, `ResolvedWorkspaceLaunchPlan`, `LaunchPlanCacheKey`).

### Tier 3 — Performance (measure and tune)

- Row presentation cache, launch plan cache, project classification cache, and git index TTL are implemented.
- [x] Structured ETW (`QuickShell-Diagnostics`) beside support JSONL (see `diagnostics.md`).
- [x] Perf harness CI artifact upload (`Category=PerformanceMeasurement`); critical-path contracts remain the blocking gate.
- Measure provider ctor time, list reload, and discover scan time with real workspace counts before adding more caching layers.

### Tier 4 — Non-goals

- Fourth host / standalone app
- Cloud sync of workspaces
- Deep monorepo crawling for every `package.json`
- Rewriting Raycast onto Core via FFI (parity matrix first)

## 7. Bottom Line

QuickShell has a **strong core** — a reusable workspace-launch engine with good persistence, health, git, trust, terminal, and cache abstractions. The biggest remaining payoffs are finishing the **residual static helpers** (suggestions, companions, diagnostics, form scheduling), consolidating the **workspace form/editing surface**, and keeping **Raycast parity** explicit through the parity matrix. Do that, and new hosts, new pill sources, and new companion presets become additive rather than invasive.

---

*Sources: `docs/architecture/*.md`, `AGENTS.md`, `README.md`, and current source under `tonythethompson/QuickShell`.*
