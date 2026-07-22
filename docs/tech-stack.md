# Project Context: QuickShell

> **Generated:** 2026-07-14 via `map-codebase` analysis
> **Repository:** [QuickShell](https://github.com/tonythethompson/QuickShell)
> **Version:** 0.2.0.0

---

## Stack

### Languages & Runtimes
| Layer | Technology | Version |
|-------|-----------|---------|
| Desktop (CmdPal extension) | **C# (.NET)** | `net10.0-windows10.0.26100.0` |
| Desktop (Run plugin) | **C# (.NET)** | `net10.0-windows10.0.26100.0` + WPF |
| Business logic library | **C# (.NET)** | `net10.0-windows7.0` + WinForms ref |
| CLI suggestion server | **C# (.NET)** | `net10.0-windows7.0` |
| Raycast extension | **TypeScript (React)** | Node.js >=22.14, tsc 5.8 |

### Key NuGet Dependencies
| Package | Scope | Purpose |
|---------|-------|---------|
| `Microsoft.CommandPalette.Extensions` | QuickShell | PowerToys CmdPal extension SDK (NuGet or local Toolkit) |
| `Microsoft.CommandPalette.Extensions.Toolkit` | QuickShell | CmdPal toolkit for building commands/pages |
| `Community.PowerToys.Run.Plugin.Dependencies` | QuickShell.Run | PowerToys Run plugin SDK (Wox.Plugin, Wox.Infrastructure) |
| `Microsoft.Extensions.DependencyInjection` | QuickShell.Core + Tests | DI container — used only for composition root |
| `Shmuelie.WinRTServer` | QuickShell | WinRT COM server hosting for CmdPal out-of-process extension |
| `xunit` / `xunit.runner.visualstudio` | Tests | Unit testing framework |
| `Microsoft.NET.Test.Sdk` | Tests | Test runner |

### Raycast npm Dependencies
| Package | Purpose |
|---------|---------|
| `@raycast/api` ^1.103.6 | Raycast extension API |
| `@raycast/utils` ^1.19.1 | Raycast utility hooks (usePromise, etc.) |
| `typescript` ^5.8.2 + `eslint` ^9.22.0 | Type-checking + linting |
| `prettier` ^3.5.3 | Code formatting |
| `vitest` (via config) | Test runner |

### Build & Packaging
- **MSIX packaging** with Windows App SDK — x64/ARM64 dual-arch bundles
- **PowerToys CmdPal** — COM out-of-process extension hosted via `Shmuelie.WinRTServer`
- **PowerToys Run** — WPF plugin loaded in-process by PowerToys
- **Raycast for Windows** — standalone extension published via `@raycast/api`
- **CI**: GitHub Actions (`windows-latest` for .NET, `ubuntu-latest` for Raycast)
- **Deploy**: `scripts/deploy.ps1` for local development loop; `release-extension.yml` / `release-run-plugin.yml` for packaging

---

## Architecture

### Project Structure
```
QuickShell.sln
├── QuickShell/                       # PowerToys CmdPal extension (WinUI, MSIX-packaged)
│   ├── Program.cs                    # COM server entry point [MTAThread]
│   ├── QuickShell.cs                 # IExtension — registers CommandsProvider
│   ├── QuickShellCommandsProvider.cs # CommandProvider + DI composition root
│   ├── Commands/                     # ICommandItem implementations
│   ├── Pages/                        # IPage / IContentPage / IFormPage
│   ├── Services/                     # UI-layer services (routing, display, settings)
│   │   └── CommandRouting/           # Command item handler pattern (Strategy)
│   └── Resources/                    # Localized strings (.resx)
│
├── QuickShell.Core/                  # Shared business logic library
│   ├── Abstractions/                 # Interface contracts
│   ├── Models/                       # Domain models (Workspace, TerminalShortcut, etc.)
│   ├── Services/                     # Service implementations
│   └── Composition/                  # DI registration extensions
│
├── QuickShell.Run/                   # PowerToys Run plugin (WPF)
│   └── Main.cs                       # IPlugin entry point
│
├── QuickShell.Suggest/               # CLI suggestion server
│   └── Program.cs                    # Console entry point
│
└── QuickShell.Core.Tests/            # xUnit tests for QuickShell.Core
```

### Data Flow
```
User types in CmdPal/Run/Raycast
    │
    ▼
Query → CommandProvider/Plugin.Query()
    │
    ▼
ShortcutRepository (singleton, in-memory + JSON on disk)
    │  ┌────────────────────────────────┐
    │  │  %LOCALAPPDATA%\QuickShell\    │
    │  │    shortcuts.json              │
    │  │    settings.json               │
    │  └────────────────────────────────┘
    │
    ▼
User selects a workspace → ShortcutLaunchExecutor.Launch()
    │
    ├─▶ TerminalLauncher.Resolve() → TerminalCatalog → LaunchTarget
    ├─▶ TerminalLauncher.Open() → Process.Start(wt.exe | powershell | cmd | wsl)
    └─▶ Dev server / companion app launch (browser open, editor open)
```

### Architectural Pattern
**Layered architecture** with clear separation:
1. **Presentation layer** (`QuickShell/`, `QuickShell.Run/`, `QuickShell.Raycast/`) — CmdPal pages, WPF panels, Raycast React components
2. **Business logic layer** (`QuickShell.Core/Services/`) — shortcut management, terminal resolution, launch execution
3. **Domain model layer** (`QuickShell.Core/Models/`) — `TerminalShortcut`, `Workspace`, `WorkspaceEntry`
4. **Abstraction layer** (`QuickShell.Core/Abstractions/`) — interface contracts for DI

The architecture is **interface-based (DIP)**: all core services are defined as interfaces in `Abstractions/` and registered via `Microsoft.Extensions.DI`. `InternalsVisibleTo` grants access to the CmdPal extension, Run plugin, Suggest CLI, and tests.

### Configuration & State Storage
- **`settings.json`** at `%LOCALAPPDATA%\QuickShell\` — extension-level preferences
- **`shortcuts.json`** at `%LOCALAPPDATA%\QuickShell\` — workspace shortcuts with layout metadata
- **Mutex-protected atomic writes** (`Global\QuickShell_shortcuts_json`) for cross-process safety
- **Debounced persistence** (2s timer) with undo/redo history (25 entries)
- **Legacy migration** — `WorkspaceLegacyMigration` auto-imports from old `workspaces.json` on first load

---

## Key Abstractions

| Interface | Implementation | Role |
|-----------|---------------|------|
| `ITerminalLauncher` | `TerminalLauncherService` + `TerminalLauncher` (static) | Resolves + launches terminal processes |
| `ITerminalProfileResolver` | `TerminalProfileResolverService` | Resolves terminal profiles from Windows Terminal settings |
| `IWorkspaceMapper` | `WorkspaceMapperService` | Clones/normalizes workspace domain objects |
| `IShortcutRepository` | `ShortcutRepository` | CRUD for shortcuts with JSON persistence |
| `IDraftStore` | `ShortcutDraftStore` | Draft/pending-edit state management |
| `IAtomicFileWriter` | `AtomicFileWriter` | Atomic JSON file writes (rename-based) |
| `ICommandIdParser` | `CommandIdParser` | Encodes/decodes command IDs for navigation |
| `IGitRepoIndex` | `GitRepoIndexService` | Git repo discovery + indexing |
| `IWorkspaceGitOperations` | `WorkspaceGitOperationsService` | Git status, branch switching, worktrees |
| `IWorkspaceHealthChecker` | `WorkspaceHealthCheckerService` | Validates workspace directory/launch integrity |
| `ICommandRouter` | `CommandRouter` | Routes command item IDs to handler strategy objects |

---

## Conventions (Observed)

### Error Handling
- **Early validation with exceptions**: `ShortcutValidation.TryNormalizeDirectory()` → throws `InvalidOperationException` / `DirectoryNotFoundException` on resolve
- **Graceful degradation**: Catch blocks at UI boundaries with user-facing messages
- **Result types** for import/export: `ShortcutTransferResult`, `ShortcutExportResult`, `ShortcutImportReadResult` with `Success` + `Message` fields
- **Agent debug logging**: `AgentDebugLog.Write()` / `.WriteException()` with hypothesis IDs scattered throughout — heavy instrumentation for AI-assisted debugging
- **No global error handler** beyond `AppDomain.CurrentDomain.UnhandledException` in Program.cs
- **Raycast**: Toast-based error feedback (`showToast`, `confirmAlert`)

### API / Surface
- **CmdPal extension** — `IExtension` COM server (WinRT out-of-process)
- **Run plugin** — `IPlugin`, `IContextMenu`, `ISettingProvider` (Wox/PowerToys interfaces)
- **Raycast** — React components with `@raycast/api` `Command` views
- **Suggest CLI** — stdout JSON via `Console.WriteLine`
- **camelCase** JSON serialization (System.Text.Json source-generated)

### Type System
- **Nullable enabled** throughout — `string?` for optional fields
- **Source-generated JSON serialization** — `QuickShellJsonContext` with `[JsonSerializable]` attributes — AOT-friendly
- **`record struct` / `record`** for value-like types: `ResolvedLaunch`, `TerminalLaunchAttempt`
- **`sealed`** on all internal service classes
- **Read-only vs clone pattern**: Repository exposes both `GetById()` (cloned) and `GetByIdReadOnly()` (direct reference)

### Testing
- **Framework**: xUnit with `Microsoft.Extensions.DependencyInjection`
- **Test strategy**: Unit tests for Core services; no UI or integration tests
- **Process isolation seam**: `TerminalLauncher.StartProcessOverride` static Func for capturing `ProcessStartInfo` without spawning
- **Parallelization guard**: `[CollectionDefinition(DisableParallelization = true)]` for tests mutating static overrides
- **Temp data**: `TempDataDirectory` helper for filesystem-dependent tests
- **Fakes**: `FakeShortcutRepository` and related test doubles
- **Test naming**: `MethodName_Scenario_ExpectedBehavior` convention (e.g., `Open_ThrowsBeforeLaunching_WhenDirectoryDoesNotExist`)
- **Raycast tests**: Vitest with 22+ test files covering storage, schema, validation, ranking, launch, migration, health

### Namespace & Assembly
- **Single-namespace `QuickShell`** for Core library (models, services, abstractions all under `QuickShell.*`)
- **Casing-sensitive paths** on Linux (relevant for cross-platform builds)
- **`EnableWindowsTargeting=true`** required for Linux restore/build of `-windows` TFMs

### Threading
- `SemaphoreSlim` for in-memory state synchronization in `ShortcutRepository`
- `Mutex` (`Global\QuickShell_shortcuts_json`) for cross-process file access
- `SynchronizationContext.Current` captured for COM-thread posting
- `ExtensionCallbackQueue` for marshaling callbacks to the extension UI thread

---

## Signals / Active Considerations

### Consistency Gaps
1. **Static vs DI service split in TerminalLauncher**: The `ITerminalLauncher` interface is DI-registered, but its implementation (`TerminalLauncherService`) is a thin proxy to the static `TerminalLauncher` class. The static carries the real logic and a mutable `StartProcessOverride` Func — a testability seam that leaks across collections.
2. **Mixed domain model naming**: The domain uses both `"shortcut"` and `"workspace"` terminology interchangeably (`TerminalShortcut` model, `WorkspaceEntry`, `ShortcutRepository` vs `WorkspaceFormCommands`). The legacy `Workspace` model and newer `TerminalShortcut` co-exist with a migration path.
3. **Two separate QuickShell codebases**: The C# desktop app and the TypeScript Raycast extension duplicate workspace concepts — schema, ranking, terminal resolution, launch logic — in different languages with no shared schema definition.
4. **InternalsVisibleTo proliferation**: 4 assemblies depend on `QuickShell.Core` internals (`QuickShell`, `QuickShell.Run`, `QuickShell.Core.Tests`, `QuickShell.Suggest`), creating high coupling to what should be a public API surface.

### Hotspots
1. **`ShortcutRepository`** (~2070 lines) — Handles CRUD, JSON serialization, concurrency (SemaphoreSlim + Mutex + debounced timer), import/export, search, legacy migration, undo/redo. Responsible for too many concerns — a prime refactoring candidate.
2. **`TerminalLauncher`** (~360 lines) — Static class with complex argument-building logic for 4 terminal types x 2 host modes x WSL paths. Central to correctness; testing depends on mutable static override.
3. **`Main.cs` (Run plugin)** (~350 lines) — Single file handling query, context menus, settings, launch, and clipboard operations.
4. **Launch argument building** is split between Core's `TerminalLauncher` and Raycast's `windows-launch.ts` — any argument-format change must be updated in both.

### Integration Points
1. **Windows Terminal settings parsing** — `TerminalSettingsDiscovery` / `WtProfilesService` reads Windows Terminal's `profiles.json` from `%LOCALAPPDATA%\Packages\...`
2. **WSL path resolution** — `WslPathResolver` converts Windows → WSL paths for cross-environment directory launches
3. **Git integration** — `GitRepoDiscovery`, `GitRepoIndex`, `WorktreeBranchTargetStore` for git-aware workspace features
4. **Dev server / companion app launch** — `WorkspaceDevServerAction`, `CompanionAppCatalog` for post-launch actions
5. **JetBrains/VS install discovery** — `JetBrainsInstallDiscovery`, `VisualStudioInstallDiscovery` for IDE companion launch
6. **Docker Compose discovery** — `DockerComposeDiscovery` for container-aware workspaces

### Architectural Strengths
- Clean layered separation with interface-based DI
- AOT-friendly with source-generated JSON serialization
- Atomic file writes with cross-process mutex protection
- Health check system (`ShortcutHealth`, `IWorkspaceHealthChecker`) catches broken workspace entries before launch
- Multi-format terminal launch (WT, PowerShell, Cmd, WSL, Intelligent Terminal, Nushell)
- Tab grouping via `; new-tab` for multi-launch workspaces
- Undo/redo history with 25-entry buffer
- Raycast parity: TypeScript port of the same workspace concepts with 22+ test files
