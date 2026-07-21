# QuickShell Tech Debt — Next Phases

This document is a concrete execution plan derived from `QuickShell-TechDebt-Overview.md`. It is ordered by dependency: Phase 1 removes the remaining static-state seams and fire-and-forget `Task.Run` sites that block parallel testing and clean host shutdown. Later phases assume those foundations are solid.

---

## High-level phase plan

### Phase 1 — Close residual static-state seams

**Goal:** Eliminate the remaining process-wide mutable static state, `Task.Run` fire-and-forget gaps, and static service seams so tests can run in parallel and the CmdPal host shuts down cleanly.

### Phase 2 — Consolidate the workspace form / editing surface

**Goal:** Reduce the in-palette editor maintenance surface while preserving the current UX. Extract a single `ShortcutFormViewBuilder` and thin `ShortcutForm`, split `WorkspaceEditor` into focused collaborators, and resolve or document the relationship between form-local undo and repository-level undo.

**Status (Implemented — manual validation pending):** `IShortcutFormViewBuilder` / `ShortcutFormViewBuilder` owns Adaptive Card JSON; `ShortcutForm` is a thin action mapper; `ShortcutFormPage` constructs editors only via `IWorkspaceEditorFactory`; `WorkspaceEditor` is split into Draft / Directory / Suggestions / Undo partials. Dual undo remains as documented in `forms.md` (form-local launch-row history + repository layout history); stacks are not merged.

### Phase 3 — Raycast parity (parallel LocalStorage)

**Goal:** Stop silent divergence between desktop hosts and the Raycast TypeScript extension without merging persistence.

