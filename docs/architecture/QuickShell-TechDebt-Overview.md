# QuickShell — Tech Debt & Architectural Overview

*Windows-only .NET 10 workspace launcher: PowerToys Command Palette extension + PowerToys Run plugin + Raycast extension.*

## 1. Executive Summary

**Overall health: B+ / solid but complex.**

QuickShell is a well-architected, feature-rich launcher built around a clean separation between a reusable .NET `QuickShell.Core` library and two Windows UI hosts (CmdPal, PowerToys Run), plus a parallel TypeScript Raycast extension. It persists user-defined “workspaces” (project folders + terminal launches, companion apps, git branch targets, dev-server URLs), validates them before launch, opens them in the user’s preferred terminal (Windows Terminal / Intelligent Terminal / WSL / classic shells), and can run post-launch companion apps or browser URLs.

The **foundations are strong**: Core has no CmdPal SDK dependency, DI is partially wired, persistence is atomic with a versioned envelope, typed command routing exists, and the launch/health/git pipeline is sophisticated. The main risks are **complexity debt** from ~90 narrow `Services/` helpers (many still `static`), an **incomplete DI migration** that still relies on a static service locator, **fragile command-ID plumbing**, and **weak cancellation/lifecycle ownership** in a long-lived CmdPal host.

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

### Layers

```
CmdPal pages/commands ──┐
PowerToys Run plugin ───┼──► DI / service facade ──► QuickShell.Core
Raycast TS UI ─────────┘          (shared desktop)          (domain)
```

### Hosting

- `QuickShellExtension` (`QuickShell/QuickShell.cs`) implements `IExtension` with a stable `[Guid]` and a `ManualResetEvent` dispose signal.
- `QuickShellCommandsProvider` (`QuickShell/QuickShellCommandsProvider.cs`) is the composition root: it builds the `ServiceProvider`, wires fallback/top-level commands, and exposes `GetCommandItem(id)` for deep links.

### Data model

- `TerminalShortcut` = a workspace (name, directory, launches, companion, dev server, pin, abbreviation).
- `WorkspaceEntry` = one terminal row inside a workspace.
- On disk layout is a **versioned envelope** in `%LOCALAPPDATA%\QuickShell\shortcuts.json`:

  ```json
  { "version": 1, "entries": [ … shortcuts + separators … ] }
  ```

- `worktree-branch-targets.json` stores per-worktree git branch targets.
- `settings.json` stores global terminal / multi-launch / git-launch preferences.

### Launch pipeline (`QuickShell.Core/Services/ShortcutLaunchExecutor.cs`)

```
Launch / LaunchEntry
  1. EnsureLaunchesFromLegacy
  2. WorkspaceHealthCheck.Check / CheckEntry      ← blocking errors stop
  3. Directory exists
  4. WorkspaceGitLaunchGate                       ← branch switch / dirty block
  5. CompanionAppLauncher (full workspace only)
  6. Single row → TerminalLauncher.Open
     Multi     → Resolve → GroupPlans → OpenGroup ("; new-tab")
  7. BuildPostLaunchResult (dev server, warnings, dismiss/stay)
```

### Command routing

- Deep-link strings → `CommandIdParser` → `CommandDescriptor` (kind + ids) → `CommandRouter` → `ICommandItemHandler` → page/command.
- 11 `CommandKind` values cover open/create/discover/status/settings/import/etc.

### Intelligence / suggestions

- `IProjectClassifier` implementations detect stacks from `package.json`, `*.csproj`, `docker-compose.yml`, `Taskfile`, etc.
- `TaskTypeCatalog` labels rows (`api`, `frontend`, `services`, `logs`, `test`, `build`, `agent`).
- `CommandSuggestionService.GetPills` merges, scores, and caps suggestion pills.
- `AgentCliSuggestion` adds PATH/markers-based agent CLI pills (`claude`, `codex`, `cursor-agent`, etc.).

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

