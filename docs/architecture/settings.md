# Settings (as-built)

Global preferences (terminal host, multi-launch presentation, git gate, recents). **Not** stored in `shortcuts.json`.

## Where settings live

| Host | Path / mechanism |
|------|------------------|
| **CmdPal + Run (shared file)** | `%LOCALAPPDATA%\QuickShell\settings.json` |
| **CmdPal** | `QuickShellSettingsManager` + `QuickShellJsonSettingsStore` (CmdPal `Settings` toolkit + JSON file) |
| **Run** | `QuickShellSettingsReader` (same JSON keys; WPF settings UI) |
| **Raycast** | Raycast storage `StoredData.settings` (schema in `schema.ts`) — **not** the same file as desktop unless user migrates manually |

Core helpers that know key names:

- `QuickShellSettingsReader.cs` — read/write shared desktop JSON
- `QuickShellMultiLaunchSettings.cs` — `multiLaunchPresentation`
- `QuickShellRecentSettings.cs` — recent count normalization

## Keys (desktop `settings.json`)

| Key | Purpose | Typical values |
|-----|---------|----------------|
| `terminalApplication` | Global terminal host | `system`, `wt`, `it`, `conhost` |
| `defaultProfile` | Profile when workspace/row is Default | `__default__` or profile name / shell id |
| `multiLaunchPresentation` | Tabs vs windows | `singleWindowTabs` (default), `separateWindows` |
| `blockDirtyBranchSwitch` | Git gate policy | `"true"` / `"false"` (default true) |
| `recentWorkspaceCount` | Home “Recent” section | Normalized count; **0 hides** section (CmdPal text setting quirks) |

Raycast settings mirror terminal app, default profile, recent count, multi-launch; git dirty gate may differ by version — check `schema.ts` / `settings.ts`.

## CmdPal wiring

`QuickShellSettingsManager`:

1. Builds `ChoiceSetSetting` / `TextSetting` for each key.  
2. Loads via `QuickShellJsonSettingsStore`.  
3. Hydrates defaults (incl. legacy terminal defaults migration).  
4. Exposes `TerminalApplicationId`, `DefaultProfileId`, `SeparateWindowsForMultiLaunch`, `BlockDirtyBranchSwitch`, etc. to launch/list.  
5. Settings **page** is a composite `QuickShellExtensionSettingsPage` with sub-forms:
   - Terminal defaults  
   - Multi-launch  
   - Git launch  
   - Home display / recents  
   - Behavior  
   - Transfer (import/export/reset)  
   - Pending edit draft (when present)

`SettingsChanged` → home page reload.

Launch always reads **live** manager/reader values (not baked into workspace JSON).

## Run wiring

`QuickShell.Run` uses `QuickShellSettingsReader` for launch options and its own settings window/panel for the same keys.

## Consumers

| Consumer | Settings used |
|----------|----------------|
| `ShortcutLaunchExecutor` | multi-launch separate windows, block dirty branch |
| `TerminalCatalog.ResolveForShortcut` | terminal app + default profile |
| Home list | recent count |
| Health / status | terminal app + default profile for profile existence |

## Gotchas

1. **Two ecosystems** — desktop JSON vs Raycast stored settings.  
2. **Recent “count”** — CmdPal text setting is partly on/off semantics (see setting description in manager).  
3. Changing terminal app invalidates form terminal choice caches (`TerminalCatalog.InvalidateCache` on refresh).  
4. Do not put per-workspace prefs in settings — those are on `TerminalShortcut` / launches.

## Key files

| File | Role |
|------|------|
| `QuickShell/QuickShellSettingsManager.cs` | CmdPal settings API |
| `QuickShell/Services/QuickShellJsonSettingsStore.cs` | File-backed settings |
| `QuickShell/Pages/*SettingsForm*.cs` | Sub-forms |
| `QuickShell.Core/Services/QuickShellSettingsReader.cs` | Shared read/write for Run/tests |
| `QuickShell.Raycast/src/lib/settings.ts` / `schema.ts` | Raycast prefs |

## Related

- [launch.md](./launch.md) — uses multi-launch + git gate + defaults  
- [cmdpal-surface.md](./cmdpal-surface.md) — settings page entry  
- [hosts.md](./hosts.md) — Run/Raycast parity  
