# Repository Guidelines

Guidance for coding agents working in QuickShell. Architecture tours live in `docs/architecture/`; numbered `0001`-`0005` files there are **proposals** that may lag landed code, so prefer the as-built tours when changing behavior and update the matching tour when you change a spine.

## Project Overview

Quick Shell is a **Windows-only .NET 10 keyboard-first workspace launcher**. A *workspace* is a saved folder plus metadata (terminal launches, companion app, git target, dev server). The primary surface is a PowerToys Command Palette extension (`QuickShell`), packaged out-of-process as a signed MSIX COM server. Two sibling hosts reuse the same on-disk model: a PowerToys Run plugin (`QuickShell.Run`, `qs` keyword) and a TypeScript Raycast extension (`QuickShell.Raycast`, parallel storage). `QuickShell.Core` owns all domain logic (no CmdPal SDK dependency) so the hosts are swappable UI shells. `QuickShell.Suggest` is a console CLI that emits JSON suggestion pills for Raycast.

The UI term is **workspace**; the on-disk file is still `%LOCALAPPDATA%\QuickShell\shortcuts.json`. Keep the product term and the storage term separate in code/comments.

## Architecture & Data Flow

**Hosting (out-of-process COM).** `QuickShell/Program.cs` is the entry point: `[MTAThread] Main` launches the COM server via `Shmuelie.WinRTServer.ComServer`, registering `QuickShellExtension` (`QuickShell/QuickShell.cs`, `[Guid]`, `: IExtension`) as `IExtension`. The process blocks on a `ManualResetEvent` until `IExtension.Dispose()` fires it. The `[Guid]` CLSID must match `Package.appxmanifest`; do **not** modify the COM-hosting pattern.

**CmdPal entry point.** `QuickShellCommandsProvider : CommandProvider, IDisposable` (`QuickShell/QuickShellCommandsProvider.cs`) is returned by `QuickShellExtension.GetProvider(ProviderType.Commands)`. Its constructor builds the DI container (`new ServiceCollection().AddQuickShellHost(...)`) and `BuildServiceProvider()`. It exposes `TopLevelCommands()` and `FallbackCommands()` and delegates `GetCommandItem(id)` to `ICommandRouter.TryHandle`.

**Pages-as-ICommands.** Pages (`QuickShell/Pages/*`, e.g. `QuickShellPage : DynamicListPage`) and Commands (`QuickShell/Commands/*`) are `ICommand` implementations. The same object can be a top-level command, a search result, or a `MoreCommands` item.

**Composition root / DI.** `QuickShell.Core/Composition/QuickShellServiceCollectionExtensions.cs` (`AddQuickShellCore(configDirectory?)`) registers all core services as interfaces. `QuickShell/Services/CommandRouting/QuickShellCommandRoutingServiceCollectionExtensions.cs` adds `AddQuickShellCommandRouting` and `AddQuickShellHost` (core + routing), the entry actually used by the provider. It prefers explicit factory lambdas over reflection for AOT/trim friendliness. Interfaces live in `QuickShell.Core/Abstractions/` and `Abstractions/Classification/`.

**Typed command routing.** `CommandRouter` (`CommandRouting/CommandRouter.cs`) uses `ICommandIdParser` to parse a deep-link string into a `CommandDescriptor` record (`Id, Kind, WorkspaceId, LaunchId, Directory, Branch`), then dispatches by `CommandKind` to a registered `ICommandItemHandler` (`CommandRouting/ICommandItemHandler.cs` + `CommandItemHandlers.cs`). Handlers receive a `CommandItemFactoryContext` (Shortcuts, Settings, CreateShortcut, ReloadPages) and return an `ICommandItem` (often a Page). To add a new deep link: add a `CommandKind` + an `ICommandItemHandler`.

**User command to launch flow.**
1. CmdPal invokes `GetCommandItem(id)` (deep link) or shows `TopLevelCommands()`.
2. `ICommandRouter` -> `ICommandIdParser.TryParse` -> `CommandDescriptor`.
3. Matching `ICommandItemHandler.Create` builds a `CommandItem`/`Page`.
4. On invoke, the page/command builds a `TerminalShortcut` (possibly via `WorkspaceSeedFactory`) and calls `ShortcutLaunchExecutor.Launch`.
5. `ShortcutLaunchExecutor` runs `WorkspaceHealthCheck` (Error blocks, Warning allows) -> `WorkspaceGitLaunchGate` (target branch from `worktree-branch-targets.json`) -> optional `CompanionAppLauncher` -> `TerminalLauncher.Resolve` -> `TerminalLauncher.Open` -> `Process.Start` (or test `StartProcessOverride`). Result is `ShortcutLaunchResult` (Dismiss/StayOpen + `LaunchDiagnosticsReport`). Quick Shell does **not** wait for command exit (handoff only).

