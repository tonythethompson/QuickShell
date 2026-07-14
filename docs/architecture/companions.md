# Companion apps (as-built)

Optional **GUI** applications opened for a workspace folder (editors, IDEs, git clients, Explorer). Separate from terminal multi-launch and from agent CLIs (`claude`, etc.).

## Mental model

```
Persisted on TerminalShortcut
  OpenCompanionAppOnLaunch
  CompanionAppPath
  CompanionAppArguments   // ".", "{folder}", "{solution}", free text

Form
  CompanionAppPreset: none | catalog id | custom
  Reconcile path/args for dropdown + browse

Detection (create)
  folder signals → installed preset (if exe found)

Launch
  Process.Start(exe, expanded args, WorkingDirectory = workspace)
```

Not part of tab grouping. At most one companion process on full workspace open (or on-demand).

## Catalog (`CompanionAppCatalog`)

Presets (installed-only in form dropdown) include among others:

| Preset | Default args (typical) |
|--------|-------------------------|
| Explorer, Fork, GitHub Desktop, Rider, IntelliJ, Obsidian, Azure Data Studio | `{folder}` |
| VS Code, Cursor, Zed, Sublime, Neovide, GVim | `.` |
| Visual Studio 2022 / 2026 | `{solution}` |
| Notepad++ | empty (no folder open by default) |
| Custom | user path |

Resolution: candidate paths, `VisualStudioInstallDiscovery`, `JetBrainsInstallDiscovery` (Rider / IDEA). Infer preset from executable filename / devenv path.

Form helpers: `ReconcileStoredShortcut`, `ReconcileForForm`, `ReconcileForSave`, `GetInstalledFormChoices`, browse → `ResolvePresetAfterBrowse`.

Disk stores **path** (and flag/args); form **infers** preset when reopening.

## Detection (`CompanionAppDetection`)

First match, only if executable resolves:

| Priority (approx.) | Signal | Preset |
|--------------------|--------|--------|
| High | `.cursor/` | Cursor |
| | `.vscode/` | VS Code |
| | `.obsidian/` | Obsidian |
| | Zed project | Zed |
| | JetBrains + .NET | Rider |
| | VS solution / `.vs` | VS 2026 then 2022 |
| | `.idea/` | IntelliJ |
| | Sublime project | Sublime |
| Low | `.git/` | Fork, else GitHub Desktop |

Signals: `WorkspaceCompanionSignals`. Suggestion sets `EnableOnLaunch = true`.

Known product gaps (not blocking): JetBrains beyond Rider/IDEA (all `.idea` → IDEA), VS Code Insiders, other AI IDEs, Notepad++ empty args.

## Launch (`CompanionAppLauncher`)

| Mode | When |
|------|------|
| Auto (`onDemand: false`) | Full `ShortcutLaunchExecutor.Launch` if open-on-launch + path set |
| On demand (`onDemand: true`) | ⋯ Open companion — ignores auto flag |

Single-row `LaunchEntry` sets **IncludeCompanionApp: false**.

Steps: resolve exe → directory exists → expand args → `Process.Start` (`UseShellExecute`). Auto failure is **soft** (terminals still start; warning in post-launch). On-demand surfaces StayOpen errors.

### Argument expansion

| Template | Result |
|----------|--------|
| empty | no args |
| `.` | workspace path (quoted if spaces) |
| `{folder}` | workspace path |
| `{solution}` | top-level `*.sln` or folder fallback |

Working directory = workspace folder.

## Form validation (`CompanionAppArgumentValidation`)

- Args field visibility depends on preset/path.
- Save: max length, no newlines, only `{folder}` / `{solution}` brace placeholders.
- Soft warnings for mismatched tokens (e.g. VS without `{solution}`).

## List / context

- `ShortcutHealth`: missing companion path subtitle when open-on-launch.
- Context menu: open companion when path configured.
- Full `WorkspaceHealthCheck` does not dedicate a companion finding kind.

## Agent CLIs vs companions

| | Companion | Terminal agent CLI |
|--|-----------|-------------------|
| Process | GUI `.exe` | Command in terminal |
| Config | Path + args + flag | Launch row command |
| Detection | Folder → IDE | PATH (if added later as pills) |

Keep agent CLIs on launch rows / [intelligence.md](./intelligence.md) pills — not this catalog.

## Key files

| File | Role |
|------|------|
| `CompanionAppCatalog.cs` | Presets, paths, form reconcile |
| `CompanionAppDetection.cs` | Folder suggest |
| `WorkspaceCompanionSignals.cs` | Markers |
| `CompanionAppLauncher.cs` | Expand + start |
| `CompanionAppArgumentValidation.cs` | Form rules |
| `OpenCompanionAppCommand` / context menu | On-demand |
| `ShortcutFormPage` | Reconcile on load |

## Tests

`CompanionAppLauncherTests` (expansion + validation paths; success start avoids real process), `CompanionAppArgumentValidationTests`, related catalog tests.

## Related

- [launch.md](./launch.md) — when companion runs in full open
- [forms.md](./forms.md) — form fields
- [intelligence.md](./intelligence.md) — command pills, not GUI apps
