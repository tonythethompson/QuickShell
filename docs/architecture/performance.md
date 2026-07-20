# Performance: critical-path contract and regression harness

This document describes QuickShell's performance architecture after "Operation Snappy"
(the series of PRs that moved I/O off provider construction, first paint, and typing) and
the regression harness introduced to keep it that way. See also
[`docs/architecture/overview.md`](overview.md) for general architecture and
[`launch.md`](launch.md) / [`git-and-discover.md`](git-and-discover.md) for the subsystems
referenced below.

## Critical-path contract

These rules are enforced by deterministic tests in
[`QuickShell.Core.Tests/Performance/CriticalPathContractTests.cs`](../../QuickShell.Core.Tests/Performance/CriticalPathContractTests.cs)
plus the pre-existing `RootPaletteSearchTests.cs`. They use instrumented fakes and counters,
never wall-clock thresholds, so they run safely in CI.

**Provider construction** (`QuickShellCommandsProvider` ctor) must not:
- synchronously load the shortcut repository from disk
- scan Git roots
- enumerate companion applications
- probe terminal profiles (Windows Terminal settings, vswhere, PATH)
- traverse drives

All of the above are deferred to `StartupWarmupCoordinator`, which only starts after the
host signals the first real workspace list was published
(`IStartupWarmupCoordinator.SignalFirstListPublished`, see `StartupWarmupStages.cs`).

**First list construction** (`QuickShellPage.GetItems`/`RefreshItems`) must not:
- run Git
- extract executable icons
- probe WSL/UNC/network directory existence
- build every context menu eagerly

Today, list construction reuses `ShortcutHealth.WouldNeedRepair(shortcut, requireDirectoryExists: false)`
throughout so directory reachability is never checked synchronously; git tags/status are
read from `WorkspaceStatusService`'s cache only (`TryGetCached`, never `Capture`); and
Windows Terminal profile icon upgrades run on a background `Task.Run` after the list is
already published, applied through `IExtensionCallbackQueue`.

> **Known gap, tracked separately:** every row's `MoreCommands` context menu is still built
> eagerly during first list construction (`ShortcutContextCommands.Build`), reused across
> refreshes only via `QuickShellPage`'s `_unpinnedItemCache`. Lazy per-row menu construction
> and an immutable row-presentation cache are the subject of a follow-on PR; this harness
> measures `workspace-list` cold/warm construction cost today specifically so that change has
> a baseline to compare against, and `CriticalPathContractTests` intentionally does not assert
> "no menus built on first paint" until that lands.

**Root-palette typing** (`QuickShellFallback.UpdateQuery`) must not:
- reacquire multiple repository snapshots per query
- perform directory existence checks
- run broad Git discovery for one-character or strong local-match queries

Already enforced by `RootPaletteSearchTests`: `UpdateQuery_AcquiresSnapshotOncePerQuery`,
`Search_OneCharacterQuerySuppressesGit`, `Search_LocalWorkspaceHitSuppressesGit`,
`UpdateQuery_GenerationGuardIncrements`, `UpdateQuery_DoesNotApplyAnOlderOverlappingResult`,
`Index_ReusesSameRevision`, `Index_RebuildsAfterRepositoryRevisionChange`. This harness adds
two more (`RootPaletteQuery_OneCharacter_NeverCallsGitSearch`,
`RootPaletteQuery_StrongLocalMatch_DoesNotFallThroughToGitSearch`) that assert against a
counting `IGitRepoIndex` fake rather than timing.

**Launch** (`ShortcutLaunchExecutor.Launch`) must:
- resolve the workspace from fresh repository state
- enforce trust and health every time
- evaluate current Git state when required
- treat cached launch plans as deterministic preparation only

`WorkspaceLaunchPlanCache` may skip recomputation of *deterministic* preparation (argv
formatting, terminal resolution) but must never skip the health/trust/Git checks themselves.
See `CriticalPathContractTests.Launch_EvaluatesHealthAndGitEveryCall_NeverMemoizesAcrossLaunches`.

## Cache ownership and invalidation

