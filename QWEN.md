# QWEN.md — Quick Shell

Context for AI agents working in this repository. `AGENTS.md` is the companion guide; where the two disagree, trust the code.

## Project Overview

**Quick Shell** is a Windows-only .NET desktop launcher. A *workspace* is a saved folder plus metadata: one or more terminal launches, optional commands to run on open (e.g. `dotnet run`, `npm run dev`), git branch targets, companion apps, and dev-server URLs. Users search and launch workspaces from three surfaces:

| Surface | Project | Notes |
| --- | --- | --- |
| PowerToys Command Palette extension | `QuickShell/` | Primary surface. Out-of-process MSIX COM server (`IExtension`). |
| PowerToys Run plugin | `QuickShell.Run/` | `qs` keyword; ships with WinGet/GitHub installs. |
| Raycast extension | `QuickShell.Raycast/` | Separate TypeScript project (Windows + macOS), **not** in the `.sln`. Parallel storage in Raycast LocalStorage. |

`QuickShell.Core` owns all domain logic (models, persistence, launch, health checks, git, terminal discovery, project classification, suggestions) with **no CmdPal SDK dependency**, so hosts are swappable UI shells. `QuickShell.Suggest` is a console CLI that emits JSON suggestion pills consumed by the Raycast extension.

- **UI term is "workspace"; the on-disk file is still `%LOCALAPPDATA%\QuickShell\shortcuts.json`.** Keep the product term and storage term separate in code/comments.
- Settings live in `%LOCALAPPDATA%\QuickShell\settings.json` (keys: `terminalApplication`, `defaultProfile`, `multiLaunchPresentation`, `blockDirtyBranchSwitch`, `recentWorkspaceCount`), **not** in `shortcuts.json`. Git branch targets live in `worktree-branch-targets.json`.

## Architecture & Data Flow

- **COM hosting:** `QuickShell/Program.cs` (`[MTAThread] Main`) starts `Shmuelie.WinRTServer.ComServer`, registering `QuickShellExtension` (`QuickShell/QuickShell.cs`, `[Guid]`, `: IExtension`). The process blocks on a `ManualResetEvent` until `IExtension.Dispose()` fires. The `[Guid]` CLSID must match `Package.appxmanifest`.
- **CmdPal entry point:** `QuickShellCommandsProvider : CommandProvider` builds the DI container (`AddQuickShellHost`) and exposes `TopLevelCommands()` / `FallbackCommands()`; `GetCommandItem(id)` delegates to `ICommandRouter.TryHandle`.
- **Pages-as-ICommands:** pages (`QuickShell/Pages/*`) and commands (`QuickShell/Commands/*`) are `ICommand` implementations that can be top-level commands, search results, or `MoreCommands` items.
- **Typed command routing:** `CommandRouter` uses `ICommandIdParser` to parse deep-link strings into `CommandDescriptor` records (`Id, Kind, WorkspaceId, LaunchId, Directory, Branch`), then dispatches by `CommandKind` to registered `ICommandItemHandler`s. To add a deep link: add a `CommandKind` + handler.
- **Launch flow:** page/command builds a `TerminalShortcut` (possibly via `WorkspaceSeedFactory`) → `ShortcutLaunchExecutor.Launch` → `WorkspaceHealthCheck` (Error blocks, Warning allows) → `WorkspaceGitLaunchGate` (target branch from `worktree-branch-targets.json`) → optional `CompanionAppLauncher` → `TerminalLauncher.Resolve` → `TerminalLauncher.Open` → `Process.Start`. Result is `ShortcutLaunchResult` (Dismiss/StayOpen + `LaunchDiagnosticsReport`). Quick Shell never waits for command exit (handoff only).
- **Multi-launch (tabs vs windows):** `ShortcutLaunchExecutor.LaunchAll` groups compatible entries via `GroupPlans` / `TerminalLauncher.OpenGroup` (`; new-tab`). Group key = `(tabHostExecutable, elevation)`; **do not add `-w` on tab segments**. Mixed elevation or Console Host falls back to separate windows. Controlled by `multiLaunchPresentation` (`singleWindowTabs` default | `separateWindows`).
- **Workspace intelligence:** `IProjectAnalysisService` orchestrates `IEnumerable<IProjectClassifier>` (Node, DotNet, DockerCompose, TaskRunner, Rust, Python, Editor, Go, Java, Deno, Procfile, Ruby, Elixir) plus dev-server/companion detectors. `WorkspaceSeedFactory` and `CommandSuggestionService` consume it for seeded launches and suggestion pills.
- **Persistence:** `ShortcutRepository` is the **sole owner** of `shortcuts.json`. On-disk format is a layout envelope `{"version":1,"entries":[...]}` (`PersistenceVersion.Current = 1`); the v0 root-array shape is still dual-read. Writes go through `AtomicFileWriter` (`path.tmp` → `File.Replace` with `.bak`), guarded by a named `Mutex` (`Global\QuickShell_shortcuts_json`) + `SemaphoreSlim` + a flush `Timer`. Undo/redo stacks capped at 25; `MarkUsed` is debounced 2 s. **Never write `shortcuts.json` outside `ShortcutRepository`; persist via `IAtomicFileWriter`.**

