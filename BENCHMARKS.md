# Startup Performance Benchmarks

Cold-start timings for Quick Shell's CmdPal extension, measured from an xUnit harness so
they are reproducible as code changes land.

## What is measured

| Path | Code under test | Notes |
|------|----------------|-------|
| Discover scan | `GitRepoDiscovery.Discover` | Bounded: `MaxDirectoriesScanned=2000`, `MaxRepos=50`, `MaxDepth=5`, DOP=4 |
| Provider ctor | `QuickShellCommandsProvider` ctor | Background git prewarm + settings prewarm are fire-and-forget and do not block the ctor |
| List reload | `QuickShellPage.Reload()` | Builds the home-list rows from the workspace snapshot |
| List `GetItems` | `QuickShellPage.GetItems()` | Cached read after a reload |

Each number is the **warm** figure: the harness runs the path once to pay JIT / first-call
costs, then times a second invocation. The provider ctor also prints its internal breakdown
via the existing `QUICKSHELL_STARTUP_TRACE=1` instrumentation.

## How to run

```powershell
dotnet test QuickShell.Core.Tests/QuickShell.Core.Tests.csproj -c Release -p:Platform=x64 `
  --filter "FullyQualifiedName~StartupPerformanceMeasurementsTests" `
  --logger "console;verbosity=detailed"
```

- `Measure_ProviderCtor_ListReload_DiscoverScan` — **synthetic** baseline (isolated temp
  store, git roots disabled, 50 in-memory fake workspaces). Fast and machine-independent.
- `Measure_RealMachine_DiscoverScan_And_ListReload` — **representative** (scans the real user
  profile / drives for git repos and loads a read-only copy of `%LOCALAPPDATA%\QuickShell\shortcuts.json`).
  Nothing on disk is mutated.

Add new numbers below after meaningful merges. Capture `dotnet --version`, the TF, and a
one-line machine note (CPU / SSD) so comparisons stay honest.

## Results

### Run 1 — 2026-07-15

Environment: Windows, .NET 10 SDK 10.0.302, `net10.0-windows10.0.26100.0`, x64 Release.

**Synthetic baseline** (25-repo temp tree, 50 fake workspaces, empty settings):

| Path | Cold | Warm |
|------|------|------|
| Discover scan | 9.96 ms | 8.97 ms |
| Provider ctor | 124.81 ms (first/JIT) | 14.22 ms |
| List reload (50 workspaces) | 19.01 ms | — |
| List `GetItems` | — | 0 ms |

**Real machine** (real profile scan, 45 saved workspaces from real `shortcuts.json`):

| Path | Cold | Warm |
|------|------|------|
| Discover scan | 33.19 ms | 26.52 ms |
| Provider ctor | 129.95 ms (first/JIT) | 16.14 ms |
| List reload (45 workspaces) | 424.99 ms | — |
| List `GetItems` | — | 0.001 ms |

Provider ctor breakdown (warm): settings manager 3.57 ms, composition root 9.47 ms,
page setup 1.65 ms.

### Observations

- The **home-list build dominates real startup feel** (~425 ms for 45 real workspaces), not
  the provider ctor (~16 ms) or the discover scan (~33 ms). The synthetic baseline
  understated this ~9x because fake workspaces have no real directory/icon/git/health
  metadata to resolve per row.
- The discover scan is cheap and bounded; it will not exceed the 2000-dir / 50-repo caps.
- `GetItems` is effectively free after a reload — all cost is in the one-time row build.
- Provider ctor first-hit cost (~125 ms) is JIT + the first real `settings.json` read;
  subsequent constructions settle near ~16 ms.

## Historical runs

_Append new runs here, newest last._
