# Performance bottleneck scan — QuickShell

Copy-paste prompt:

---

# Performance bottleneck scan — QuickShell

You are auditing **QuickShell** (Windows .NET 10 CmdPal / Run / Raycast workspace launcher) for remaining performance bottlenecks and optimization opportunities. Read-only first: do **not** implement fixes unless I explicitly ask after you report.

## Goal

Produce a prioritized inventory of **remaining** optimizations: both **quick wins** (hours, low risk) and **larger efforts** (days+, architectural or multi-surface). Prefer evidence from code paths, existing harnesses, and as-built docs over speculation.

## Grounding docs (read these first)

1. `docs/architecture/performance.md` — critical-path contract, cache table, harness
2. `docs/performance-audit.md` — what was already done / deferred
3. `docs/architecture/QuickShell-TechDebt-Overview.md` + `QuickShell-TechDebt-Phases.md` — known remaining debt
4. `docs/architecture/diagnostics.md` — ETW / plan-cache / row-cache event codes
5. Tours as needed: `launch.md`, `cmdpal-surface.md`, `persistence.md`, `git-and-discover.md`, `forms.md`, `intelligence.md`, `hosts.md`

Treat `0001`–`0005` proposals as possibly stale; prefer as-built tours + landed code.

## Critical-path contract (do not recommend violating these)

Enforced by `QuickShell.Core.Tests/Performance/CriticalPathContractTests.cs` and related tests:

| Path | Must not / must |
|------|-----------------|
| `QuickShellCommandsProvider` ctor | No sync repo load, git scan, companion enum, terminal profile probe, drive traverse |
| First list (`QuickShellPage.GetItems` / `RefreshItems`) | No git IO, icon extract, WSL/UNC existence probes, (menus still eager — known gap) |
| Root palette typing (`QuickShellFallback.UpdateQuery`) | One snapshot per query; no dir existence; suppress broad git on weak/local hits |
| Launch (`ShortcutLaunchExecutor.Launch`) | Always evaluate health/trust/git; launch-plan cache may only memoize deterministic prep |

Any recommendation that moves health/trust/git behind a cache is a **reject**. Soften only deterministic prep / presentation.

## Surfaces to scan

**Core hot paths**
- Startup / warmup: `StartupWarmupCoordinator`, `StartupPerformanceTrace`, provider ctor, `ShortcutRepository` preload
- Home list: `QuickShellPage`, `WorkspaceRowPresentationCache`, `WorkspaceRowEnrichmentCoordinator`, `ShortcutContextCommands` (eager `MoreCommands`)
- Search: `QuickShellFallback`, `RootPaletteSearchIndex`, `SearchDebouncer`, scoring
- Launch: `ShortcutLaunchExecutor`, `WorkspaceLaunchPlanCache`, `TerminalLauncher`, `WorkspaceGitLaunchGate`, `CompanionAppLauncher`, `WorkspaceHealthCheck`
- Persistence: `ShortcutRepository`, `AtomicFileWriter`, snapshot/clone churn, undo stacks
- Git: `GitRepoIndex`, discovery parallelism, status/cache TTL, launch-path git
- Intelligence: `ProjectAnalysisService` / classifiers, `CommandSuggestionService` TTL, form pill rebuilds
- Forms: `ShortcutFormTemplateJson` / `ShortcutFormTemplateCache`, Adaptive Card rebuild on every keystroke/action, terminal catalog refresh
- Companions / terminals: catalog caches, install discovery (`vswhere`, JetBrains), icon resolvers

**Hosts**
- CmdPal (primary): list + form + fallback
- `QuickShell.Run`: sync `Query()` constraints
- `QuickShell.Raycast` + `QuickShell.Suggest`: Node/TS parity, suggest CLI spawn cost

## Search patterns (use tools)

Hunt for:
- Sync disk / network IO on UI or provider construction paths (`File.`, `Directory.`, `Process.Start`, `HttpClient`, path existence)
- Unbounded or unbounded-looking allocations in list/search loops (LINQ in hot loops, repeated `ToList`/`ToArray`, string concat, defensive clones)
- Static mutable caches not tied to `IQuickShellLifetime` / invalidation
- `Task.Run` / fire-and-forget without cancellation or coalescing
- Double work: re-parse JSON, re-scan profiles, rebuild Adaptive Cards / context menus when a cache/revision key would suffice
- Lock / mutex hold times on `shortcuts.json` write path
- Raycast: repeated process spawns to Suggest, redundant FS reads, N+1 terminal discovery

Also inventory **existing** caches and note gaps vs `performance.md` table (stale docs count as findings).

## Known gaps already tracked (verify status, don’t rediscover as novel)

- Eager per-row `MoreCommands` on first paint (`performance.md` known gap)
- Residual static catalog/builder helpers and a few `Task.Run` sites (`TechDebt-Overview`)
- Defensive cloning on some repository getters (`performance-audit.md`)
- Sync Run plugin query API limits full async I/O

Mark each as: **still open**, **partially fixed**, or **done**.

## Method

1. Map call chains for: provider ctor -> first list -> typing -> launch -> form open/edit.
2. Cite concrete `file:line` (or symbol) evidence for each finding.
3. Estimate impact: **user-visible latency** (cold start, first paint, keystroke, launch click, form rebuild) vs **CPU/alloc only**.
4. Prefer measurement hooks already present (`StartupPerformanceTrace`, ETW `QuickShell-Diagnostics`, `PerformanceRegressionHarnessTests`, `BenchmarkRunner`) over inventing new wall-clock CI gates.
5. Do **not** propose behavior changes (trust/health/git semantics, persistence correctness) as “perf”.

## Output format

### A. Executive summary
5–10 sentences: where time still goes; biggest remaining bets.

### B. Quick wins (S)
Table: `ID | Area | Evidence | Why cheap | Expected win | Risk | Suggested test/harness touch`

### C. Medium (M)
Same table; may need DI/cache ownership or host-specific work.

### D. Large (L)
Same table; multi-PR / architectural (e.g. lazy menus, persist git index, form rebuild model).

### E. Do not touch / false friends
Things that look slow but are correct, rare, or already optimized.

### F. Recommended next 2 weeks
Ordered sequence: 3–5 items max, mix of S + one L spike if justified. Include “how to validate” (which test or trace).

### G. Open questions
Only where code is ambiguous or needs a runtime profile (`QUICKSHELL_STARTUP_TRACE=1`, PerfView on EventSource).

## Constraints

- Windows-only .NET 10; platform `-p:Platform=x64` for build/test.
- Do not edit `Directory.Build.props`.
- Do not break COM hosting / CmdPal `[Guid]` / MSIX identity.
- Keep Core free of CmdPal SDK dependency.
- Raycast is separate Node surface; call out host-specific vs Core-shared wins.
- No drive-by refactors; report first.

Start by reading `performance.md` and `CriticalPathContractTests.cs`, then walk the four critical paths with evidence.

---

Optional one-liner if you want a shorter kickoff: *"Run the QuickShell performance bottleneck scan prompt; report only, no code changes."*