- `AddQuickShellCore` registers ~30 services, but several “services” are thin wrappers that immediately delegate to `static` classes:

  | Interface | Service | Static backer |
  | --- | --- | --- |
  | `ITerminalLauncher` | `TerminalLauncherService` | `TerminalLauncher` |
  | `IWorkspaceHealthChecker` | `WorkspaceHealthCheckerService` | `WorkspaceHealthCheck` |
  | `ITerminalProfileResolver` | `TerminalProfileResolverService` | `TerminalProfileResolver` |
  | `IWorkspaceMapper` | `WorkspaceMapperService` | `WorkspaceMapper` |
  | `IWorkspaceGitOperations` | `WorkspaceGitOperationsService` | `WorkspaceGitOperations` |
  | `IGitRepoIndex` | `GitRepoIndexService` | `GitRepoIndex` |

- The CmdPal host still resolves shared state through `QuickShellServices.Current` — a static service locator. As of this audit, **27 files** in `QuickShell/` use `QuickShellServices.Current`.
- `ProjectAnalysisAccessor.Instance` is a static mutator set from the provider constructor.
- `QuickShell.Core/Services` contains **117 `.cs` files / ~17.6 kLOC** and **91 `static class` declarations** across Core.

**Impact:** Tests against the wrapper test almost nothing; pages/commands cannot be unit-tested in isolation; adding a feature requires touching the static hub and the facade.

**Recommended direction:** Inline the 6 wrappers into real instance services, migrate the 27 call sites to constructor injection, and delete `QuickShellServices.Current`.

### 4.2 Command routing ID contract is scattered

**Evidence:**

- The deep-link format is split across **four** files:
  - `QuickShellDeepLinkIds` — prefix constants
  - `ShortcutCommandIds` — ID builders
  - `CommandIdParser` — parsers
  - `CommandIdEncoding` — serialization
- `.admin`/`.standard` suffix stripping lives in the parser; builders do not append them — the suffix is added ad-hoc by `ShortcutFieldButtonFactory` call sites.
- No `CommandDescriptor.ForOpenWorkspace(id)` factory exists.

**Impact:** The ID string is a public contract (deep links survive in CmdPal history), but its schema is hard to reason about and easy to break.

**Recommended direction:** Add `CommandDescriptor` static factories, rebuild `ShortcutCommandIds` call sites, delete the scattered ID files, and document the frozen contract.

### 4.3 Service explosion / static intelligence helpers

**Evidence:**

- `QuickShell.Core/Services` has 117 files; many are narrow `*Discovery`, `*Actions`, `*Form*`, `*Catalog`, `*Cache` helpers.
- Classification has been partially registry-ized (`IProjectClassifier` with 13 implementations), but the **suggestion/companion half is not**:
  - `CommandSuggestionService` — static
  - `TaskTypeCommandSuggestion` — static, 500+ LOC
  - `TaskTypeCandidateBuilder` — static
  - `TaskTypeCatalog` — static
  - `SuggestionPillPresentation` — static
  - `SuggestCommandLineArgs` — static
  - `CompanionAppCatalog` — static
  - `CompanionAppDetection` — static (duplicates the DI `ICompanionAppDetector`)
  - `CompanionAppLauncher` — static
  - `WorkspaceCompanionSignals` — static
  - `WorkspaceSetupSuggestion` — static
  - `ProjectClassificationCache` — static `ConcurrentDictionary`

**Impact:** New pill sources or companion presets require editing multiple static files; ordering/scoring logic is buried.

**Recommended direction:** Define `ITaskSuggestionProvider`, extract pill providers from `TaskTypeCandidateBuilder`, register them in DI, and delete the duplicate static companion detector.

### 4.4 Cancellation, dispose, and lifecycle gaps

**Evidence:**