**Workspace intelligence.** Folders are understood by `IProjectAnalysisService` (`Classification/ProjectAnalysisService.cs`) orchestrating `IEnumerable<IProjectClassifier>` (Node, DotNet, DockerCompose, TaskRunner, Rust, Python, Editor, Go, Java, Deno, Procfile, Ruby, Elixir) plus `IDevServerDetector`/`ICompanionAppDetector`. `WorkspaceSeedFactory` and `CommandSuggestionService` consume this to seed launches and suggestion pills.

**Multi-launch (tabs vs windows).** `ShortcutLaunchExecutor.LaunchAll` groups compatible entries via `GroupPlans` / `TerminalLauncher.OpenGroup` (`; new-tab`). `GroupPlans` key = `(tabHostExecutable, elevation)`; build `wt.exe <tab0> ; new-tab <tab1>` and **do not add `-w` on tab segments**. Console Host and mixed elevation fall back to separate windows. Controlled by `multiLaunchPresentation` (`singleWindowTabs` default | `separateWindows`) in `settings.json`.

**Persistence.** `ShortcutRepository` (`IShortcutRepository`) is the owner of `%LOCALAPPDATA%\QuickShell\shortcuts.json`. On-disk is a **layout** (Shortcuts + Separators) written as envelope `{"version":1,"entries":[...]}` (`PersistenceVersion.Current = 1`); v0 root array is still readable (dual-read). `AtomicFileWriter` writes `path.tmp` then `File.Replace(path.tmp, path, path.bak)`, guarded by a process-wide named `Mutex` `Global\QuickShell_shortcuts_json` plus a `SemaphoreSlim`; a `Timer` flushes pending writes. Undo/redo stacks are <=25. Debounced `MarkUsed` flushes `LastUsedUtc` after 2s. `WorkspacesChanged` event lets UI react without polling. **Never write `shortcuts.json` outside `ShortcutRepository`;** persist via `IAtomicFileWriter`.

## Key Directories

| Path | Purpose |
|------|---------|
| `QuickShell.Core/` | Domain: models, persistence, launch, health, git, terminals, classification, suggestions, companions. **No** CmdPal SDK dependency. `Services/`, `Models/`, `Composition/`, `Classification/`, `Abstractions/`. |
| `QuickShell/` | CmdPal extension: MSIX, Adaptive Card pages, command routing. `Pages/`, `Commands/`, `Services/CommandRouting/`, `Program.cs`, `QuickShell.cs`, `QuickShellCommandsProvider.cs`. |
| `QuickShell.Run/` | PowerToys Run plugin (`IPlugin`, `qs` keyword); consumes Core. |
| `QuickShell.Core.Tests/` | xUnit unit tests for Core (Windows-only TFM). |
| `QuickShell.Raycast/` | Separate npm/TS extension; **not** in the `.sln`; mirrors product rules, shells out to `QuickShell.Suggest`. |
| `QuickShell.Suggest/` | Console CLI emitting JSON suggestion pills for Raycast. |
| `scripts/` | `deploy.ps1`, `run-cmdpal-dev.ps1`, `deploy-all.ps1`/`ddeploy.ps1`, `generate-assets.ps1`, `RaycastLifecycle.ps1`, `build-exe.ps1`, `setup-template.iss`, `LogoAssetGenerator/`. |
| `docs/architecture/` | As-built tours (`overview`, `launch`, `persistence`, `cmdpal-surface`, `hosts`, `settings`, `forms`, `intelligence`, `companions`, `git-and-discover`) + ADRs `0001`-`0005`. |
| `.github/workflows/` | `ci.yml` (build/test), `release-extension.yml` (tag-triggered release + WinGet). |

## Development Commands

**Windows (primary, authoritative).** Platform flag is required on the CLI (`Directory.Build.props` sets `<Platforms>x64;ARM64</Platforms>` with no default); omitting `-p:Platform=x64` fails.

