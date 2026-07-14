# Overview (as-built)

## Solution projects

| Project | Role |
|---------|------|
| **QuickShell.Core** | Domain models, persistence, launch, health, git, terminals, classification, suggestions, companion apps. **No** CmdPal SDK dependency. |
| **QuickShell** | PowerToys Command Palette extension (MSIX, Adaptive Card pages, command routing). |
| **QuickShell.Run** | PowerToys Run plugin (`IPlugin`, action keyword `qs`). |
| **QuickShell.Core.Tests** | Unit tests for Core (and some host-adjacent behavior). |
| **QuickShell.Suggest** | Small console CLI: JSON suggestion pills for Raycast. |
| **QuickShell.Raycast/** | Separate npm/TS extension (not in `.sln`). Concept parity with Core; launch/storage reimplemented. |

Stack: **.NET 10**, Windows-only TFMs, WinUI / Windows App SDK / CsWinRT for CmdPal packaging. Version pin: `Directory.Build.props` (`AppVersion` — do not bump unless asked).

## Layers

```
┌─────────────────────────────────────────────────────────────┐
│ Hosts: CmdPal pages/commands │ Run plugin │ Raycast UI      │
└───────────────────────┬───────────────────┬─────────────────┘
                        │                   │
                        ▼                   ▼
              DI / services facade    TS storage + windows-launch
                        │                   │
                        ▼                   ▼
                   QuickShell.Core              QuickShell.Suggest
              models · repo · launch · health · classify · companions
```

- **Core** owns “what is a workspace and how do we open it safely.”
- **CmdPal / Run** are UI shells over Core.
- **Raycast** does not load Core; it mirrors product rules and shells out for pills.

## Composition (Core)

`QuickShell.Core/Composition/QuickShellServiceCollectionExtensions.cs` registers:

- `IAtomicFileWriter`, `IShortcutRepository`, `IDraftStore`, `ICommandIdParser`
- Terminal launcher / profile resolver / workspace mapper
- Git index / git operations / health checker (some as services; many helpers remain **static**)

CmdPal builds a host service provider in `QuickShellCommandsProvider` and binds `QuickShellServices`. Run often constructs `ShortcutRepository` directly and uses settings readers.

## Domain vocabulary

| Term | Meaning |
|------|---------|
| **Workspace / TerminalShortcut** | Saved folder + metadata (name, pins, launches, companion, dev server, …) |
| **Launch / WorkspaceEntry** | One terminal row: label, terminal/profile, command, admin, task type |
| **Layout** | Ordered list of shortcuts **and** section separators on disk |
| **Default terminal** | Global host (`wt` / `it` / `conhost` / system) + default profile |
| **Companion app** | Optional GUI app open for the workspace folder (not a terminal command) |
| **Suggestion pill** | Project-aware command chip that fills a launch row |

## Data on disk

Default directory: `%LOCALAPPDATA%\QuickShell\`

| File | Owner |
|------|--------|
| `shortcuts.json` | `ShortcutRepository` (versioned layout envelope) |
| `settings.json` | Host settings (CmdPal / Run / Raycast prefs — not Core repo) |
| `shortcut-edit-draft.json` | `ShortcutDraftStore` (in-progress **edit**) |
| `worktree-branch-targets.json` | Git target branch per worktree |

## Where to change what

| Goal | Start here |
|------|------------|
| Save / list / undo workspaces | [persistence.md](./persistence.md) |
| Open terminals / tabs / elevation | [launch.md](./launch.md) |
| Create/edit UX, Adaptive Cards | [forms.md](./forms.md) |
| Suggested commands / project type | [intelligence.md](./intelligence.md) |
| Open VS Code / Cursor / VS | [companions.md](./companions.md) |
| Global terminal / multi-launch / git gate prefs | [settings.md](./settings.md) |
| Home list, deep links, root fallback | [cmdpal-surface.md](./cmdpal-surface.md) |
| Discover repos / worktree branches | [git-and-discover.md](./git-and-discover.md) |
| Run or Raycast host differences | [hosts.md](./hosts.md) |
| Dev-server URL / post-open links | [post-launch.md](./post-launch.md) |
| Raycast implementation detail | `QuickShell.Raycast/src/lib/*` + Suggest CLI |

## Multi-command presentation

Setting key: `multiLaunchPresentation` — `singleWindowTabs` (default) or `separateWindows`.

- Desktop: `ShortcutLaunchExecutor` + `TerminalLauncher.OpenGroup` (`; new-tab`).
- Raycast: `launch-grouping.ts` + `windows-launch.ts` (**do not** pass `-w` on tab segments).
- Tabs require Windows Terminal / Intelligent Terminal as global host; Console Host and mixed elevation fall back to separate windows.

See [launch.md](./launch.md) and root `AGENTS.md`.
