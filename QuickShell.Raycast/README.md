# QuickShell Raycast

Raycast-native workspace launcher for QuickShell on **Windows**.

## Commands

- **Open Workspace** — search, launch, favorite, duplicate, edit, import/export, undo/redo
- **Create Workspace** — directory-first form with auto-fill; drafts enabled; opens Open Workspace after save
- **Edit Workspace** — searchable picker with inline form; optional `workspaceId` argument
- **Discover Git Repos** — scan common project folders and add repositories as workspaces
- **Manage Workspaces** — import/export, undo/redo, open Raycast extension preferences

**Extension preferences** (Raycast → Extensions → QuickShell): default terminal app, default profile, show recents, multi-command tabs, block dirty branch switch.

Root search: type `qs`, `quickshell`, or a workspace home keyword. Register **Open Workspace** as a fallback command to honor root-search text via `fallbackText`.

## Requirements

- **Raycast for Windows**
- **Node.js 22.14+** (development only)
- Windows terminals such as Windows Terminal, PowerShell, or WSL

## File structure

```
QuickShell.Raycast/
├── assets/              # extension and command icons (512px PNG)
├── CHANGELOG.md         # Store Version History (no version field in package.json)
├── eslint.config.js     # Raycast ESLint flat config
├── package.json         # Raycast manifest + npm metadata + preferences
├── src/
│   ├── *.tsx            # one entry file per command name in the manifest
│   ├── components/      # shared UI (forms, platform guard)
│   └── lib/             # storage, launch, validation, search, preferences
└── src/__tests__/       # Vitest unit tests (lib only; no @raycast/api in tests)
```

## Deeplinks

```
raycast://extensions/tonythethompson/quickshell/open-workspace
raycast://extensions/tonythethompson/quickshell/create-workspace?arguments=%7B%22directory%22%3A%22C%3A%5CProjects%5Cfoo%22%7D
raycast://extensions/tonythethompson/quickshell/edit-workspace?arguments=%7B%22workspaceId%22%3A%22<id>%22%7D
```

Open Workspace with launch context (after create):

```
raycast://extensions/tonythethompson/quickshell/open-workspace?context=%7B%22focusWorkspaceName%22%3A%22QuickShell%22%7D
```

## Development

```bash
cd QuickShell.Raycast
npm install
npm test
npm run lint
npm run build
npm run dev
```

Run `npm run build` before submitting Store changes. Do not add a `version` field to `package.json`; Store versioning uses `CHANGELOG.md`.

## Store checklist

- [x] Extension preferences for defaults, `CHANGELOG.md`, ESLint/Prettier scaffold
- [x] `useForm` validation, drafts, `launchCommand`, fallback text support
- [ ] Store screenshots (Raycast Window Capture)
- [ ] Dedicated icon for Discover Git Repos command

## Scope

Workspaces live in Raycast `LocalStorage` (`quickshell-data`), not shared `%LOCALAPPDATA%\QuickShell\` files. Import/export is the bridge to CmdPal/Run.

Raycast-local parity includes: trust/import contracts, Suggest.exe pills with local fallback, multi-companion form + presets, terminal-host and port-in-use health warnings, copyable launch diagnostics, git launch gate (`branchTargets` + `blockDirtyBranchSwitch`), and layout section separators. Intentional gaps remain: shared LocalAppData stores / Core `worktree-branch-targets.json`, process-list health, ETW.