## Building, Testing, Deploying

**Windows is the only authoritative platform.** `Directory.Build.props` sets `<Platforms>x64;ARM64</Platforms>` with no default, so `-p:Platform=x64` (or ARM64) is **required** on every CLI `dotnet` invocation; omitting it fails.

```powershell
# Build the whole solution (Release, x64) — the exact CI command.
dotnet build QuickShell.sln -c Release -p:Platform=x64

# Test (QuickShell.Core.Tests is the only runnable test project).
dotnet test QuickShell.Core.Tests/QuickShell.Core.Tests.csproj -c Release -p:Platform=x64

# PowerShell script tests (Pester, run in CI).
Invoke-Pester -Path scripts/tests

# Default CmdPal dev loop: stop CmdPal → regen assets → build/sign/install MSIX → restart CmdPal.
.\scripts\deploy.ps1                 # -SkipElevation, -RecreateCertificate, -UseLocalCmdPalSdk, -NoRestartCmdPal
.\scripts\run-cmdpal-dev.ps1 -UseLocalSdk   # daily wrapper; prints Reload steps

# All three surfaces (CmdPal MSIX + Run plugin + Raycast).
.\scripts\deploy-all.ps1             # shorthand: .\scripts\ddeploy.ps1
```

After every CmdPal deploy: open CmdPal (`Win+Alt+Space`) → run **Reload Command Palette Extension** → search **Quick Shell**. In Visual Studio use **Build > Deploy** (not just Build).

**Raycast extension (Node):**

```bash
cd QuickShell.Raycast
npm ci
npm test        # vitest run (kept in parity with Core behavior)
npm run lint    # ray lint
npm run build   # ray build
npm run dev     # ray develop
```

The `ray` CLI is a precondition — `scripts/verify-raycast-cli.js` runs as a pre-hook on `dev`/`build`/`lint` and fails fast if it is missing. Node: `package.json` requires `>=20`; `.nvmrc` pins `22.22.2`.

**Cross-platform note (this checkout may live on WSL/Linux):** `Directory.Build.props` sets `EnableWindowsTargeting=true`, so `QuickShell.Core` (and `QuickShell.Suggest`) can *compile* off-Windows, but nothing can *execute* there (Windows-only TFMs, Win32/COM APIs). The **full solution does not build on Linux** because `QuickShell.Core.Tests` references `QuickShell.csproj` (targets `net10.0-windows10.0.26100.0`). Validate shared-logic changes by building `QuickShell.Core` alone; anything touching the extension, Run plugin, tests, or packaging must be verified on Windows.

**CI gates** (`.github/workflows/ci.yml`): `build-test` (build + `dotnet test --no-build` + Pester for `scripts/tests`), `raycast-check` on Windows and macOS (`npm test` / `lint` / `build`), plus an informational `perf-harness` (`Category=PerformanceMeasurement`, never gates PRs). Release workflows: `release-extension.yml` (tag-triggered GitHub Release + WinGet PRs), `release-run-plugin.yml`, `publish-store.yml`. Raycast ships via the Raycast Store only.

## Toolchain

