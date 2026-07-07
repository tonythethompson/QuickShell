# Quick Shell release packages

Quick Shell ships as **separate install targets** for each host app. They share workspace data concepts, but each host has different install mechanics, so we publish standalone packages instead of one mega-installer.

## GitHub Release artifacts (`v*` tag)

| Artifact | Host | What it installs |
|---|---|---|
| `QuickShell-Setup-*-x64.exe` / `*-arm64.exe` | PowerToys | CmdPal extension **+** Run plugin (bundled PowerToys package) |
| `QuickShellforCmdPal-Setup-*.exe` | PowerToys | CmdPal extension only (Store-equivalent) |
| `QuickShellforRun-Setup-*.exe` | PowerToys | Run plugin only |
| `QuickShell.Run-*.zip` | PowerToys | Run plugin ZIP (manual extract) |
| `QuickShellforRaycast-Setup-*-x64.exe` | Raycast | Raycast extension sideload (Windows x64) |
| `QuickShell.Raycast.zip` | Raycast | Raycast extension ZIP (manual import) |

## WinGet package IDs

```powershell
# PowerToys CmdPal + Run (bundled)
winget install tonythethompson.QuickShell

# PowerToys CmdPal only
winget install tonythethompson.QuickShellforCmdPal

# PowerToys Run plugin only
winget install tonythethompson.QuickShellforRun

# Raycast extension only
winget install tonythethompson.QuickShellforRaycast
```

Initial manifests for `QuickShellforRun` and `QuickShellforRaycast` must be created in [microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs) before CI can auto-submit updates. The release workflow submits updates when `WINGET_PAT` is configured.

## Why not one installer for all three?

A single EXE that installs CmdPal + Run + Raycast sounds convenient, but it is a poor default:

- **Different prerequisites**: PowerToys vs Raycast for Windows
- **Different activation**: COM registration vs Raycast extension import/sideload
- **Different CPU support**: Raycast build is x64-only today; CmdPal/Run ship x64 + ARM64
- **Coupled releases**: a Raycast-only fix would force a CmdPal/Run reinstall

Keep **`tonythethompson.QuickShell`** as the PowerToys bundle (CmdPal + Run). Add separate WinGet IDs for Run-only and Raycast-only users.

Optional later: a **`QuickShellComplete`** WinGet meta-package that declares dependencies on the three host-specific packages once all manifests exist.

## CI workflow

Primary workflow: [`.github/workflows/release-extension.yml`](../.github/workflows/release-extension.yml) (Release WinGet installers).

Run-only tag releases: [`release-run-plugin.yml`](../.github/workflows/release-run-plugin.yml) (`run-v*`).
