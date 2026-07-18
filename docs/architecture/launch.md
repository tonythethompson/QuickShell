# Launch pipeline (as-built)

End-to-end path from “open workspace” to `Process.Start`, including resolve, health, git gate, multi-launch grouping, and command argv.

Before this pipeline, `IWorkspaceLaunchService` resolves the current workspace by ID and applies the action-specific trust policy. A cached CmdPal/Run/Raycast item is not proof of trust; revocation therefore fails closed on the next invocation. See [trust-model.md](./trust-model.md).

## Host entry points

| Host | Full workspace | Single launch row |
|------|----------------|-------------------|
| **CmdPal** | `OpenTerminalShortcutCommand` → `ShortcutLaunchExecutor.Launch` | `OpenShortcutLaunchCommand` → `LaunchEntry` |
| **Run** | `Main.Launch` → `Launch` | context variants |
| **Raycast** | `executeWorkspace` → plan → `executeWorkspaceLaunch` | filter plan entries |

Options injected by UI: `RunAsAdmin`, `RunAsStandard`, `BlockDirtyBranchSwitch`, `SeparateWindowsForMultiLaunch`, companion/dev-server include flags. Per-row `WorkspaceEntry.RunAsAdmin` is set from the CmdPal Commands **Admin** checkbox (and Run’s per-row elevate toggle). Mixed elevation cannot share WT tabs (see grouping below).

## Call graph (Core)

```
ShortcutLaunchExecutor.Launch / LaunchEntry
  1. Resolve shortcut from repository and get repository version
  2. Build or reuse resolved launch plan from `WorkspaceLaunchPlanCache`
  3. WorkspaceHealthCheck.Check / CheckEntry     ← blocking Errors stop (always reevaluated)
  4. Directory exists                            ← always reevaluated
  5. WorkspaceGitLaunchGate                      ← target branch / dirty (always evaluated)
  6. Execute plan: CompanionAppLauncher (full workspace only) ← soft fail
  7. Execute plan: Single row → TerminalLauncher.OpenResolved
     Multi     → OpenGroup (grouped by tab host + elevation)
  8. BuildPostLaunchResult (dev server URL, warnings, dismiss/stay)
```

Primary types:

- `QuickShell.Core/Services/ShortcutLaunchExecutor.cs`
- `TerminalLauncher.cs` / `TerminalLauncherArgs.cs`
- `TerminalCatalog.cs` / `TerminalHostIds.cs`
- `WorkspaceHealthCheck.cs` / `WorkspaceGitLaunchGate.cs`
- `WorkspaceLaunchPlanCache.cs` / `LaunchPlanCacheKey.cs`
- `ResolvedWorkspaceLaunchPlan.cs` / `ResolvedLaunchGroup.cs` / `ResolvedLaunchPlanEntry.cs`

## Preflight

### Health (`WorkspaceHealthCheck`)

| Severity | Launch effect | Examples |
|----------|---------------|----------|
| **Error** | Block open | Missing folder, no launches, missing terminal/profile/WSL/exe |
| **Warning** | Allow; may StayOpen after | Port in use, existing process (“running”) |
| **Info** | No block | Git branch text |

Volatile checks (ports/processes) can be toggled off via flags. List UI uses cheaper `ShortcutHealth` + cached `WorkspaceStatusService` (attention badges); full check runs on open.

Full `Check` and `CheckEntry` both resolve every enabled `same-as-previous` row before validation. Resolution walks prior enabled rows; an all-inherited chain falls back to the configured default target without mutating persisted launch rows.

### Git gate (`WorkspaceGitLaunchGate`)

After health: if a **target branch** is stored for the directory and HEAD differs, try switch. Dirty tree + `blockDirtyBranchSwitch` → StayOpen (not a health Error). Targets live in `worktree-branch-targets.json`.

## Terminal resolution (`TerminalCatalog`)

Storage uses `Terminal` + `WtProfile` per row. Forms use compact ids (`default`, `wt:Name`, `same-as-previous`, `pwsh`, …).

```
EncodeLaunchTargetId(Terminal, WtProfile)
  default            → ResolveDefaultTarget(global app, default profile)
  wt / it (+profile) → ResolveProfileTarget (host from global app)
  powershell/pwsh/cmd/wsl → catalog Resolve
  same-as-previous   → resolved BEFORE ResolveForShortcut via ResolveLaunchEntry
```

