# Command Palette Extension – Copilot Instructions

Concise guidance for AI-assisted development of this Command Palette extension.

## Project Structure

| Folder / project | Purpose |
|------------------|---------|
| `QuickShell/` | CmdPal extension — `Pages/`, `Commands/`, `Services/`, `Assets/`, `Program.cs`, `QuickShellCommandsProvider.cs` |
| `QuickShell.Core/` | Shared models and services — `ShortcutRepository`, terminal discovery, JSON, launch logic |
| `QuickShell.Run/` | PowerToys Run plugin (`qs` keyword) |
| `QuickShell.Core.Tests/` | Unit tests for core services |
| `scripts/` | Build, deploy, store, and asset scripts (`deploy.ps1`, `run-cmdpal-dev.ps1`, etc.) |
| `docs/` | GitHub Pages site (Jekyll) |
| `cmdpal-gallery/` | CmdPal Extension Gallery submission package |

## Key Conventions

- Extensions run **out-of-process** via COM server registration
- `QuickShell/Program.cs` hosts the COM server — do not modify the hosting pattern
- `QuickShellCommandsProvider` is the CmdPal entry point for all commands
- Pages are **ICommand** implementations — they can be used anywhere commands are used
- UI term is **workspace**; on-disk file remains `%LOCALAPPDATA%\QuickShell\shortcuts.json`
- Always **Deploy** (not just Build) to register the MSIX package
- After deploying, use the **Reload** command in Command Palette to refresh

## Build & Deploy

```powershell
# Default dev loop: stop CmdPal → build/install MSIX → start CmdPal
.\scripts\deploy.ps1

# Local PowerToys CmdPal SDK (sibling PowerToys checkout)
.\scripts\run-cmdpal-dev.ps1 -UseLocalSdk
```

In Visual Studio: **Build > Deploy** (not just Build), then run **Reload Command Palette Extension** in CmdPal.

## Source Control

If using git, remove these lines from `.gitignore` (needed for deployment):
- `**/Properties/launchSettings.json`
- `*.pubxml`

## Available Skills

This project includes Copilot skills for common workflows:
- **add-adaptive-card-form** — Create form-based UI with Adaptive Cards
- **add-extension-settings** — Add a settings page to your extension
- **add-dock-band** — Add persistent toolbar widgets
- **add-fallback-commands** — Add catch-all search commands
- **publish-extension** — Publish to Microsoft Store or WinGet

## Documentation

- [Creating an extension](https://learn.microsoft.com/windows/powertoys/command-palette/creating-an-extension)
- [Extension samples](https://learn.microsoft.com/windows/powertoys/command-palette/samples)
- [Extensibility overview](https://learn.microsoft.com/windows/powertoys/command-palette/extensibility-overview)
