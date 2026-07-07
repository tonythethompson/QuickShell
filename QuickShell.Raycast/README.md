# QuickShell Raycast

Raycast-native workspace launcher for QuickShell on **Windows**.

## Commands

- **Open Workspace** — search, launch, favorite, duplicate, edit, import/export, undo/redo
- **Create Workspace** — directory-first form with auto-fill and multi-command launches
- **Edit Workspace** — searchable picker with inline form; optional `workspaceId` argument
- **Discover Git Repos** — scan common project folders and add repositories as workspaces
- **QuickShell Settings** — default terminal app, default profile, recents, import/export

Root search: type `qs`, `quickshell`, or a workspace home keyword to find commands and matches quickly.

## Requirements

- **Raycast for Windows**
- **Node.js 22.14+** (development only)
- Windows terminals such as Windows Terminal, PowerShell, or WSL

## Development

```bash
cd QuickShell.Raycast
npm install
npm test
npm run build
npm run dev
```

Run `npm run build` before submitting Store changes. Raycast validates the distribution build separately from dev mode.

### `Cannot find module .../develop/index.js`

PowerShell repair:

```powershell
cd QuickShell.Raycast
Remove-Item -Recurse -Force node_modules
Remove-Item -Force package-lock.json
npm install
npm run dev
```

Verify Node first:

```powershell
node -v   # must be v22.14.0 or newer
```

## Store checklist (in progress)

- [x] `platforms: ["Windows"]`, MIT license, command subtitles/keywords
- [x] `CHANGELOG.md` and README for onboarding
- [ ] Store screenshots (Raycast Window Capture)
- [ ] Dedicated icon for Discover Git Repos command
- [ ] Decide Settings command vs Raycast Preferences API

## Scope

This extension tracks [Project 3](https://github.com/users/tonythethompson/projects/3). Broader CmdPal parity (shared `shortcuts.json`, git branch targets, full health checks) lives in [Project 4](https://github.com/users/tonythethompson/projects/4).