`ToLaunchShortcut` expands same-as-previous by walking prior enabled rows; entire chain of same-as-previous falls back to **default**.

Global host (`TerminalHostIds`): `system` | `wt` | `it` | `conhost`.

## Multi-launch grouping

`LaunchAll`:

1. Resolve each enabled row → `EntryPlan` + effective elevation.
2. If `SeparateWindowsForMultiLaunch` → one process per row.
3. Else `GroupPlans` key = **`(tabHostExecutable, elevation)`**.

Who can share tabs:

| Target | Tab host |
|--------|----------|
| WT / IT | That host’s `wt.exe` / `wtai.exe` |
| PS / pwsh / cmd / WSL | Global WT-family host only (coax into tabs) |
| Global **conhost** | No tab host → separate windows |

`TerminalLauncher.OpenGroup` builds:

```text
wt.exe  <tab0>  ; new-tab  <tab1>  ; new-tab  <tab2>
```

**Do not add `-w` on tab segments** (extra windows). Elevation cannot mix in one process.

Raycast mirrors grouping in `launch-grouping.ts` / `windows-launch.ts`.

## Launch plan cache

Deterministic plan preparation is cached so repeated launches of the same workspace avoid re-resolving terminals, profiles, and tab groups. Volatile checks (health, directory existence, git gate, companion availability, process start) are always reevaluated against the cached plan.

Cache key (`LaunchPlanCacheKey`) includes:

- Workspace id
- Repository version
- Effective terminal application id
- Default profile id
- `SeparateWindowsForMultiLaunch`
- `RunAsAdmin` / `RunAsStandard`
- Launch entry id (for `LaunchEntry`)
- Terminal catalog fingerprint (`TerminalCatalog.GetFingerprint`)

Invalidation is explicit: shortcut edits bump the repository version; terminal application / default profile / run-as / tab-mode settings change the settings fingerprint; terminal catalog changes (installed terminals, WT profiles, WSL distros) change the catalog fingerprint. The cache is bounded with LRU eviction and single-flighted so concurrent requests for the same key share one build.

Instrumentation diagnostics: `PlanCacheHit`, `PlanCacheMiss`, `PlanCacheBuild`, `PlanCacheEvicted`.

## Command argv (`TerminalLauncherArgs`)

| Mode | Who owns `cd` |
|------|----------------|
| Direct PS/cmd/wsl | Shell (`Set-Location`, `cd /d`, `wsl --cd`) |
| WT host | Usually `wt -d`, then command **suffix** |

Suffix policy for WT (profile `commandline` inspection):

- PowerShell/pwsh profile → `*sh.exe -NoExit -Command "…"`
- Nushell → `nu -c '…'`
- WSL profile/path → `wsl.exe …`
- Else (incl. many package-manager commands) → `cmd.exe /k "…"`

When `-d` already set, suffix uses `omitDirectoryChange: true`.

Quick Shell does **not** wait for command exit codes (handoff only).

## Post-launch

- Optional **dev server URL** open (soft).
- Companion soft failure folded into StayOpen message.
- Clean success → dismiss palette; partial multi-launch → StayOpen with counts.
- Detailed diagnostics → `LaunchDiagnosticsReport` / CmdPal “Copy launch diagnostics”.
- Support diagnostics → redacted, bounded JSONL under `%LOCALAPPDATA%\QuickShell\logs`; “Copy support bundle” contains app/OS metadata and aggregate diagnostic counts only. It never includes workspace names, commands, paths, titles, details, exception messages, arbitrary host data, or the user-specific local log folder path.

## Mental model: when do you get tabs?

```
separateWindows? → N windows
else global host is conhost? → N windows
else partition by (tabHost, elevation)
  size > 1 → one wt.exe with "; new-tab"
  size == 1 → OpenResolved
```

## Tests

- `ShortcutLaunchExecutorTests`, `ShortcutLaunchExecutorCacheTests`, `WorkspaceLaunchPlanCacheTests`, `LaunchPlanCacheMeasurementsTests`, `TerminalLauncher*Tests`, `TerminalLauncherArgsTests`
- Raycast: `launch-grouping.test.ts`, `windows-launch.test.ts`

## Related

- [companions.md](./companions.md) — GUI side-open during full launch
- [intelligence.md](./intelligence.md) — where launch **commands** often come from
- [forms.md](./forms.md) — editing launch rows
