# Quick Shell

**Open your favorite project folders from [PowerToys Command Palette](https://learn.microsoft.com/windows/powertoys/command-palette/overview) — in one search.**

Save directories you use every day, pick a terminal, optionally run a command on open (`dotnet run`, `npm run dev`, and so on), and jump there without digging through File Explorer.

---

## What you can do

- **Save shortcuts** to folders you open often, with optional abbreviations for fast search
- **Launch in your terminal** — Windows Terminal (with profile), PowerShell, PowerShell 7, or cmd
- **Run a command on open** — start dev servers, scripts, or anything else automatically
- **Pin shortcuts** so they stay at the top of your list
- **Create and edit shortcuts in Command Palette** — no hand-editing JSON required
- **Open elevated** when you need admin — from the ⋯ menu or with **Ctrl+Enter**
- **Search from the root palette** — type an abbreviation like `api` and matching shortcuts appear without opening the extension first

---

## Requirements

- Windows 10 version 2004 (build 19041) or later — **Windows 11 recommended**
- [PowerToys](https://learn.microsoft.com/windows/powertoys/install) with **Command Palette** enabled

---

## Install

### Option 1 — WinGet (easiest)

```powershell
winget install tonythethompson.QuickShell
```

### Option 2 — Download an installer

Get the latest **x64** or **ARM64** installer from [GitHub Releases](https://github.com/tonythethompson/QuickShell/releases).

### After installing

1. Open **PowerToys Command Palette** (default: **Win + Alt + Space**)
2. Run **`Reload Command Palette Extension`**
3. Search **`Quick Shell`**

You should see **Quick Shell** with the subtitle *Open saved folders in your terminal*.

> **Tip:** If the extension does not appear, confirm Command Palette is on in PowerToys → Command Palette, then run **Reload Command Palette Extension** again.

---

## Quick start

1. Open Command Palette and search **Quick Shell**
2. Choose **Create new shortcut**
3. Pick a folder, name it, and save
4. Select the shortcut to open it in your terminal

Your shortcuts are stored at:

`%LOCALAPPDATA%\QuickShell\shortcuts.json`

The app creates this file on first run. You can edit it in any text editor, or manage shortcuts entirely from Command Palette.

---

## Everyday usage

| What you want | How |
| --- | --- |
| Open a saved folder | Search **Quick Shell**, pick a shortcut |
| Jump straight to a shortcut | Type its **abbreviation** in the root search box (e.g. `api`) |
| Reload after editing JSON | Run **Reload shortcuts** inside Quick Shell |
| Open once as admin | Select a shortcut → **⋯** → **Open as administrator**, or press **Ctrl+Enter** |
| Always open as admin | Set `"RunAsAdmin": true` on that shortcut (see below) |
| Change default terminal | **Quick Shell** → **⋯** → **Quick Shell settings** |

---

## Shortcut options

Each shortcut supports these fields in `shortcuts.json`:

| Field | Required | Description |
| --- | --- | --- |
| `Name` | Yes | Display name in Command Palette |
| `Directory` | Yes | Folder to open |
| `Abbreviation` | No | Short keyword for root search (e.g. `api`) |
| `Command` | No | Command to run after opening the folder |
| `Terminal` | No | `wt`, `powershell`, `pwsh`, or `cmd` (default: `wt`) |
| `RunAsAdmin` | No | `true` to always launch elevated (UAC prompt) |

Example:

```json
[
  {
    "Name": "My API",
    "Abbreviation": "api",
    "Directory": "C:\\Projects\\MyApi",
    "Command": "dotnet run",
    "Terminal": "wt"
  }
]
```

More examples: [`shortcuts.example.json`](shortcuts.example.json).

---

## Troubleshooting

**Extension missing after install**  
Run **Reload Command Palette Extension** in Command Palette. Restart PowerToys if needed.

**Shortcuts disappeared after an update**  
Check `%LOCALAPPDATA%\QuickShell\shortcuts.json.bak` for a backup. Older installs may also have left a copy at `%LOCALAPPDATA%\TerminalShortcutsCmdPal\shortcuts.json`.

**Duplicate or broken Quick Shell in Windows Settings**  
You may have an old installer alongside a newer one. In **Settings → Apps**, uninstall extra **Quick Shell** entries and keep a single install.

**WinGet install works but Command Palette integration is incomplete**  
The WinGet installer registers the extension for discovery. For local development or the fullest MSIX integration, see [Building from source](#building-from-source) below.

---

## Accessibility

Quick Shell is built on PowerToys Command Palette, which supports keyboard navigation, screen readers, and high contrast. For a testing checklist and links to Windows accessibility settings, see [`ACCESSIBILITY.md`](ACCESSIBILITY.md).

---

## Building from source

For contributors and local MSIX installs (recommended for development):

**Prerequisites:** Windows 11, .NET 10 SDK, Visual Studio 2022 (Windows workload), PowerToys with Command Palette enabled.

```powershell
# MSIX install (dev-signed, full Command Palette integration)
.\scripts\deploy.ps1

# WinGet-style EXE installers (x64 + ARM64)
cd QuickShell
.\build-exe.ps1 -Version 0.1.2.2
```

Then run **Reload Command Palette Extension** in Command Palette.

---

## License

MIT — see [LICENSE](LICENSE).

## Feedback

[Open an issue](https://github.com/tonythethompson/QuickShell/issues) on GitHub for bugs, ideas, or questions.