- **.NET 10 SDK** — `mise.toml` pins `10.0.302`; no `global.json`. TFMs: `QuickShell` → `net10.0-windows10.0.26100.0`; `QuickShell.Core`, `QuickShell.Core.Tests`, `QuickShell.Suggest` → Windows TFMs (Core/Suggest `net10.0-windows7.0`, Tests `net10.0-windows10.0.26100.0` because it references `QuickShell`).
- **NuGet with Central Package Management** — versions only in `Directory.Packages.props` (`ManagePackageVersionsCentrally=true`); projects use versionless `PackageReference`. CmdPal SDK is `Microsoft.CommandPalette.Extensions` from NuGet; a sibling local PowerToys checkout can be used via `-p:UseLocalCmdPalSdk=true` (defines `CMDPAL_HOVER_ACTIONS` — don't assume those APIs exist otherwise).
- **Analyzers are on and strict:** `EnableNETAnalyzers=true`, `AnalysisMode=Recommended`, plus StyleCop. Treat analyzer warnings seriously; they can break the Windows build. No `.editorconfig` or `global.json`.
- **`Directory.Build.props` is protected** by a PreToolUse hook (`.claude/hooks/run-guard-directory-build-props.sh`) and pins `AppVersion` (currently `0.2.4.0`). Do not edit it unless explicitly asked.
- **PowerShell** drives all build/deploy/release (`scripts/*.ps1`).

## Code Conventions

- **Namespaces mirror folders**; all Core projects share `RootNamespace=QuickShell`. `QuickShell`, `QuickShell.Services`, `QuickShell.Services.CommandRouting`, `QuickShell.Pages`, `QuickShell.Commands`, `QuickShell.Core`, `QuickShell.Core.Services|Models|Composition|Abstractions`, `QuickShell.Core.Classification[.Classifiers|.Detectors]`.
- **One type per file; most types are `internal`** (small public surface). Stateless helpers are `internal static class` (`TerminalLauncher`, `WorkspaceSeedFactory`, `CommandSuggestionService`, `ShortcutLaunchExecutor`); stateful singletons are `internal sealed class`. Pure logic = static helper; swappable dependency = interface + DI. Follow the established split.
- **Records vs classes:** value/result DTOs are `readonly record struct` (`ResolvedLaunch`, `CommandDescriptor`, `ShortcutExportResult`); richer results are `record` (`TerminalLaunchAttempt`); entities are mutable `class` (`TerminalShortcut`, `WorkspaceEntry`). `init`-only and `required` properties are used.
- **DI:** `Microsoft.Extensions.DependencyInjection`; composition roots are `AddQuickShellCore` (`QuickShell.Core/Composition/`) and `AddQuickShellHost` (`QuickShell/Services/CommandRouting/`). Most services are singletons; `IWorkspaceHealthChecker` and `IWorkspaceGitOperations` are transient. Classifiers register via `IEnumerable<IProjectClassifier>` (auto-injected, priority-ordered). Prefer explicit factory lambdas over reflection (AOT/trim friendliness). New services: register in `AddQuickShellCore`, expose via an interface in `Abstractions/` or `QuickShell.Services`.
- **Error handling is mixed by design:** the launch path throws (`InvalidOperationException`, `DirectoryNotFoundException`, `Win32Exception`) and is caught in `LaunchSingle` → `ShortcutLaunchResult.StayOpen`; import/export/transfer use result types with `Success`/`Error`. There is no global `Result` monad.
- **async/await:** `*Async` methods take `CancellationToken cancellationToken = default`; sync wrappers use `.GetAwaiter().GetResult()`. Fire-and-forget `Task.Run` (e.g. git prewarm) is best-effort `try/catch`.
- **Dispose / cancellation:** `IDisposable` on provider, extension, repository, pages, `SearchDebouncer`. `ShortcutRepository` owns a `Mutex` + `SemaphoreSlim` + persist `Timer`. No root `CancellationTokenSource` yet (ADR 0005, partial).
- **Instrumentation:** pervasive `#region agent log` blocks calling `AgentDebugLog.Write/WriteException(... hypothesisId)`. They are harmless to behavior — leave them in place when editing nearby code.
- **User-facing strings** go through `QuickShell/Resources/Strings.cs`.
- **Command/pill model:** `CommandSuggestionService` produces `CommandSuggestionPill`s (`TaskTypeCatalog` ids like `api`, `frontend`, `agent`); `QuickShell.Suggest` serializes them to JSON stdout for Raycast (`QUICKSHELL_SUGGEST_EXE` overrides the executable path in development).

## Testing Conventions

- **xUnit** (`global using Xunit;` in `QuickShell.Core.Tests/GlobalUsings.cs`). Method names use underscores (`CA1707` suppressed intentionally in the test csproj). **No Moq, no FluentAssertions.**
- **Seams, not mocks:** tests use real services plus process-wide static override seams — `LaunchExecutorTestEnvironment.Apply()/Reset()` (stubs terminal discovery + health), `FakeShortcutRepository` (in-memory `IShortcutRepository`), `AgentCliCatalog.IsCommandOnPathOverride`. Shared seams are grouped with `[Collection]`.
- `QuickShell.Core` exposes internals to `QuickShell`, `QuickShell.Run`, `QuickShell.Core.Tests`, and `QuickShell.Suggest` via `InternalsVisibleTo` (see `QuickShell.Core.csproj`).
- Raycast behavior is covered by Vitest under `QuickShell.Raycast/src/__tests__/` (arg escaping, target resolution, `wt` launch plan), kept in parity with Core.
- No coverage threshold; CI gates on pass/fail only.

## Key Directories

| Path | Purpose |
| --- | --- |
| `QuickShell.Core/` | Domain: models, persistence, launch, health, git, terminals, classification, suggestions, companions. No CmdPal SDK dependency. |
| `QuickShell/` | CmdPal extension: MSIX, Adaptive Card pages, command routing. `Program.cs`, `QuickShell.cs`, `QuickShellCommandsProvider.cs`, `Pages/`, `Commands/`, `Services/CommandRouting/`. |
| `QuickShell.Run/` | PowerToys Run plugin (`IPlugin`, `qs` keyword); consumes Core; ships its own WPF settings/editor. |
| `QuickShell.Core.Tests/` | xUnit tests for Core (references both Core and the CmdPal host). |
| `QuickShell.Suggest/` | Console CLI emitting JSON suggestion pills for Raycast. |
| `QuickShell.Raycast/` | Separate npm/TS Raycast extension; not in the `.sln`; shells out to `QuickShell.Suggest`. |
| `scripts/` | Deploy/build/release PowerShell (`deploy.ps1`, `ddeploy.ps1`/`deploy-all.ps1`, `run-cmdpal-dev.ps1`, `generate-assets.ps1`, …) + Pester tests in `scripts/tests`. |
| `docs/architecture/` | As-built tours (`overview`, `launch`, `persistence`, `cmdpal-surface`, `hosts`, `settings`, `forms`, `intelligence`, `companions`, `git-and-discover`) + ADRs `0001`–`0005`. Tours may lag; update the matching tour when you change a spine. |
| `shared/` | Cross-surface data (e.g. `workspace-trust-features.json`, embedded into Core and synced into Raycast via a pre-hook). |
| `.github/workflows/` | `ci.yml`, release workflows, CodeQL, Pages. |

## Hard Rules / Gotchas

- **Do not modify** `Program.cs` COM hosting or the `[Guid]` in `QuickShell.cs` (must match `Package.appxmanifest`). MSIX identity is `tonythethompson.536944BA0D095` (see `QuickShell/QuickShell.csproj`).
- **Always Deploy (not just Build)** to register the MSIX, then run **Reload Command Palette Extension** in CmdPal.
- **Never write `shortcuts.json` outside `ShortcutRepository`**; use `IAtomicFileWriter` for any other persistence.
- **`Directory.Build.props` is hook-protected** — edits are blocked unless explicitly requested.
- **WinGet dev conflict:** a WinGet-installed CmdPal-only EXE can shadow the dev MSIX; uninstall `tonythethompson.QuickShellforCmdPal` before using `deploy.ps1` locally (the script warns about `%LOCALAPPDATA%\Programs\QuickShell\QuickShell.exe`).
- **`.gitignore`:** dev certs (`QuickShell_Dev.cer`/`.pfx`) and `dev-shortcuts.json` are intentionally ignored. Do not reintroduce `**/Properties/launchSettings.json` or `*.pubxml` exclusions.
- Additional rule files to honor when present: `.github/copilot-instructions.md`, `.github/instructions/cmdpal-extension.instructions.md` (`**/*.cs`), `.claude/settings.json` + hooks, `.cursor/*`, and Copilot skills under `.github/skills/` (`add-adaptive-card-form`, `add-extension-settings`, `add-dock-band`, `add-fallback-commands`, `publish-extension`).
