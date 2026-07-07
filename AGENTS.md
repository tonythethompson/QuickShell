# AGENTS.md

Guidance for coding agents working in this repository.

## Repository overview

QuickShell is a Windows keyboard-first workspace launcher (.NET WinUI + PowerToys CmdPal). The repo also contains `QuickShell.Raycast/`, a Raycast for Windows extension that shares workspace concepts with the desktop app.

## Build and test

- **Desktop app**: `dotnet build QuickShell.sln` and `dotnet test QuickShell.sln`
- **Raycast extension**: `cd QuickShell.Raycast && npm install && npm test`

## Protected files

Do not edit `Directory.Build.props` unless explicitly asked. It pins the app version (`AppVersion`). A Claude `PreToolUse` hook enforces this policy.

## Cursor Cloud specific instructions

Cloud agents run on **Linux**, not Windows.

- Paths are case-sensitive. Use exact casing from the repo.
- The desktop app and Raycast launch logic target Windows (`wt.exe`, `powershell.exe`, etc.). Unit tests mock these; do not assume Windows binaries exist on the VM.
- **Raycast extension** (`QuickShell.Raycast/`):
  - Requires **Node.js >= 22.14.0** (`engines` in `package.json`, `.nvmrc`)
  - `npm run dev` needs a complete `@raycast/api` install; if `develop/index.js` is missing, run a clean `npm install` (see `QuickShell.Raycast/README.md`)
  - `platforms` in `package.json` is `["Windows"]`; cloud agents can still edit and test TypeScript with `npm test` and `npx tsc --noEmit`
- **Claude hooks** (`.claude/settings.json`): guards use POSIX shell + Python, not PowerShell, so file edits work on Linux cloud VMs.
- **Branch naming** for cloud agents: `cursor/<descriptive-name>-2981`
- Prefer focused diffs; Raycast work stays under `QuickShell.Raycast/` unless integrating with core services.
