# Quick Shell release packages

Quick Shell ships as **separate install targets** for each host app. They share workspace data concepts, but each host has different install mechanics, so we publish standalone packages instead of one mega-installer.

## GitHub Release artifacts (`v*` tag)

| Artifact | Host | What it installs |
|---|---|---|
| `QuickShell-Setup-*-x64.exe` / `*-arm64.exe` | PowerToys | CmdPal extension **+** Run plugin (bundled PowerToys package) |
| `QuickShellforCmdPal-Setup-*.exe` | PowerToys | CmdPal extension only (Store-equivalent) |
| `QuickShellforRun-Setup-*.exe` | PowerToys | Run plugin only |
| `QuickShell.Run-*.zip` | PowerToys | Run plugin ZIP (manual extract) |

**Raycast** is not published on GitHub Releases or WinGet. Install from the [Raycast Store](https://www.raycast.com/store). Local `scripts/build-raycast-extension.ps1` remains for development / Store packaging only.

## WinGet package IDs

```powershell
# PowerToys CmdPal + Run (bundled)
winget install tonythethompson.QuickShell

# PowerToys CmdPal only
winget install tonythethompson.QuickShellforCmdPal

# PowerToys Run plugin only
winget install tonythethompson.QuickShellforRun
```

Initial manifests for `QuickShellforRun` must be created in [microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs) before CI can auto-submit updates. The release workflow submits updates when `WINGET_PAT` is configured.

The former `tonythethompson.QuickShellforRaycast` WinGet package is retired (no further CI updates). Prefer the Raycast Store.

## Why not one installer for all hosts?

A single EXE that installs CmdPal + Run + Raycast sounds convenient, but it is a poor default:

- **Different prerequisites**: PowerToys vs Raycast
- **Different activation**: COM registration vs Raycast Store extension
- **Different CPU support**: CmdPal/Run ship x64 + ARM64
- **Coupled releases**: a Raycast-only fix would force a CmdPal/Run reinstall

Keep **`tonythethompson.QuickShell`** as the PowerToys bundle (CmdPal + Run).

## CI workflow

Primary workflow: [`.github/workflows/release-extension.yml`](../.github/workflows/release-extension.yml) (Release WinGet installers).

Run-only tag releases: [`release-run-plugin.yml`](../.github/workflows/release-run-plugin.yml) (`run-v*`).