**Storage decision (locked):** Keep Raycast `LocalStorage` (`quickshell-data`). Import/export remains the bridge. Do **not** share `%LOCALAPPDATA%\QuickShell\` stores, `worktree-branch-targets.json`, or `blockDirtyBranchSwitch` in this phase. ETW is out of scope.

**Status (Implemented — manual Store validation pending):** Stages 0–5 landed: trust/import/adversarial Vitest; Suggest.exe form wiring with local fallback; multi-companion form + preset catalog; health terminal-host checks + copyable launch diagnostics; matrix/hosts/README updated.

**Staged workstreams:**

| Stage | Focus | Exit |
|-------|--------|------|
| 0 | Freeze parity matrix intentional gaps; document this Phase 3 plan | Matrix + this section agree |
| 1 | Trust / import / adversarial Vitest mirroring Core Phase 4 contracts | Same issue classes covered in TS |
| 2 | Wire Suggest.exe pills into create/edit form (local heuristic fallback) | Pills when CLI present |
| 3 | Multi-companion form editing + preset catalog | Form manages multi companions |
| 4 | Health subset (terminal host) + launch diagnostics copy | Copyable failure summary |
| 5 | Docs / matrix / README closeout | Matrix matches shipped Raycast |

See [parity-matrix.md](./parity-matrix.md), [hosts.md](./hosts.md), [trust-model.md](./trust-model.md).

### Phase 4 — Measure, tune, and harden

**Goal:** Prove performance and reliability before adding new product surfaces. Add structured diagnostics/ETW, benchmark with real workspace counts, extend security adversarial tests, and add end-to-end tests for form, trust, and import flows.

**Status (landed):** EventSource (`QuickShell-Diagnostics`) beside support JSONL; perf harness CI artifacts; adversarial + form/trust/import flow tests. See detailed plan below.

---

## Phase 1 — Detailed plan

### Workstream A — Mutable static diagnostics → DI services

- **File:** `QuickShell/Services/SupportDiagnostics.cs`
  - **Current problem:** `internal static class` with mutable `LogDirectoryOverride`, `MaximumLogFileBytesOverride`, and `ResetForTests()`.
  - **Change:** Introduce `ISupportDiagnostics` and an `internal sealed class SupportDiagnostics` singleton registered in the host composition root. Replace static overrides with constructor-injected options or a test instance.

- **File:** `QuickShell.Core/Services/RowPresentationDiagnostics.cs`
  - **Current problem:** Static `ConcurrentDictionary` counters and `ResetForTests()`.
  - **Change:** Convert to `IRowPresentationDiagnostics` instance; inject into `WorkspaceRowPresentationCache` and `WorkspaceRowEnrichmentCoordinator`.

**Tests to update:**

- `SupportDiagnosticsTests.cs` — stop calling `ResetForTests`; use a test instance.
- `WorkspaceRowPresentationCacheTests` / `WorkspaceRowEnrichmentCoordinatorTests` — mock `IRowPresentationDiagnostics`.

### Workstream B — `Task.Run` fire-and-forget → `IQuickShellLifetime`-aware scheduling

- **Location:** `QuickShell/Services/SettingsFormHelpers.cs`
  - **Current problem:** `ScheduleRefresh` uses `Task.Run(async () => await Task.Delay(...))` with no `CancellationToken`.
  - **Change:** Convert to an instance scheduler or use `Task.Delay(delayMs, lifetime.CancellationToken)`; marshal the callback through `IExtensionCallbackQueue`.

- **Location:** `QuickShell/Pages/QuickShellPage.cs`
  - **Current problem:** Profile-prewarm / directory-repair probes likely run via `Task.Run`.
  - **Change:** Pass `IQuickShellLifetime.CancellationToken` into all async helpers; cancel on page `Dispose`.

- **Location:** `QuickShell/Services/WorkspaceRowEnrichmentCoordinator.cs`
  - **Current problem:** Default scheduler uses `Task.Run` without a token.
  - **Change:** Replace the default `Action<Action>` callback with an `IQuickShellLifetime`-aware scheduler or require one in the constructor; cancel pending enrichment on `Dispose`.

- **Location:** `QuickShell/Services/ShortcutFormCatalogPrewarm.cs` / `TerminalCatalogPrewarm.cs`
  - **Current problem:** Static `Task.Run` prewarm calls.
  - **Change:** Convert to instance services that accept `CancellationToken` and register them in the composition root.

**Test addition:**

Add an architecture test (regex or analyzer) that flags `Task.Run(` in production code without an adjacent `CancellationToken`.

### Workstream C — Suggestion/companion static catalogs → DI-registered providers

`CommandSuggestionService` already consumes `IEnumerable<ITaskSuggestionProvider>`. Make the existing static builders actual providers.

1. Replace `TaskTypeCandidateBuilder` scoring and candidate-building with project-scoped providers:
   - `NodeTaskSuggestionProvider`
   - `DockerComposeTaskSuggestionProvider`
   - `DenoTaskSuggestionProvider`
   - `DotNetTaskSuggestionProvider`
   - `TaskRunnerTaskSuggestionProvider`
2. Convert `WorkspaceSetupSuggestion` → `IWorkspaceSetupSuggestionProvider`.
3. Convert `AgentCliCatalog` / `AgentCliSuggestion` → `IPathMarkerTaskSuggestionProvider` that scans PATH and workspace marker files.
4. Convert `SuggestionPillPresentation` → `IFormSuggestionPillRenderer` so `ShortcutFormViewBuilder` can render pills without static helpers.
5. Keep `TaskTypeCatalog` as `ITaskTypeCatalog` (pure type mapping) registered as a singleton.
6. Ensure `ICompanionAppCatalog` and `IWorkspaceCompanionSignals` (there is already `WorkspaceCompanionSignalsInstance`) are used everywhere; delete remaining static calls from form/builder code.

**Registration:** `AddQuickShellCore` should register the new `ITaskSuggestionProvider` implementations. Update `AddQuickShellCommandRouting` if host-only registration is required.

**Tests:** Add provider unit tests and a composition test verifying all `ITaskSuggestionProvider` instances can be resolved and return distinct pill types.

### Workstream D — Form / presentation static caches → instance services

- **Static helper:** `ShortcutFormTemplateCache`
  - **New home:** `IShortcutFormTemplateCache` singleton keyed on a `WorkspaceEditState` snapshot.

- **Static helper:** `ShortcutFormTemplateJson` + `ShortcutFormCatalogPrewarm`
  - **New home:** `IFormTemplateBuilder` / `ShortcutFormViewBuilder` instance in `QuickShell/Services/`.

- **Static helper:** `TerminalCatalog` + `TerminalCatalogPrewarm` + `TerminalListIconCache` + `TerminalProfileIconResolver`
  - **New home:** `ITerminalCatalog` instance with cache and `IQuickShellLifetime`-aware refresh.

- **Static helper:** `FormPayloadMerge`
  - **New home:** Keep as pure static or merge into `IWorkspaceFormActionParser` if it needs context.

`ShortcutFormViewBuilder` should consume `ICommandSuggestionService`, `ITaskTypeCatalog`, `ICompanionAppCatalog`, `ITerminalCatalog`, and `IProjectAnalysisService` to build `TemplateJson` and `DataJson` from `WorkspaceEditState`.

### Workstream E — Test infrastructure cleanup

1. Remove `[CollectionBehavior(DisableTestParallelization = true)]` from `QuickShell.Core.Tests/AssemblyInfo.cs` once all static seams are gone.
2. Update `RuntimeStaticStateGuardsTests.cs`:
   - Remove banned substrings for patterns that no longer exist.
   - Add `static class RowPresentationDiagnostics` and `static class SupportDiagnostics` if they still exist after partial conversion.
3. Update `SupportDiagnosticsTests.cs` to use instance injection instead of `ResetForTests`.

### Workstream F — Static-class audit

Use the current inventory (≈69 static classes in `QuickShell.Core/Services` plus host static classes) and tag each as one of:

- **Pure / keep** — e.g. `WorkspacePath`, `WorkspaceClone`, `ShortcutValidation`, `PersistenceVersion`, `TerminalHostIds`, `FormActionGlyphs`, `WorkspaceFormTooltips`, `WorkspaceStatusLabels`, `WslPathResolver`, `JsonFileDocument`, `GitRepoSearchRoots`, `RunQueryScoring`, `ListSearchQuery`, `ImportConflictState`.
- **Extract to DI** — any class with mutable fields, caches, test overrides, or `Task.Run`: `SupportDiagnostics`, `RowPresentationDiagnostics`, `ShortcutFormTemplateCache`, `TerminalCatalog` family, `TerminalListIconCache`, `TerminalProfileIconResolver`, `ShortcutFormCatalogPrewarm`, `TerminalCatalogPrewarm`, `WorkspaceSetupSuggestion`, `AgentCliCatalog`, `TaskTypeCandidateBuilder` (or fold into providers), `SettingsFormHelpers`.
- **Already wrapped** — `WorkspaceCompanionSignals` has `WorkspaceCompanionSignalsInstance` and `IWorkspaceCompanionSignals`; update remaining call sites to inject the interface and remove static usage from pages/form builders.

### Phase 1 acceptance criteria

- `RuntimeStaticStateGuardsTests` passes and is updated.
- `dotnet test QuickShell.Core.Tests -c Release -p:Platform=x64` passes with `DisableTestParallelization` removed.
- `dotnet build QuickShell.sln -c Release -p:Platform=x64` has **0** warnings.
- `QuickShellServices.Current` remains **0** references in production code.
- No new `static` mutable state is introduced; pure static helpers are still allowed.
- Every `Task.Run` call in `QuickShell/` and `QuickShell.Core` is passed a `CancellationToken` from `IQuickShellLifetime` or an explicit `CancellationTokenSource` tied to object lifetime.

### Phase 1 exit milestone

The codebase has **no process-wide mutable static service seams**, `Task.Run` fire-and-forget is replaced with lifetime-aware scheduling, and `QuickShell.Core.Tests` can run in parallel. This unblocks isolated unit tests for pages/commands and is the foundation for Phase 2.

---

## Phase 3 — Detailed plan

### Workstream A — Trust / import / adversarial (Core Phase 4 contracts)

- Expand `QuickShell.Raycast/src/__tests__/security.test.ts` and add storage+authorize flow tests.
- Mirror issue classes from `WorkspaceSecurityAdversarialTests`, `WorkspaceTrustCommandTests`, and `FormTrustImportFlowTests`.
- Keep export content-only; import always untrusted for new/collision cases ([trust-model.md](./trust-model.md)).

### Workstream B — Suggest.exe form wiring

- Call `suggest-commands.ts` from create/edit `workspace-form.tsx`; fall back to `project-setup-suggestion.ts` when Suggest.exe is missing.
- Respect `QUICKSHELL_SUGGEST_EXE` and packaged `bin/QuickShell.Suggest.exe`.

### Workstream C — Companion UX

- Multi `companionApps` rows in the form; preset picker from a TS catalog mirrored from Core `CompanionAppCatalog`.
- Full detector parity and companion-before-terminals reorder are optional; document ordering difference if left as-is.

### Workstream D — Health and diagnostics subset

- Missing terminal executable / profile checks in `workspace-health.ts`.
- Copyable launch diagnostics summary (no ETW). Ports/process/git dirty gate remain intentional Partial/No.

### Workstream E — Docs closeout

- Update [parity-matrix.md](./parity-matrix.md), [hosts.md](./hosts.md), [companions.md](./companions.md), `QuickShell.Raycast/README.md`.

### Phase 3 acceptance criteria

- Parallel LocalStorage remains the only Raycast workspace store.
- Vitest covers adversarial + trust/import flows aligned with Core Phase 4 issue classes.
- Suggest pills appear when the CLI is available; heuristics remain fallback.
- Form edits multi companions with presets.
- Health catches missing terminal host; launch failures offer a diagnostics copy path.
- Matrix intentional gaps (shared store, git targets, ETW) stay explicit.

### Phase 3 exit milestone

**Reached (code + docs):** Raycast stops silently diverging on trust, suggestions, companions, and launch feedback while remaining a parallel TypeScript host with import/export as the desktop bridge. Manual Raycast Store validation remains optional.

---

## Phase 4 — Detailed plan

### Workstream A — Structured diagnostics / EventSource

- Add `IQuickShellEventSource` / `QuickShellEventSource` (`QuickShell-Diagnostics`) with stable event IDs for row cache, plan cache, startup spans, repository reports, support write success/failure, and git discovery completion.
- Bridge from `RowPresentationDiagnostics`, `ShortcutLaunchExecutor` plan-cache callbacks, `RepositoryDiagnostics`, `SupportDiagnostics`, `StartupPerformanceTrace`, and `GitRepoDiscovery`.
- Document the catalog in `docs/architecture/diagnostics.md`. Do **not** replace JSONL support logs or introduce OpenTelemetry.

### Workstream B — Benchmarks and CI tracking

- Keep `PerformanceRegressionHarnessTests` (`Category=PerformanceMeasurement`) as the wall-clock source of truth.
- Fix `performance.md` cache table to match landed `WorkspaceLaunchPlanCache` / `WorkspaceRowPresentationCache`.
- Add a non-blocking CI job that runs the measurement filter and uploads `artifacts/perf/*` (no absolute ms budgets).
- Record actual workspace count from real-machine measurements into `artifacts/perf/real-machine-workspace-count.json`.

### Workstream C — Security adversarial expansion

- Add `WorkspaceSecurityAdversarialTests` for `InvalidCommand` / control characters, URL schemes, UNC/pipe/env paths, malformed/oversized import, and newline injection in commands.
- Add invoke-level tests for `GrantWorkspaceTrustCommand` / `RevokeWorkspaceTrustCommand`.
- Keep symlink/junction resolution as a documented v1 non-goal in `trust-model.md`.

### Workstream D — Form / trust / import flow tests

- Service/command-level flows (no CmdPal COM UI automation):
  - Form: editor create → save → repository snapshot (+ form-local undo).
  - Trust: untrusted → grant (review token) → launch allowed → revoke → blocked.
  - Import: conflict pending → merge/replace/cancel decision path.

### Workstream E — Docs and phase exit

- Expand this Phase 4 section; keep Overview Tier 3 aligned when workstreams land.

### Phase 4 acceptance criteria

- EventSource + catalog documented; support JSONL unchanged in behavior.
- `performance.md` cache table accurate; perf artifact CI job exists.
- Adversarial + trust-command + form/trust/import flow tests green.
- `dotnet build QuickShell.sln -c Release -p:Platform=x64` has **0** warnings; Core tests green.

### Phase 4 exit milestone

Measurement and hardening gates exist before new product surfaces: ETW alongside support logs, tracked perf artifacts, expanded adversarial coverage, and flow-level regression tests for form/trust/import.
