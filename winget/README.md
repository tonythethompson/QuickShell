# WinGet package sources

Quick Shell publishes two WinGet package IDs:

| Package ID | Installer artifact | Contents |
| --- | --- | --- |
| `tonythethompson.QuickShell` | `QuickShell-Setup-{version}-{platform}.exe` | Command Palette + PowerToys Run |
| `tonythethompson.QuickShellforCmdPal` | `QuickShellforCmdPal-Setup-{version}-{platform}.exe` | Command Palette only (Store-equivalent) |

Both installers register the same Command Palette extension (shared Inno `AppId`). Installing one replaces the other; do not expect to run both side by side.

## Files in this folder

Template manifests for the repo copy. Release CI runs `wingetcreate update` against both IDs after each GitHub Release.

## First-time setup for `tonythethompson.QuickShellforCmdPal`

`wingetcreate update` only works after the package exists in [microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs). Before the first release that ships `QuickShellforCmdPal-Setup-*.exe`:

1. Cut a GitHub Release that includes the CmdPal-only installers.
2. Update SHA256 values in `tonythethompson.QuickShellforCmdPal.installer.yaml`.
3. Open a PR to winget-pkgs with the three manifest files (or run `wingetcreate new` / `wingetcreate submit`).

Subsequent releases are updated automatically by `.github/workflows/release-extension.yml`.
