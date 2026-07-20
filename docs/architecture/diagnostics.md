# Diagnostics: support logs and ETW

Quick Shell emits two complementary diagnostic channels. Neither replaces the other.

| Channel | Purpose | Redaction |
|---------|---------|-----------|
| **Support JSONL** (`SupportDiagnostics`) | User-copyable support bundle + bounded rotating log under `%LOCALAPPDATA%\QuickShell\logs\` | Free-text and structured payloads hashed; no raw paths/commands |
| **ETW EventSource** (`QuickShell-Diagnostics`) | Machine-local timing and cache counters for PerfView / `dotnet-trace` | Stable event codes and counts only; no user content |

See also [`performance.md`](performance.md) (critical-path contract and harness) and [`launch.md`](launch.md) (plan-cache diagnostic kinds).

## EventSource catalog (`QuickShell-Diagnostics`)

Provider name: **`QuickShell-Diagnostics`**. Implementation: [`QuickShellEventSource`](../../QuickShell.Core/Services/QuickShellEventSource.cs) (`IQuickShellEventSource`).

| Event ID | Method | When |
|----------|--------|------|
| 1 | `RowCache(kind)` | `IRowPresentationDiagnostics.Record` (`row-cache:*`, `row-enrichment:*`) |
| 2 | `PlanCache(kind)` | Launch plan cache hit/miss/build/evict (`LaunchDiagnosticKind`) |
| 3 | `StartupSpan(name, elapsedMs)` | `StartupPerformanceTrace.Measure` dispose (when ETW listeners or `QUICKSHELL_STARTUP_TRACE` are active) |
| 4 | `Repository(location, eventName, elapsedMs)` | `RepositoryDiagnostics.Report` (mutex/lock timeouts, slow ops) |
| 5 | `SupportEvent(eventCode)` | Successful support JSONL write |
| 6 | `SupportWriteFailure(exceptionType)` | Support log IO/JSON failure (host must not throw) |
| 7 | `GitDiscoveryComplete(repoCount)` | End of `GitRepoDiscovery.Discover` |

Payloads use short fixed strings (event codes, kind names, exception type names, numeric counts). Do **not** add full workspace paths, command lines, or environment variables.

### Capturing ETW

```powershell
dotnet-trace collect --providers QuickShell-Diagnostics --process-id <pid>
# or attach PerfView / Windows Performance Recorder to provider QuickShell-Diagnostics
```

## Support diagnostics

Host entry: `SupportDiagnostics.Default` (also registered into DI as `ISupportDiagnostics`). Core bridges repository timing through `RepositoryDiagnostics.Sink` (wired in `Program.cs`) and always mirrors those reports to ETW.

Production logs never use per-investigation `hypothesisId` / `#region agent log` markers.
