# Quick Shell for PowerToys Run

Third-party plugin that reads the same shortcuts and settings as the [Quick Shell extension](https://github.com/tonythethompson/QuickShell).

## Install

### From GitHub Releases

1. Download `QuickShell.Run-x64.zip` or `QuickShell.Run-ARM64.zip` from [Releases](https://github.com/tonythethompson/QuickShell/releases) (added in releases after v0.1.7.0; older tags are CmdPal installer only).
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
| Browse shortcuts | **Alt+Space** → `qs` (optionally `qs measure`, etc.) |
| Home keyword in Run | **Alt+Space** → `qs` + keyword (e.g. `qs measure`) — not bare `measure` |
| Create / export / import | **Alt+Space** → `qs` → pick a utility row, or use **PowerToys Settings → PowerToys Run → Quick Shell** |
| Edit shortcut | Select shortcut → **→** context menu → **Edit shortcut** |
| Run elevated | **Ctrl+Shift+Enter**, or context menu |

Shared data lives in `%LOCALAPPDATA%\QuickShell\` (`shortcuts.json`, `settings.json`).

## Submit to Microsoft’s plugin list

Open a PR to [microsoft/PowerToys `doc/thirdPartyRunPlugins.md`](https://github.com/microsoft/PowerToys/blob/main/doc/thirdPartyRunPlugins.md) with:

```markdown
| [Quick Shell](https://github.com/tonythethompson/QuickShell) | [tonythethompson](https://github.com/tonythethompson) | Open saved project folders in any terminal — shared shortcuts with the Quick Shell extension |
```

Link the release ZIP in the PR description.