```powershell
# Build the whole solution (Release, x64).
dotnet build QuickShell.sln -c Release -p:Platform=x64

# Test only the Core test project (the only runnable test project).
dotnet test QuickShell.Core.Tests/QuickShell.Core.Tests.csproj -c Release -p:Platform=x64

# Default CmdPal dev loop: stop CmdPal -> regen assets -> build/sign/install MSIX -> restart.
.\scripts\deploy.ps1
#   -SkipElevation        trust cert in CurrentUser\TrustedPeople (no UAC)
#   -RecreateCertificate  force new dev signing cert
#   -UseLocalCmdPalSdk    build against a sibling PowerToys CmdPal SDK
#   -NoRestartCmdPal      build/install only

# Daily wrapper over deploy.ps1; prints the Reload steps. Most common command.
.\scripts\run-cmdpal-dev.ps1 -UseLocalSdk

# Deploy all three surfaces: CmdPal MSIX + Run plugin + Raycast.
.\scripts\deploy-all.ps1      # shorthand: .\scripts\ddeploy.ps1
#   -SkipCmdPal/-SkipRun/-SkipRaycast/-SkipTests/-NoRestart/-Configuration Release
```

In Visual Studio: **Build > Deploy** (not just Build), then run **Reload Command Palette Extension** in CmdPal. After any deploy: open CmdPal (`Win+Alt+Space`), run **Reload Command Palette Extension**, search **Quick Shell**.

**Raycast extension (Node).** `QuickShell.Raycast/` requires **Node.js >= 22.14.0** (`engines` in `package.json`, pinned by `.nvmrc`). It is **not** part of the .NET solution; CI runs it under the `raycast-check` job (Ubuntu).

```bash
cd QuickShell.Raycast
npm ci
npm test        # vitest run
npm run lint    # ray lint
npm run build   # ray build
npm run dev     # ray develop
```

`ray` (Raycast CLI) is a precondition: `scripts/verify-raycast-cli.js` runs as a pre-hook on `predev`/`prebuild`/`prelint` and fails clearly if `ray` is missing.

## Code Conventions & Common Patterns

- **Namespaces mirror folders.** `QuickShell`, `QuickShell.Services`, `QuickShell.Services.CommandRouting`, `QuickShell.Pages`, `QuickShell.Commands`, `QuickShell.Core`, `QuickShell.Core.Services|Models|Composition|Abstractions`, `QuickShell.Core.Classification[.Classifiers|.Detectors]`. All Core projects share `RootNamespace=QuickShell`. `Nullable` and `ImplicitUsings` are enabled project-wide.
- **One type per file**; most types are `internal` (small public surface). Stateless helpers are `internal static class` (`TerminalLauncher`, `WorkspaceSeedFactory`, `CommandSuggestionService`, `ShortcutLaunchExecutor`). Stateful singletons are `internal sealed class`.
- **Records vs classes.** Value/result DTOs are `readonly record struct` (`ResolvedLaunch`, `CommandDescriptor`, `ShortcutExportResult`); richer results are `record` (`TerminalLaunchAttempt`). Entities are mutable `class` (`TerminalShortcut`, `WorkspaceEntry`). `init`-only and `required` properties are used (`CommandItemFactoryContext`).
- **DI style.** `Microsoft.Extensions.DependencyInjection`; composition-root extension methods `AddQuickShellCore` / `AddQuickShellHost`. Most services `AddSingleton`; `IWorkspaceHealthChecker` and `IWorkspaceGitOperations` are `AddTransient`. The classifier registry uses `IEnumerable<IProjectClassifier>` (auto-injected, priority-ordered). Add new services via `AddQuickShellCore`; expose via an interface in `Abstractions/` or `QuickShell.Services`.
- **Static vs DI split.** Pure logic = `internal static` helper; swappable dependency = interface + DI registration. Prefer the established split.
- **Error handling.** Mixed. The launch path throws (`InvalidOperationException`, `DirectoryNotFoundException`, `Win32Exception`) caught in `LaunchSingle` -> `ShortcutLaunchResult.StayOpen`. Import/export/transfer use result types (`ShortcutTransferResult`, `ShortcutExportResult`, `ShortcutImportReadResult`) with `Success`/`Error`. No global `Result` monad.
- **async/await.** `*Async` methods take `CancellationToken cancellationToken = default`; sync wrappers call `.GetAwaiter().GetResult()`. Fire-and-forget `Task.Run` (git prewarm) is best-effort `try/catch`.
- **Dispose / cancellation.** `IDisposable` on provider, extension, repository, pages, `SearchDebouncer`. Extension shutdown via `ManualResetEvent`. `ShortcutRepository` owns a `Mutex` + `SemaphoreSlim` + persist `Timer`. No root `CancellationTokenSource` yet (ADR 0005, partial).
- **Instrumentation.** Pervasive `#region agent log` blocks calling `AgentDebugLog.Write/WriteException(... hypothesisId)` for traceability; harmless to behavior, leave them.
- **Localization.** User strings go through `QuickShell/Resources/Strings.cs`.
- **Command/pill model.** `CommandSuggestionService` produces `CommandSuggestionPill` objects (`TaskTypeCatalog` ids like `api`, `frontend`, `agent`). `QuickShell.Suggest` serializes these to JSON stdout for Raycast; `QUICKSHELL_SUGGEST_EXE` overrides the executable path in development.
- **Settings vs workspaces.** Preferences live in `%LOCALAPPDATA%\QuickShell\settings.json` (`QuickShellSettingsManager` / `QuickShellJsonSettingsStore` for CmdPal, `QuickShellSettingsReader` for Run), **not** in `shortcuts.json`. Keys: `terminalApplication` (`system`/`wt`/`it`/`conhost`), `defaultProfile`, `multiLaunchPresentation`, `blockDirtyBranchSwitch` (default true), `recentWorkspaceCount`. Launch always reads the live manager/reader values.