| Cache | Owner | Key | Invalidation | Bounded | Persisted | Serves stale? | Excludes |
|---|---|---|---|---|---|---|---|
| Repository snapshot | `ShortcutRepository.GetSnapshot()` | none (latest) | new snapshot on every call; `Version` increments on mutation | N/A (one snapshot object per call) | no (backed by `shortcuts.json`) | no — always current | n/a |
| Root-palette query index | `QuickShellFallback._cachedSearchIndex` (`RootPaletteSearchIndex`) | implicit — one instance per `QuickShellFallback` | `snapshot.Version` mismatch rebuilds; per-query generation guard discards stale async results | 1 instance | memory-only | no | n/a |
| Persistent Git index | `GitRepoIndex` (in-memory only today) | `rootKey` (sorted, newline-joined search roots) + `includeDefaultSearchRoots` flag | `Invalidate()` (repository change), 10-minute TTL (`CacheLifetime`), or `rootKey` mismatch | 1 cache entry (single most-recent root set) | **no** — memory only, lost on process restart | yes, up to 10 minutes (stale-while-revalidate: `Search`/`GetAll` return the cached set immediately and kick a background refresh) | n/a |
| Launch-plan cache | `WorkspaceLaunchPlanCache` (owned by `ShortcutLaunchExecutor`) | `LaunchPlanCacheKey` (workspace id, repository version, terminal app, profile, launch entry, options fingerprint) | repository version bump evicts older keys; capacity trim (`MaxEntries = 50`) | 50 | memory-only | no — version-keyed | health, trust, and git gate results (always evaluated after cache lookup) |
| Row presentation cache | `WorkspaceRowPresentationCache` | workspace id + repository version + presentation mode | newer repository version prunes older entries | `MaxShortcutCount * 3` | memory-only | no — version-keyed | icon extraction, git IO, directory-existence probes |

Security-sensitive values excluded from every cache above and from diagnostics logging:
launch health results, process state, trust/authorization decisions, and directory-existence
results are always evaluated fresh at use (see the Launch contract). `SupportDiagnostics`
(`QuickShell/Services/SupportDiagnostics.cs`) redacts free-text messages and structured data
to SHA-256 hash tags before writing, and never logs full user paths, command contents, or
environment variables. ETW mirrors (`QuickShell-Diagnostics`) are documented in
[`diagnostics.md`](diagnostics.md).

## Benchmark harness

[`QuickShell.Core.Tests/Performance/PerformanceRegressionHarnessTests.cs`](../../QuickShell.Core.Tests/Performance/PerformanceRegressionHarnessTests.cs)
runs one consolidated pass across provider/startup, workspace-list construction (10, 100,
`ShortcutValidation.MaxShortcutCount` workspaces, mixed pinned/unpinned/WSL/UNC/invalid
shapes), root-palette search, Git discovery, launch, and writes:

- `artifacts/perf/quickshell-perf-results.json` — machine-readable (median/p95/min/max ms,
  mean allocated bytes, operation counts, environment metadata)
- `artifacts/perf/quickshell-perf-results.md` — human-readable summary table per category

Override the output location with the `QUICKSHELL_PERF_OUTPUT_DIR` environment variable.

### Running it

```powershell
# Full test suite, including the harness and all critical-path contract tests.
dotnet test QuickShell.Core.Tests/QuickShell.Core.Tests.csproj -c Release -p:Platform=x64

# Just the wall-clock harness (hardware-sensitive; safe to run standalone or on a schedule).
dotnet test QuickShell.Core.Tests/QuickShell.Core.Tests.csproj -c Release -p:Platform=x64 --filter Category=PerformanceMeasurement

# Just the deterministic, CI-blocking critical-path contracts.
dotnet test QuickShell.Core.Tests/QuickShell.Core.Tests.csproj -c Release -p:Platform=x64 --filter "FullyQualifiedName~CriticalPathContractTests|FullyQualifiedName~RootPaletteSearchTests"
```

### Representative baseline (development machine, Release x64)

