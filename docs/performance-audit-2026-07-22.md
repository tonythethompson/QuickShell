# QuickShell performance bottleneck scan

Audit date: 2026-07-22

Scope: remaining performance bottlenecks and optimization opportunities across CmdPal, Run, Raycast, and shared Core paths. This report is read-only and does not include fixes.

## A. Executive summary

The biggest remaining time sinks are no longer in the root palette itself; they’re concentrated in startup discovery, first-paint list assembly, and form/list enrichment. The clearest single win is terminal discovery on the critical path: the current implementation still shells out through serial `where.exe` lookups, and live measurement showed that the discovery fan-out alone accounts for roughly a second of cold-launch cost.

The second cluster is repeated work during list and form construction. A few call sites already cache the right data, but the surrounding presentation layer still recomputes workspace row state, suggestion pills, and form JSON more often than necessary. That makes the hot paths pay for stable data multiple times.

The third cluster is Raycast storage and health loading. The host already uses background work in some places, but the current load path can still re-parse storage and do per-workspace health work in ways that are fine functionally but expensive at scale.

Launch itself is mostly shaped by correctness constraints rather than simple caching. Health, trust, and git checks still need to stay live at launch time, so the remaining work there is limited to deterministic prep and lower-level discovery. The performance docs were slightly optimistic compared to current measurement, so the repo should treat the current artifact as the source of truth.

Overall, the remaining gains look real but uneven: a small number of short, safe changes should buy visible improvements, while the larger opportunities are architectural and should be validated with the existing harnesses rather than guessed at.

## B. Quick wins (S)

| ID | Area | Evidence | Why cheap | Expected win | Risk | Suggested test/harness touch |
|---|---|---|---|---|---|---|
| S1 | Terminal discovery | `QuickShell.Core/Services/TerminalCatalog.cs:895`, `QuickShell.Core/Services/TerminalCatalog.cs:915`; live `where.exe` fan-out measured at roughly 1.03s total | Replace serial process-based discovery with in-process PATH / known-location scanning plus explicit invalidation | Large cold-launch reduction, likely the biggest single win | Low if cache invalidation stays correct | Add/extend discovery benchmark and cold-launch trace around provider startup |
| S2 | Workspace form suggestion state | `QuickShell.Core/Services/WorkspaceEditor/WorkspaceEditor.cs:1416`, `QuickShell/Services/ShortcutFormViewBuilder.cs:46` | `BuildState` already computes the pills; the UI layer can reuse it instead of recomputing JSON/context repeatedly | Less form rebuild CPU and allocation | Low-medium, mostly wiring | Extend form rebuild tests and a focused UI-state benchmark |
| S3 | Disabled trust row loading | `QuickShell.Core/Classification/ProjectAnalysisService.cs:81`, `QuickShell/Services/ShortcutListItems.cs:59` | Avoid repeated `GetStoredWorkspace` work for UI rows that will never show trust controls | Cuts repeated list work on first paint | Low if behavior stays identical | Add row-list regression around trust-disabled workspace rendering |
| S4 | Raycast load coalescing | `QuickShell.Raycast/src/open-workspace.tsx:85`, `QuickShell.Raycast/src/open-workspace.tsx:95`, `QuickShell.Raycast/src/lib/storage.ts:185`, `QuickShell.Raycast/src/lib/storage.ts:199` | Collapse in-flight loads and avoid multiple parses when requests overlap | Better first interaction latency and less duplicate disk work | Low-medium | Add a concurrency test around repeated load calls |

## C. Medium (M)

| ID | Area | Evidence | Why cheap | Expected win | Risk | Suggested test/harness touch |
|---|---|---|---|---|---|---|
| M1 | Raycast health indexing | `QuickShell.Raycast/src/lib/workspace-health-index.ts:52`, `QuickShell.Raycast/src/lib/workspace-health.ts:86` | First paint appears to wait on port probes and per-workspace checks; a two-phase model can split visible readiness from background enrichment | Better perceived list responsiveness | Medium, needs careful state separation | Add a timing trace around the two phases and a focused Vitest case |
| M2 | Support diagnostics buffering | `QuickShell/Services/SupportDiagnostics.cs:94`, `QuickShell/Services/SupportDiagnostics.cs:107`, `QuickShell/Services/SupportDiagnostics.cs:121`, `QuickShell/Services/SupportDiagnostics.cs:127` | Buffer/coalesce synchronous file writes instead of emitting many small ones | Less sync I/O churn during diagnostic-heavy flows | Medium, because diagnostics are operationally useful | Add a trace or unit test around batching behavior |
| M3 | Repository clone churn | `QuickShell.Core/Services/ShortcutRepository.cs:161`, `QuickShell.Core/Services/ShortcutRepository.cs:171`, `QuickShell.Core/Services/ShortcutRepository.cs:869`, `QuickShell.Core/Services/ShortcutRepository.cs:1497`, `QuickShell.Core/Services/ShortcutRepository.cs:1536`, `QuickShell.Core/Services/ShortcutRepository.cs:1768` | Collapse repeated defensive cloning / normalization where the same data is revisited | Lower CPU and allocation on repeated read paths | Medium, because repository invariants matter | Add repository regression coverage and allocation-sensitive tests |
| M4 | Run query snapshotting | `QuickShell.Run/Main.cs:178`, `QuickShell.Run/Main.cs:537` | The sync host API is a constraint, but a revisioned snapshot/query view can reduce duplicate scans | Better responsiveness without changing the API shape | Medium, because sync constraints remain | Add a benchmark for repeated query calls |

