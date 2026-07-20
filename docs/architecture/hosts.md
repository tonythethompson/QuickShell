# Hosts: CmdPal, Run, and Raycast (as-built)

How the three launchers share concepts and where they diverge.

All hosts retain workspace IDs rather than trust-bearing snapshots and resolve current repository state immediately before external effects. CmdPal and Run use Core `IWorkspaceLaunchService`; Raycast applies the equivalent policy in `src/lib/security.ts`. See [trust-model.md](./trust-model.md).

## Matrix

| Concern | CmdPal (`QuickShell/`) | PowerToys Run (`QuickShell.Run/`) | Raycast (`QuickShell.Raycast/`) |
|---------|------------------------|-----------------------------------|----------------------------------|
| **Runtime** | CmdPal extension host, MSIX | Wox plugin in PowerToys | Raycast extension (Node) |
| **Business logic** | **QuickShell.Core** project ref | **QuickShell.Core** project ref | **TypeScript reimplementation** |
| **Workspace store** | `%LOCALAPPDATA%\QuickShell\shortcuts.json` | **Same file** | Raycast `STORAGE_KEY` blob |
| **Settings** | `settings.json` via settings manager | **Same JSON** via `QuickShellSettingsReader` | Stored in Raycast data + prefs |
| **Launch** | `ShortcutLaunchExecutor` | Same | `launch-executor.ts` + `windows-launch.ts` |
| **Pills** | Adaptive Card / form | `RunLaunchSuggestionPanel` | `QuickShell.Suggest.exe` |
| **Edit UI** | Adaptive Card forms | WPF `ShortcutWorkspaceEditorWindow` | React form components |
| **Action keyword** | Extension name + fallback | **`qs`** (+ global activation phrases) | Raycast commands |
| **Package** | Store / WinGet CmdPal packages | Bundled with full WinGet / GH setup | Raycast store / sideload |

Desktop CmdPal + Run **share** Core data files. Raycast is **parallel** unless the user imports or exports JSON.

## CmdPal

See [cmdpal-surface.md](./cmdpal-surface.md), [forms.md](./forms.md).

Strengths: full Adaptive Card UX, deep links, fallback, status pages, hover actions (local SDK).

## PowerToys Run

Entry: `QuickShell.Run/Main.cs` (`IPlugin`, context menu, settings provider, reloadable).

- Loads `ShortcutRepository` + `QuickShellSettingsReader`  
- Preloads shortcuts async  
- Query: action keyword `qs` and optional global activation via `RunGlobalQuery` (phrases like “quick shell”)  
- Scoring: `RunQueryScoring`  
- Launch: same `ShortcutLaunchExecutor`  
- Repair/missing folder UX via `ShortcutHealth`  
- Settings UI in WPF; editor window for create/edit  

Does **not** use CmdPal Adaptive Cards or `CommandRouter` deep links.

## Raycast

Structure:

```
src/
  open-workspace.tsx, create/edit, discover, settings
  lib/   storage, schema, launch-*, health, search, suggest-commands, …
  components/
```

Important libs:

| Lib | Desktop analogue |
|-----|------------------|
| `storage.ts` | `ShortcutRepository` (+ undo) |
| `schema.ts` / `migration.ts` | layout + version |
| `windows-launch.ts` + `launch-grouping.ts` | `TerminalLauncher` + grouping |
| `launch-executor.ts` | `ShortcutLaunchExecutor` |
| `post-launch-actions.ts` | companion + dev server after terminals |
| `workspace-health.ts` | subset of health |
| `suggest-commands.ts` | shells out to Core Suggest CLI |
| `settings.ts` | settings prefs |

Parity goals: multi-launch tabs (no `-w` on tab segments), similar settings keys, similar workspace shape. Gaps: health (lighter), companion presets/detection, git worktree targets file, full Adaptive Card forms.

## Shared Core (desktop only)

Anything that must stay consistent between CmdPal and Run belongs in **QuickShell.Core** (and tests). Raycast changes require **manual** TS + optional Suggest rebuild.

## Packaging notes

- Microsoft Store / CmdPal-only WinGet: extension without Run.  
- Bundled WinGet / GH setup: CmdPal + Run plugin (restart PowerToys for Run).  
- Raycast: separate release track (`scripts/build-raycast-extension.ps1`, etc.).

## Key files

| Host | Entry |
|------|--------|
| CmdPal | `QuickShell/QuickShellCommandsProvider.cs` |
| Run | `QuickShell.Run/Main.cs` |
| Raycast | `package.json` commands + `src/open-workspace.tsx` |
| Suggest | `QuickShell.Suggest/Program.cs` |

## Related

- [overview.md](./overview.md)  
- [launch.md](./launch.md)  
- [settings.md](./settings.md)  
- [post-launch.md](./post-launch.md)  