Captured with the command above; **treat as a relative signal for this machine, not a
universal number** — see "Hardware-dependent metrics" below.

| Scenario | Median | Notes |
|---|---:|---|
| provider constructor | ~115 ms | cold JIT/COM activation; dominates first-open latency |
| first placeholder GetItems | ~16 ms | before any workspace load |
| cold home-list construction, 10 workspaces | ~4 ms | |
| cold home-list construction, 100 workspaces | ~3.5 ms | |
| cold home-list construction, 500 workspaces (`MaxShortcutCount`) | ~9.6 ms | |
| warm home-list construction, 500 workspaces | ~9.9 ms | row presentation cache may warm on repeat; harness still reports full construction cost |
| root-palette abbreviation/name/task hit | <0.1 ms | |
| cold Git discovery, 10 repos | ~37 ms | synthetic tree, no default roots |
| cold Git discovery, 100 repos | ~118 ms | |
| launch, cold | ~15 ms | first-call JIT/profile-resolution cost |
| launch, warm (repeated) | ~0.5 ms | |

Full output (all categories) is regenerated on every harness run at
`artifacts/perf/quickshell-perf-results.md`.

## Investigating a regression

1. Run the harness (`--filter Category=PerformanceMeasurement`) on the base commit and on
   the suspect commit, on the *same machine*, and diff the two Markdown/JSON artifacts.
2. If a `workspace-list`, `root-palette`, or `launch` median moves noticeably at a fixed
   workspace/repo count, check the operation-count columns first — a jump in git process
   invocations, snapshot acquisitions, or repository lock acquisitions points at a broken
   critical-path guarantee before you profile wall-clock at all.
3. Run the deterministic contract tests (`CriticalPathContractTests`, `RootPaletteSearchTests`)
   — if one of those started failing, that is the regression; fix the critical-path violation
   directly instead of chasing the timing number.
4. For a real slowdown with no contract violation, use `QUICKSHELL_STARTUP_TRACE=1` (see
   `StartupPerformanceTrace`) to get per-span timings from a live provider construction /
   list refresh, or attach a profiler around the specific `BenchmarkRunner.Measure` scenario.
5. Confirm the fix by rerunning the harness and re-diffing artifacts, not by eyeballing a
   single number — wall-clock has run-to-run noise; the JSON's median/p95/min/max spread
   tells you whether a change is inside normal noise.

## Hardware-dependent vs. CI-blocking

**Hardware-dependent (informational only, not asserted on):** everything in
`PerformanceRegressionHarnessTests` — all wall-clock medians/p95/min/max and allocation
numbers in the JSON/Markdown artifacts. These vary with CPU, disk, and background load and
must never be pinned to a fixed millisecond budget in ordinary unit tests.

**CI-blocking (deterministic, safe on any machine):**
- `CriticalPathContractTests` (this PR) — provider construction touches no repository/git
  state, first list construction runs no git process and probes no WSL/UNC directories,
  root-palette one-character/strong-local-match queries never reach git search, launch
  re-evaluates health/git on every call.
- `RootPaletteSearchTests` — single-snapshot-per-query, generation-guard staleness
  rejection, revision-driven index rebuild, one-character/local-hit git suppression.
- Existing allocation/shape tests: `ShortcutRepositoryPerformanceShapeTests`,
  `StartupPerformanceTraceTests`, `SupportDiagnosticsTests`.

## Diagnostics

Production diagnostics (`SupportDiagnostics.Write`/`WriteException`) use stable event codes
derived from `"File.cs:Method"` locations and short, fixed message strings (`"start"`,
`"complete"`, `"stage failed"`, …) — never per-investigation `hypothesisId`/`runId` tags or
`#region agent log` markers. Free-text messages and structured `data` payloads are hashed to
a bounded `message:sha256:…` / `data:present` tag before being written, so log output never
contains raw command lines, full file paths, or other user content by default.

Structured ETW events (`QuickShell-Diagnostics` EventSource) mirror cache and timing codes
without user content. Full catalog: [`diagnostics.md`](diagnostics.md).