## D. Large (L)

| ID | Area | Evidence | Why cheap | Expected win | Risk | Suggested test/harness touch |
|---|---|---|---|---|---|---|
| L1 | Lazy CmdPal MoreCommands | `QuickShell/QuickShellCommandsProvider.cs:38`, `docs/architecture/performance.md:40` | Eager menus are a known gap, but making them lazy touches the SDK surface and UI behavior | Big first-paint improvement | High, because it changes presentation sequencing | Use the performance contract tests and a CmdPal smoke trace |
| L2 | Immutable repository snapshots | `QuickShell.Core/Services/WorkspaceRepositorySnapshot.cs:26`, `QuickShell.Core/Services/ShortcutRepository.cs:161` | A deeper snapshot model would remove a lot of clone/recompute churn | Structural reduction in repeat work across hosts | High, because it’s a model change | Add regression tests around snapshot versioning and mutation behavior |
| L3 | Persist git index only if warranted | `QuickShell.Core/Services/ShortcutRepository.cs:1497`, `docs/performance-audit.md` | The current cold git scan is not the dominant cost compared with terminal discovery | Possible win in cold startup, but current evidence does not justify prioritizing it | High, because it can trade freshness for speed | Benchmark first; only proceed if the trace says it matters |
| L4 | Persistent/cancellable suggest worker | `QuickShell.Raycast/src/lib/suggest-commands.ts:70`, `QuickShell.Raycast/src/lib/suggest-commands.ts:83`, `QuickShell.Suggest/Program.cs:13` | Spawning the suggest CLI per request is a structural cost that likely needs a longer-lived process model | Better repeated suggestion latency | High, because it changes host/process boundaries | Add request-latency measurements before any redesign |

## E. Do not touch / false friends

- Root palette typing is not the bottleneck to chase first; the current contract there is mostly correct.
- Git discovery is not the first two-week priority unless a new trace shows a worse regression.
- Launch-plan caching is already the right shape; health, trust, and git still need to stay live at launch.
- First-list filesystem probes are largely under control already; the remaining visible gap is eager menus.
- The performance docs are slightly stale in places; current artifact data should outrank the older claim that cold launch is only around 15ms.
- Residual static catalogs are only perf-relevant in the form layer, not a blanket refactor target.

## F. Recommended next 2 weeks

1. Fix terminal discovery first.
   Validate with the existing startup trace and a focused benchmark on provider startup.

2. Remove the redundant form suggestion rebuilds.
   Validate with the form tests and a before/after rebuild trace.

3. Reduce trust-disabled row work in the home list.
   Validate with the list contract tests and a first-paint trace.

4. Coalesce Raycast storage loads and health lookups.
   Validate with Vitest plus a small timing probe around repeated loads.

5. If those land cleanly, spike lazy `MoreCommands`.
   Validate with the CmdPal contract tests and a first-paint trace before broadening the change.

## G. Open questions

- Whether the remaining terminal discovery work can be satisfied entirely with local PATH / known-location resolution, or whether any host-specific edge cases still require subprocess fallback.
- Whether Raycast list health can be split cleanly into visible readiness and background enrichment without making the UI feel inconsistent.
- Whether the current repository clone churn is worth a broader snapshot-model rewrite, or whether a narrower cache ownership change gets most of the gain.

## Notes on evidence

Current measurement from the perf artifact shows the main cold-start penalties are provider construction and terminal discovery, with the discovery fan-out dominating the visible pause. The scan also confirmed that the repository already has useful harnesses for startup traces, critical-path contracts, and regression coverage, so the best next step is to use those before broadening any architectural work.

Relevant files and traces:

- `artifacts/perf/quickshell-perf-results.md`
- `QuickShell.Core.Tests/Performance/CriticalPathContractTests.cs`
- `docs/architecture/performance.md`
- `docs/performance-audit.md`
- `QuickShell.Core/Services/TerminalCatalog.cs`
- `QuickShell.Core/Services/WorkspaceEditor/WorkspaceEditor.cs`
- `QuickShell/Services/ShortcutFormViewBuilder.cs`
- `QuickShell/Services/ShortcutListItems.cs`
- `QuickShell.Core/Classification/ProjectAnalysisService.cs`
- `QuickShell.Core/Services/ShortcutRepository.cs`
- `QuickShell/QuickShellCommandsProvider.cs`
- `QuickShell.Run/Main.cs`
- `QuickShell.Raycast/src/open-workspace.tsx`
- `QuickShell.Raycast/src/lib/storage.ts`
- `QuickShell.Raycast/src/lib/workspace-health-index.ts`
- `QuickShell.Raycast/src/lib/workspace-health.ts`
- `QuickShell.Raycast/src/lib/suggest-commands.ts`
- `QuickShell.Suggest/Program.cs`
