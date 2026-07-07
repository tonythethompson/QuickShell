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

## File structure

```
QuickShell.Raycast/
├── assets/              # extension and command icons (512px PNG)
├── changelog.md         # Store Version History (no version field in package.json)
├── package.json         # Raycast manifest + npm metadata
├── src/
│   ├── *.tsx            # one entry file per command name in the manifest
│   ├── components/      # shared UI (forms, platform guard)
│   └── lib/             # storage, launch, validation, search
└── src/__tests__/       # Vitest unit tests (lib only; no @raycast/api in tests)
```

Command entry files map 1:1 to manifest `commands[].name` (for example `open-workspace.tsx`).

## Deeplinks

Raycast deeplink format:

```
raycast://extensions/tonythethompson/quickshell/<command-name>
```

Examples:

- Open Workspace: `raycast://extensions/tonythethompson/quickshell/open-workspace`
- Create with folder: `raycast://extensions/tonythethompson/quickshell/create-workspace?arguments=%7B%22directory%22%3A%22C%3A%5CProjects%5Cfoo%22%7D`
- Edit by ID: `raycast://extensions/tonythethompson/quickshell/edit-workspace?arguments=%7B%22workspaceId%22%3A%22<id>%22%7D`
- Prefill Open search (fallback): append `?fallbackText=myproject`

Use **Copy Deeplink** on any command in Raycast root search for the exact URL.

## Development

```bash
cd QuickShell.Raycast
npm install
npm test
npm run build
npm run dev
```

Run `npm run build` before submitting Store changes. Raycast validates the distribution build separately from dev mode. Do not add a `version` field to `package.json`; Store versioning uses `changelog.md` and automatic updates.

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

- [x] `platforms: ["Windows"]`, MIT license, command subtitles/keywords, extension keywords
- [x] `changelog.md` and README for onboarding
- [x] Windows runtime guard and load-error toasts
- [ ] Store screenshots (Raycast Window Capture)
- [ ] Dedicated icon for Discover Git Repos command
- [ ] Decide Settings command vs Raycast Preferences API
- [ ] `eslint.config.js` / Prettier (optional tooling parity with Raycast scaffold)

## Scope

This extension tracks [Project 3](https://github.com/users/tonythethompson/projects/3). Broader CmdPal parity (shared `shortcuts.json`, git branch targets, full health checks) lives in [Project 4](https://github.com/users/tonythethompson/projects/4).

## Security note

Workspace data is stored in Raycast LocalStorage (encrypted per extension). Launch commands run locally with the user's permissions; admin launches use Windows elevation. See [Raycast security](https://developers.raycast.com/information/security).