## Important Files

- **Entry / COM:** `QuickShell/Program.cs`, `QuickShell/QuickShell.cs`.
- **CmdPal provider:** `QuickShell/QuickShellCommandsProvider.cs`.
- **DI:** `QuickShell.Core/Composition/QuickShellServiceCollectionExtensions.cs`, `QuickShell/Services/CommandRouting/QuickShellCommandRoutingServiceCollectionExtensions.cs`.
- **Routing:** `QuickShell/Services/CommandRouting/{CommandRouter,CommandItemHandlers,ICommandItemHandler,CommandItemFactoryContext}.cs`; `QuickShell.Core/Services/{CommandDescriptor,CommandKind,CommandIdParser,CommandIdEncoding}.cs`.
- **State / persistence:** `QuickShell.Core/Services/{ShortcutRepository,IShortcutRepository,ShortcutDraftStore,AtomicFileWriter,PersistenceVersion}.cs`.
- **Launch:** `QuickShell.Core/Services/{TerminalLauncher,ShortcutLaunchExecutor,WorkspaceSeedFactory,WorkspaceHealthCheck,WorkspaceGitLaunchGate,CompanionAppLauncher}.cs`.
- **Intelligence:** `QuickShell.Core/Classification/{ProjectAnalysisService,ProjectLayoutAnalyzer,ProjectClassificationPipeline}.cs`, `QuickShell.Core/Classification/Classifiers/*`, `QuickShell.Core/Services/CommandSuggestionService.cs`.
- **UI:** `QuickShell/Pages/QuickShellPage.cs`, `QuickShell/Services/{ShortcutListItems,ShortcutTaskActionListItems}.cs`, `QuickShell/Commands/*`.
- **Config:** `QuickShell/QuickShellSettingsManager.cs`, `QuickShell/Services/QuickShellJsonSettingsStore.cs`.
- **Models:** `QuickShell.Core/Models/{TerminalShortcut,WorkspaceEntry,Workspace,ShortcutLayoutEntry}.cs`.
- **Build config (protected):** `Directory.Build.props` (pins `AppVersion`, `<Platforms>`, analyzers; a `PreToolUse` hook blocks edits), `Directory.Packages.props` (Central Package Management), `QuickShell/QuickShell.csproj` (MSIX identity `tonythethompson.536944BA0D095`, build variants Debug/Release/Store/WinGet), `QuickShell/setup-template.iss`, `QuickShell/build-exe.ps1`.

## Runtime / Tooling Preferences

