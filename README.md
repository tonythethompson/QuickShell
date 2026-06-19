# Quick Shell for Command Palette

A PowerToys Command Palette extension that opens saved directories in a terminal and optionally runs a command.

## Features

- Search shortcuts by name, abbreviation, path, or command
- Open Windows Terminal, PowerShell, PowerShell 7, or cmd
- Reload config without restarting CmdPal
- Fallback results so abbreviations can appear from the root search box

## Config

On first run, the extension creates:

`%LOCALAPPDATA%\QuickShell\shortcuts.json`

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

| Field | Required | Values |
| --- | --- | --- |
| `Name` | yes | Display name |
| `Abbreviation` | no | Short search keyword |
| `Directory` | yes | Folder to open |
| `Command` | no | Command to run after opening |
| `Terminal` | no | `wt`, `powershell`, `pwsh`, `cmd` (default: `wt`) |
| `RunAsAdmin` | no | `true` to always launch elevated (UAC prompt) |

See `shortcuts.example.json` for more examples.

## Build and install

### WinGet (recommended)

```powershell
winget install tonythethompson.QuickShell
```

After installing, run **Reload Command Palette Extension** in Command Palette.

### Build from source (MSIX / Visual Studio)

Prerequisites:

- Windows 11
- PowerToys with Command Palette enabled
- Visual Studio 2022 with the Windows development workload
- .NET 10 SDK

Steps:

1. Open `QuickShell.sln` in Visual Studio
2. Set platform to `x64`
3. Build → Deploy `QuickShell`
4. In Command Palette, run `Reload Command Palette Extension`
5. Search for `Quick Shell`

## Usage

- Search `Quick Shell` and pick a saved directory
- Or type an abbreviation like `api` at the root palette; matching shortcuts appear as fallback results
- Use `Reload shortcuts` after editing `shortcuts.json`
- Use **Open as administrator** from a shortcut's ⋯ menu for a one-off elevated launch
- Set `"RunAsAdmin": true` in JSON for shortcuts that should always elevate

## Project layout

- `QuickShell/Models` — shortcut model
- `QuickShell/Services` — config loading and terminal launch
- `QuickShell/Pages` — searchable CmdPal page
- `QuickShell/Commands` — invokable actions

## Notes

- The COM class GUID is `528cc766-cbe8-4861-9933-722c7a3f3581`
