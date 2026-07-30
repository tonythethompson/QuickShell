# WinGet package sources

Quick Shell publishes two WinGet package IDs:

| Package ID | Installer artifact | Contents |
| --- | --- | --- |
| `tonythethompson.QuickShell` | `QuickShell-Setup-{version}-{platform}.exe` | Command Palette + PowerToys Run |
| `tonythethompson.QuickShellforCmdPal` | `QuickShellforCmdPal-Setup-{version}-{platform}.exe` | Command Palette only (Store-equivalent) |

Both installers register the same Command Palette extension (shared Inno `AppId`). Installing one replaces the other; do not expect to run both side by side.

## Files in this folder

Template manifests for the repo copy. Release CI runs `wingetcreate update` against both IDs after each GitHub Release.

## Package status

| Package ID | Status |
| --- | --- |
| `tonythethompson.QuickShell` | Published; CI submits version bumps |
| `tonythethompson.QuickShellforCmdPal` | Published (initial `0.2.3.0`); CI submits version bumps |
| `tonythethompson.QuickShellforRun` | Initial package PR must merge once before CI can `wingetcreate update` |

`wingetcreate update` only works after the package exists in [microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs). Subsequent releases for published IDs are submitted by `.github/workflows/release-extension.yml`.