- **.NET 10 SDK** (no `global.json`; SDK version implied). Target frameworks differ by project: the `QuickShell` CmdPal host targets `net10.0-windows10.0.26100.0`; `QuickShell.Core`, `QuickShell.Core.Tests`, and `QuickShell.Suggest` target `net10.0-windows7.0`. All are Windows-only (CsWinRT/CsWin32, Windows App SDK, WinUI, MSIX tooling).
- **`QuickShell.Core` has `<UseWindowsForms>true</UseWindowsForms>`** despite owning no CmdPal SDK dependency. It uses WinForms for clipboard/path pickers. So Core is only *compilable* off-Windows (via `-p:EnableWindowsTargeting=true`); it cannot *execute* on Linux. Keep Windows-only APIs in Core minimal so the swappable-host story holds.
- **Package manager:** NuGet with **Central Package Management** (`Directory.Packages.props`, `ManagePackageVersionsCentrally=true`). CmdPal SDK is `Microsoft.CommandPalette.Extensions` (NuGet) or a sibling local PowerToys SDK via `-p:UseLocalCmdPalSdk=true` (defines `CMDPAL_HOVER_ACTIONS`; don't assume those APIs exist otherwise).
- **No `.editorconfig` or `global.json`.** Analyzers are on: `EnableNETAnalyzers=true`, `AnalysisMode=Recommended`, plus StyleCop. Treat analyzer warnings seriously; they can break the Windows build.
- **Node >= 22.14** for the Raycast surface; `npm`/`ray` CLI for its build/lint/test.
- **PowerShell** drives build/deploy (`scripts/*.ps1`). Platform flag (`x64`/`ARM64`) is required on CLI `dotnet build/test`.
- **`Directory.Build.props` is protected** by a `PreToolUse` hook (`.claude/hooks/run-guard-directory-build-props.sh`); do not edit it unless explicitly asked.
- **Cross-platform (Linux cloud VM):** only `QuickShell.Core` (and `QuickShell.Suggest`) build with `-p:EnableWindowsTargeting=true`; `net10.0-windows*` assemblies cannot execute there (see the `UseWindowsForms` note above). The full solution does **not** build on Linux because `QuickShell.Core.Tests` references `QuickShell.csproj` (Win10.26100). Validate shared-logic changes by building Core alone on Linux; anything touching the extension, Run plugin, tests, or packaging must be verified on Windows.

## Testing & QA

- **Framework:** xUnit (`global using Xunit;` in `QuickShell.Core.Tests/GlobalUsings.cs`). Method names use underscores; `CA1707` is suppressed in the test csproj (intentional). **No Moq / FluentAssertions.**
- **Seams, not mocks.** Tests use real services plus process-wide static override seams: `LaunchExecutorTestEnvironment.Apply()/Reset()` (stubs terminal discovery + health), `FakeShortcutRepository` (in-memory `IShortcutRepository`), and `AgentCliCatalog.IsCommandOnPathOverride`. Shared seams are grouped with `[Collection]`.
- **InternalsVisibleTo:** `QuickShell.Core` exposes internals to `QuickShell`, `QuickShell.Run`, `QuickShell.Core.Tests`, and `QuickShell.Suggest` (see `QuickShell.Core.csproj`).
- **Raycast:** Vitest (`vitest run`) under `QuickShell.Raycast/src/__tests__/windows-launch.test.ts` (arg escaping, target resolution, `wt` launch plan), kept in parity with Core behavior.
- **What is covered:** `AgentCliSuggestionTests`, `TaskTypeCatalogTests`, `LaunchRowListEditorTests`, `TerminalProfileIconResolverTests`, `RunQueryScoringTests`, `ShortcutDisplayTests`, `ShortcutFormSaveRunEditorTests`, `ShortcutLaunchFormJsonTests`, `WorkspaceUtilityTests`, `ShortcutLaunchExecutorTests`, `TerminalLauncherArgsTests`.
- **CI gates:** `.github/workflows/ci.yml` -> `windows-latest` runs `dotnet test --no-build`; `ubuntu-latest` runs the `raycast-check` job (`npm test`/`lint`/`build`). `.github/workflows/release-extension.yml` (on `v*` tag or dispatch) builds EXE/Run/Raycast installers, creates a GitHub Release, and opens WinGet manifest PRs. **No coverage threshold**; CI gates on pass/fail only.

## Conventions & Gotchas

- **Always Deploy (not just Build)** to register the MSIX; after deploying, use the **Reload** command in Command Palette to refresh.
- **Don't modify `Program.cs` COM hosting or the `[Guid]` in `QuickShell.cs`** (must match `Package.appxmanifest`).
- **`.gitignore` note:** the Copilot instructions say to remove `**/Properties/launchSettings.json` and `*.pubxml` for git deployment, but those lines are **not present** in the current `.gitignore` (dev certs `QuickShell_Dev.cer`/`.pfx` and `dev-shortcuts.json` are intentionally ignored). Do not reintroduce them.
- **Raycast `ray` CLI is a precondition** for `build`/`lint`/`dev`; a missing binary fails fast via `verify-raycast-cli.js`.
- Honor existing rule files if present: `.github/copilot-instructions.md`, `.github/instructions/cmdpal-extension.instructions.md` (`**/*.cs`), `.claude/settings.json` + hooks, `.cursor/*`, plus Copilot skills under `.github/skills/` (`add-adaptive-card-form`, `add-extension-settings`, `add-dock-band`, `add-fallback-commands`, `publish-extension`).