- `QuickShellExtension.Dispose()` only signals `_extensionDisposedEvent.Set()`; it **does not call** `_provider.Dispose()`. The provider’s real dispose chain (settings unsubscribe, page dispose, service unbind, `ServiceProvider.Dispose`) only runs if the host happens to trigger it.
- Fire-and-forget `Task.Run` sites without `CancellationToken`:
  - `KickoffGitRepoIndexPrewarm` (provider ctor)
  - `GitRepoIndex.StartRefreshLocked`
  - `GitRepoDiscovery.Discover`
  - `QuickShellServices.BeginShortcutPreload`
  - settings prewarm (`QuickShellCommandsProvider` ctor)
- Static mutable state survives provider instances:
  - `GitRepoIndex` — 6 static fields
  - `ProjectClassificationCache` — static `ConcurrentDictionary` + `Queue`

**Impact:** Background work can outlive the extension; static caches may leak or race across reloads; no clean shutdown path.

**Recommended direction:** Introduce a root `QuickShellLifetime` / `CancellationTokenSource`, thread the token through `IGitRepoIndex` and discovery, clear static caches on dispose, and wire `QuickShellExtension.Dispose` → cancel → dispose provider → set event.

### 4.5 Raycast / host parity drift

**Evidence:**

- Raycast does not load `QuickShell.Core`; it reimplements storage, schema, launch grouping, and health in TypeScript.
- Raycast has **no** `worktree-branch-targets.json` integration and no `blockDirtyBranchSwitch` gate.
- Companion presets, full health checks, and `GitRepoIndex` prewarm are weaker or absent in Raycast.
- CmdPal and Run share `%LOCALAPPDATA%\QuickShell\` JSON; Raycast uses its own `STORAGE_KEY` blob unless the user manually imports/exports.

**Impact:** Every desktop improvement must be manually mirrored in TypeScript, and the two ecosystems can diverge silently.

**Recommended direction:** Treat parity as a deliberate budget; use the parity matrix in `docs/architecture/parity-matrix.md` before adding a host-only feature; do not “fix” a Raycast gap unless product commits to it.

### 4.6 Form / editing complexity

**Evidence:**

- `ShortcutFormPage` is ~1,200 LOC, plus `ShortcutForm`, `ShortcutFormTemplateJson`, `FormEditHistory`, `LaunchRowListEditor`, `FormPayloadMerge`, `ShortcutDraftStore`, etc.
- Two independent undo stacks: form-local launch-row history and full repository layout history.
- Disk drafts for in-progress edits (`shortcut-edit-draft.json`) plus pending-edit pages and import-conflict pages.

**Impact:** The in-palette editor is a differentiator but a large maintenance surface; Adaptive Card SDK churn increases risk.

**Recommended direction:** Keep the in-palette UX, but extract a bounded “WorkspaceEditor” domain service and avoid adding more form-only state to Core.

### 4.7 Security / trust surface

Implementation status: addressed by the repository-owned trust boundary, centralized action authorization, revision-bound review confirmation, and host launch audit described in [trust-model.md](./trust-model.md).

**Evidence:**

- Workspaces can run arbitrary commands (`Command` field) and can be launched elevated (`RunAsAdmin`).
- `Import workspaces` merges user-provided JSON into the local store without a trust boundary.
- Companion apps / dev-server URLs are opened via `Process.Start` / browser.

**Impact:** A malicious or malformed imported workspace could run unwanted code or trigger UAC.

**Recommended direction:** Validate/sanitize paths and commands on load and before launch; consider a “trusted workspace” flag or hash for imported sets; document the trust model.

### 4.8 Performance / responsiveness risks

**Evidence:**

- Health checks, git status, and project classification can run on list render / selection paths.
- `GitRepoIndex` / classification caches have no TTL; there is no structured cache invalidation strategy.
- Prewarm is best-effort with empty `catch` blocks; failures are silent.

**Impact:** As workspace counts grow, list/open latency can degrade.

**Recommended direction:** Cache health snapshots with TTL or explicit invalidation; make expensive checks opt-in “Refresh” actions; add ETW/file logging for silent prewarm failures.

### 4.9 Secondary maintainability nits

- `UseWindowsForms = true` in Core pulls WinForms in just for `FolderPickerService` / `StaClipboard`; consider narrowing or replacing with WinRT/Storage APIs.
- `Microsoft.Web.WebView2` is pinned in `Directory.Packages.props` but no project references it.
- `QuickShell.Core` enables WinForms; this is a CmdPal-only extension at heart.

## 5. Quantitative Snapshot

| Metric | Value |
| --- | --- |
| Total `.cs` files | ~318 |
| Total C# LOC (desktop projects + tests) | ~42,500 |
| `QuickShell.Core/Services` files | 117 |
| `QuickShell.Core/Services` LOC | ~17,600 |
| `QuickShell/Pages` files / LOC | 17 / ~4,900 |
| `QuickShell/Commands` files / LOC | 15 / ~800 |
| `QuickShell.Core/Classification` files / LOC | 22 / ~1,300 |
| `static class` declarations in `QuickShell.Core` | ~91 |
| `QuickShellServices.Current` references in `QuickShell/` | 66 |
| `IProjectClassifier` implementations | 13 |
| `Task.Run(` call sites in `QuickShell/` | 6 |
| Abstractions/interfaces in `QuickShell.Core` | ~15 |

## 6. Recommended Roadmap

Based on the existing architecture tours and the `remaining-architectural-gaps` doc, the next decisive work is:

### Tier 0 — Truth (already mostly done)

- Keep `proposal-status.md` and the parity matrix current as code changes.
- Treat `docs/architecture/*` as the as-built source of truth.

### Tier 1 — High-leverage engineering

1. **Finish DI for hot paths.** Inline the 6 static-wrapper services, migrate the 27 `QuickShellServices.Current` call sites to constructor injection, and remove the static locator. This unlocks isolated unit tests for pages/commands.
2. **Suggestion / companion registry.** Add `ITaskSuggestionProvider`, register pill providers in DI, delete duplicate static companion detection, and keep classification/suggestion growth bounded.
3. **Freeze the command ID contract.** Move ID construction behind `CommandDescriptor` factories, delete `ShortcutCommandIds`/`QuickShellDeepLinkIds`/`CommandIdEncoding`, and document the deep-link schema.
4. **Root lifetime / cancellation.** Add `QuickShellLifetime`, propagate `CancellationToken` through git/discovery/preload, clear static caches on dispose, and wire extension dispose correctly.

### Tier 2 — Product-quality fixes

- [x] `WorkspaceHealthCheck` resolves every enabled `same-as-previous` row before validating its effective terminal, profile, WSL distro, and executable.
- [x] Companion detection includes current desktop IDEs, including TRAE’s `.trae/` workspace marker and installed executable preset.
- [x] Support diagnostics use bounded, redacted JSONL logs plus a copyable aggregate support bundle; detailed launch diagnostics remain an explicit user action.

### Tier 3 — Performance (only with numbers)

- Measure provider ctor time, list reload, and discover scan time before optimizing.
- If needed, add TTL’d health snapshots and bounded GitRepoIndex refresh.

### Tier 4 — Non-goals

- Fourth host / standalone app
- Cloud sync of workspaces
- Deep monorepo crawling for every `package.json`
- Rewriting Raycast onto Core via FFI (parity matrix first)

## 7. Bottom Line

QuickShell has a **strong core** — a reusable workspace-launch engine with good persistence, health, git, and terminal abstractions. The biggest payoff is completing the **DI migration and cancellation ownership** that is already half-built, then consolidating the **suggestion/intelligence helpers** behind a registry. Do that, and new hosts, new pill sources, and new companion presets become additive rather than invasive. Keep Raycast parity explicit and measured; do not let the TypeScript surface drift ahead of the documented parity matrix.

---

*Sources: `docs/architecture/*.md`, `AGENTS.md`, `README.md`, and current source under `tonythethompson/QuickShell`.*
