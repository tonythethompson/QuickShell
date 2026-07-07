# QuickShell Raycast

Raycast-native workspace launcher for QuickShell.

## Commands

- **Open Workspace** — search, launch, favorite, duplicate, edit, and open saved workspaces
- **Create Workspace** — create a workspace with name, directory, terminal, and launch command
- **Edit Workspace** — searchable picker with inline form; optional `workspaceId` argument for direct edit
- **Settings** — default terminal app, default profile, and recent workspaces toggle

## Development

Requires **Node.js 22.14+** and **Raycast for Windows**.

```bash
cd QuickShell.Raycast
npm install
npm test
npm run dev
```

### `Cannot find module .../develop/index.js`

That means `@raycast/api` did not install completely (common after an interrupted install or antivirus blocking large files).

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

If `npm run dev` still fails, confirm this file exists:

`node_modules\@raycast\api\dist\commands\develop\index.js`

It should be about 3–4 MB. If it is missing or tiny, rerun `npm install` with antivirus exclusions for the repo folder.

## MVP scope

This extension tracks [Project 3](https://github.com/users/tonythethompson/projects/3). Broader parity work lives in [Project 4](https://github.com/users/tonythethompson/projects/4) and stays out of the MVP unless the core path is complete.
