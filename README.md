# Terminal Shortcuts for Command Palette

A PowerToys Command Palette extension that opens saved directories in a terminal and optionally runs a command.

## Features

- Search shortcuts by name, abbreviation, path, or command
- Open Windows Terminal, PowerShell, PowerShell 7, or cmd
- Reload config without restarting CmdPal
- Fallback results so abbreviations can appear from the root search box

## Config

On first run, the extension creates:

`%LOCALAPPDATA%\TerminalShortcutsCmdPal\shortcuts.json`

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

See `shortcuts.example.json` for more examples.

## Build and install

### WinGet (recommended)

```powershell
winget install tonythethompson.TerminalShortcuts
```

After installing, run **Reload Command Palette Extension** in Command Palette.

### Build from source (MSIX / Visual Studio)

Prerequisites:

- Windows 11
- PowerToys with Command Palette enabled
- Visual Studio 2022 with the Windows development workload
- .NET 10 SDK

Steps:

1. Open `TerminalShortcuts.sln` in Visual Studio
2. Set platform to `x64`
3. Build → Deploy `TerminalShortcuts`
4. In Command Palette, run `Reload Command Palette Extension`
5. Search for `Terminal Shortcuts`

## Usage

- Search `Terminal Shortcuts` and pick a saved directory
- Or type an abbreviation like `api` at the root palette; matching shortcuts appear as fallback results
- Use `Reload shortcuts` after editing `shortcuts.json`

## Project layout

- `TerminalShortcuts/Models` — shortcut model
- `TerminalShortcuts/Services` — config loading and terminal launch
- `TerminalShortcuts/Pages` — searchable CmdPal page
- `TerminalShortcuts/Commands` — invokable actions

## Notes

- Update the `Publisher` identity in `Package.appxmanifest` before publishing
- The COM class GUID is `A3FFFE73-298E-4749-BE32-BFD576F0E3FF`
