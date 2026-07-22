# Quick Shell for PowerToys Run

Third-party plugin that reads the same shortcuts and settings as the [Quick Shell extension](https://github.com/tonythethompson/QuickShell).

## Install

### WinGet or GitHub EXE (bundled)

The **`tonythethompson.QuickShell`** WinGet package and **`QuickShell-Setup-*.exe`** installers include both the Command Palette extension and this Run plugin. Restart PowerToys after install.

```powershell
winget install tonythethompson.QuickShell
```

### CmdPal-only installs

If you use **`tonythethompson.QuickShellforCmdPal`**, the Microsoft Store, or **`QuickShellforCmdPal-Setup-*.exe`**, Command Palette is installed without Run. Use the ZIP steps below.

### Run plugin ZIP only

Use this if you installed Quick Shell from the **Microsoft Store** (CmdPal only) and want Run without reinstalling:

1. Download `QuickShell.Run-x64.zip` or `QuickShell.Run-ARM64.zip` from [Releases](https://github.com/tonythethompson/QuickShell/releases).

   - Full app releases (`v*`) include the ZIP alongside installers.
   - Run-only updates ship on `run-v*` tags (for example `run-v0.2.1.0`) without a new CmdPal installer.

2. Extract into:

   `%LOCALAPPDATA%\Microsoft\PowerToys\PowerToys Run\Plugins\QuickShell\`

3. Restart PowerToys.

### From source

```powershell
.\scripts\build-run-plugin.ps1 -Deploy
```

Restart PowerToys after deploy.

## Usage

| Action | How |
| --- | --- |
| Browse shortcuts | **Alt+Space** → `qs` — shortcuts appear first; type more to filter (e.g. `qs measure`) |
| Global activation | **Alt+Space** → type `Quick Shell`, `quickshell`, or `qs` without the action keyword |
| Home keywords | Per-workspace **Home keyword** in shortcut settings (same as CmdPal abbreviation) |
| Manage actions | `qs create`, `qs settings`, `qs export`, etc., or browse after global activation |
| Settings | `qs settings` or PowerToys → Quick Shell → **Open settings…** (full WPF window) |
| Edit shortcut | Context menu → **Edit shortcut** (one-page WPF editor; shared Core `WorkspaceEditor`) |
| Run elevated | **Ctrl+Shift+Enter**, or context menu |

Shared data lives in `%LOCALAPPDATA%\QuickShell\` (`shortcuts.json`, `settings.json`).

## Submit to Microsoft’s plugin list

Open a PR to [microsoft/PowerToys `doc/thirdPartyRunPlugins.md`](https://github.com/microsoft/PowerToys/blob/main/doc/thirdPartyRunPlugins.md) with:

```markdown
| [Quick Shell](https://github.com/tonythethompson/QuickShell) | [tonythethompson](https://github.com/tonythethompson) | Open saved project folders in any terminal — shared shortcuts with the Quick Shell extension |
```

Link the release ZIP in the PR description.
