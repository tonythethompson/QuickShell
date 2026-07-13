# AGENTS.md

Guidance for coding agents working in this repository.

## Repository overview

QuickShell is a Windows keyboard-first workspace launcher (.NET WinUI + PowerToys CmdPal). The repo also contains `QuickShell.Raycast/`, a Raycast for Windows extension that shares workspace concepts with the desktop app.

Quick Shell is a **Windows-only .NET 10 desktop product**: a PowerToys Command Palette extension (`QuickShell`), a PowerToys Run plugin (`QuickShell.Run`), and a shared business-logic library (`QuickShell.Core`, tested by `QuickShell.Core.Tests`). Every runnable component targets `net10.0-windows*` and depends on WinForms/WPF/WinUI, the Windows App SDK, CsWinRT, and MSIX packaging.

## Architecture (as-built)

Contributor-oriented tours of how the code works today live under **`docs/architecture/`**:

- Start: [`docs/architecture/README.md`](docs/architecture/README.md)
- Core spines: overview, launch, persistence, forms, intelligence (pills), companions
- Hosts & chrome: settings, CmdPal surface, git/discover, hosts (CmdPal/Run/Raycast), post-launch
- Priorities: [\docs/architecture/roadmap-next-steps.md\](docs/architecture/roadmap-next-steps.md)

Numbered `0001`–`0005` docs in that folder are **proposals** (may lag landed code). Prefer the as-built tours when changing behavior, and update the matching tour when you change a spine.

## Build and test

- **Desktop app (Windows)**: `dotnet build QuickShell.sln` and `dotnet test QuickShell.sln` — see `README.md` and `.github/workflows/ci.yml`
- **Raycast extension**: `cd QuickShell.Raycast && npm install && npm test`

## Protected files

Do not edit `Directory.Build.props` unless explicitly asked. It pins the app version (`AppVersion`). A Claude `PreToolUse` hook enforces this policy.

## Cursor Cloud specific instructions

The Cursor Cloud VM is **Linux**, so the GUI product **cannot be run** here and cannot be fully built or tested here. Full build/test/run happens on Windows — see the `Building from source` section of `README.md` and the `windows-latest` job in `.github/workflows/ci.yml` for the authoritative commands (`scripts/deploy.ps1` is the Windows dev loop).

### What works on this Linux VM

- The **.NET 10 SDK** is installed system-wide at `/usr/share/dotnet` (`dotnet` is on `PATH` via `/usr/local/bin/dotnet`). It persists in the VM snapshot; the startup update script only runs `dotnet restore` (see below).
- `dotnet restore QuickShell.sln -p:EnableWindowsTargeting=true` succeeds for all projects.
- `dotnet build QuickShell.Core/QuickShell.Core.csproj -c Release -p:Platform=x64 -p:EnableWindowsTargeting=true` succeeds. This is the only project that compiles on Linux, and it is **build-only** — `net10.0-windows7.0` assemblies cannot execute here (no Windows Desktop runtime).
- Paths are case-sensitive. Use exact casing from the repo.
- **Raycast extension** (`QuickShell.Raycast/`):
  - Requires **Node.js >= 22.14.0** (`engines` in `package.json`, `.nvmrc`)
  - `npm run dev` needs a complete `@raycast/api` install; if `develop/index.js` is missing, run a clean `npm install` (see `QuickShell.Raycast/README.md`)
  - `platforms` in `package.json` is `["Windows"]`; cloud agents can still edit and test TypeScript with `npm test` and `npx tsc --noEmit`

### What does NOT work on this Linux VM (expected, do not try to "fix")

- Building `QuickShell`, `QuickShell.Run`, or `QuickShell.Core.Tests` fails: CsWinRT runs `cswinrt.exe` (a Windows PE binary) during build → `Exec format error`. MSIX tooling and the WinUI/Windows App SDK are also Windows-only.
- `dotnet test` cannot run: the test project is `net10.0-windows10.0.26100.0` and references the WinUI extension, so it requires the Windows runtime.
- `EnableWindowsTargeting=true` is required for any restore/build of the `-windows` TFMs on Linux; without it restore/build errors out with NETSDK1100.
- The desktop app and Raycast launch logic target Windows (`wt.exe`, `powershell.exe`, etc.). Unit tests mock these; do not assume Windows binaries exist on the VM.

Validate cross-platform changes to shared logic by building `QuickShell.Core` on Linux; anything touching the extension, Run plugin, tests, or packaging must be verified on Windows.

### Agent workflow notes

- **Claude hooks** (`.claude/settings.json`): guards use POSIX shell + Python, not PowerShell, so file edits work on Linux cloud VMs.
- **Branch naming** for cloud agents: `cursor/<descriptive-name>-2981`
- Prefer focused diffs; Raycast work stays under `QuickShell.Raycast/` unless integrating with core services.

### Multi-command launch (tabs vs windows)

- Setting key: `multiLaunchPresentation` — `singleWindowTabs` (default) or `separateWindows` in `%LOCALAPPDATA%\\QuickShell\\settings.json` and Raycast stored settings.
- Desktop: `ShortcutLaunchExecutor.LaunchAll` groups compatible entries via `GroupPlans` / `TerminalLauncher.OpenGroup` (`; new-tab`). Raycast mirrors this in `launch-grouping.ts` + `windows-launch.ts` (do not pass `-w` on tab segments).
- Tabs require Windows Terminal (or Intelligent Terminal) as the global terminal app; Console Host and mixed elevation always fall back to separate windows. See `docs/getting-started.md`.
