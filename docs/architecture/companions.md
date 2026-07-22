# Companion apps (as-built)

Optional **GUI** applications opened for a workspace folder (editors, IDEs, git clients, Explorer). Separate from terminal multi-launch and from agent CLIs (`claude`, etc.).

## Mental model

```
Persisted on TerminalShortcut
  CompanionApps[]            // source of truth (max 5)
    Id, Path, Arguments, OpenOnLaunch, Order
  // Legacy scalars (mirrored from primary = first by Order)
  OpenCompanionAppOnLaunch
  CompanionAppPath
  CompanionAppArguments

Form (multi-row)
  Each row: CompanionAppPreset_i + Browse + (last row) "+" + (if >1) "−"
  "+" tooltip: "Add another companion app" (inline with picker)
  Cap 5; save writes full CompanionApps list

Detection (create)
  folder signals → installed preset (if exe found) → primary only

Launch
  Auto: all entries with OpenOnLaunch
  On-demand: all configured entries
  Process.Start(exe, expanded args, WorkingDirectory = workspace)
```

Not part of tab grouping. Multiple companion processes may start on full workspace open (or on-demand). Soft-fail per app on auto launch (terminals still start).

## Schema dual-read / dual-write

| On disk | Behavior |
|---------|----------|
| Only legacy scalars | `EnsureCompanionsFromLegacy` builds a one-entry list |
| `CompanionApps` present | Normalize order, drop empty paths, mirror primary → scalars |
| Save from single-row form | Updates primary; **preserves** companions after index 0 |

Cap: `CompanionAppNormalization.MaxCompanionCount` = **5**.

Same pattern as launch rows (`Launches` + legacy `Command`/`Terminal`).

## Catalog (`CompanionAppCatalog`)

Presets (installed-only in form dropdown) include among others:

| Preset | Default args (typical) |
|--------|-------------------------|
| Explorer, Fork, GitHub Desktop, Rider, IntelliJ, Obsidian, Azure Data Studio | `{folder}` |
| VS Code, Cursor, TRAE, Zed, Sublime, Neovide, GVim | `.` |
| Visual Studio 2022 / 2026 | `{solution}` |
| Notepad++ | `{folder}` |
| Custom | user path |

Resolution: candidate paths, `VisualStudioInstallDiscovery`, `JetBrainsInstallDiscovery` (Rider / IDEA). Infer preset from executable filename / devenv path.

Form helpers: `ReconcileStoredShortcut`, `ReconcileForForm`, `ReconcileForSave`, `GetInstalledFormChoices`, browse → `ResolvePresetAfterBrowse`.

Disk stores **path** (and flag/args) per entry. Forms edit the full list (CmdPal Adaptive Card + Run WPF).

## Detection (`CompanionAppDetection`)

Used to **seed** a primary companion on **Discover create** (`WorkspaceSeedFactory.ApplyCompanionHints`). Plain add/edit Browse–Paste does **not** auto-fill companion presets (user picks).

First match, only if executable resolves:

| Priority (approx.) | Signal | Preset |
|--------------------|--------|--------|
| High | `.cursor/` | Cursor |
| | `.trae/` | TRAE |
| | `.vscode/` | VS Code |
| | `.obsidian/` | Obsidian |
| | Zed project | Zed |
| | JetBrains + .NET | Rider |
| | VS solution / `.vs` | VS 2026 then 2022 |
| | `.idea/` | IntelliJ |
| | Sublime project | Sublime |
| Low | `.git/` | Fork, else GitHub Desktop |

Signals: `WorkspaceCompanionSignals`. Suggestion sets `EnableOnLaunch = true`.

Raycast form supports multi companion rows, an installed-preset picker (`companion-catalog.ts`), and light folder-marker seeding (`.cursor` / `.vscode` / `.trae` / `.obsidian` / `.git` → installed presets). Full vswhere/JetBrains Toolbox detection remains desktop-only.

## Launch (`CompanionAppLauncher`)

| Mode | When |
|------|------|
| Auto (`onDemand: false`) | Full `ShortcutLaunchExecutor.Launch` — all `OpenOnLaunch` entries |
| On demand (`onDemand: true`) | ⋯ Open companions — **all** configured entries |

Single-row `LaunchEntry` sets **IncludeCompanionApp: false**.

Steps per entry: resolve exe → directory exists → expand args → `Process.Start` (`UseShellExecute`). Auto failure is **soft** (terminals still start; warning in post-launch). On-demand surfaces StayOpen errors (combined if multiple fail).

### Argument expansion

| Template | Result |
|----------|--------|
| empty | no args |
| `.` | workspace path (quoted if spaces) |
| `{folder}` | workspace path |
| `{solution}` | top-level `*.sln` or folder fallback |

Working directory = workspace folder.

## Form validation (`CompanionAppArgumentValidation`)

- Args field visibility depends on preset/path (primary).  
- Save: max length, no newlines, only `{folder}` / `{solution}` brace placeholders.  
- Soft warnings for mismatched tokens (e.g. VS without `{solution}`).  
- `ShortcutValidation.TryValidateCompanionApp` validates **every** list entry.

## List / context

- `ShortcutHealth`: missing companion path subtitle when any open-on-launch entry is missing.  
- Context menu: open companions when any path configured (summary label for multi).  
- Full `WorkspaceHealthCheck` does not dedicate a companion finding kind.

## Agent CLIs vs companions

| | Companion | Terminal agent CLI |
|--|-----------|-------------------|
| Process | GUI `.exe` | Command in terminal |
| Config | Path + args + per-entry open-on-launch | Launch row command |
| Detection | Folder → IDE | PATH (if added later as pills) |

Keep agent CLIs on launch rows / [intelligence.md](./intelligence.md) pills — not this catalog.

## Key files

| File | Role |
|------|------|
| `CompanionAppEntry.cs` | List entry model |
| `CompanionAppNormalization.cs` | Dual-read, primary mirror, cap |
| `CompanionAppCatalog.cs` | Presets, paths, form reconcile |
| `CompanionAppDetection.cs` | Folder suggest |
| `WorkspaceCompanionSignals.cs` | Markers |
| `CompanionAppLauncher.cs` | Expand + multi start |
| `CompanionAppArgumentValidation.cs` | Form rules |
| `OpenCompanionAppCommand` / context menu | On-demand (all) |
| `ShortcutFormPage` / Run editor | Primary form; preserve extras on save |

## Tests

`CompanionAppLauncherTests` (expansion, multi auto/on-demand with `StartProcessOverride`), `CompanionAppNormalizationTests`, `CompanionAppArgumentValidationTests`, and deterministic catalog/detection tests (including TRAE marker priority).

## Related

- [launch.md](./launch.md) — when companion runs in full open  
- [forms.md](./forms.md) — form fields  
- [intelligence.md](./intelligence.md) — command pills, not GUI apps  
