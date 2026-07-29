# Quick Shell release packages

Quick Shell ships as **separate install targets** for each host app. They share workspace data concepts, but each host has different install mechanics, so we publish standalone packages instead of one mega-installer.

## Release checklist (product `v*` tag)

One tag push updates **GitHub Release**, **WinGet**, and the **Microsoft Store** (package + What's new).

1. Bump `AppVersion` in [`Directory.Build.props`](../Directory.Build.props) and `Identity.Version` in [`QuickShell/Package.appxmanifest`](../QuickShell/Package.appxmanifest) to the same `X.Y.Z.W`.
2. Move bullets from `## [Unreleased]` in [`CHANGELOG.md`](../CHANGELOG.md) under a new heading:
   `## [X.Y.Z.W] - YYYY-MM-DD`
   Write user-facing **Added** / **Fixed** / **Changed** / **Removed** notes (not commit or PR titles). Keep it short enough for Store What's new (about 5–15 bullets). Leave an empty `## [Unreleased]` section for the next cycle.
3. Commit, tag `vX.Y.Z.W`, and push the tag (or run **Release WinGet installers** via `workflow_dispatch` with that version).
4. Expect, in order:
   - [`.github/workflows/release-extension.yml`](../.github/workflows/release-extension.yml) builds installers, creates the GitHub Release from `CHANGELOG.md`, and opens WinGet manifest PRs (when `WINGET_PAT` is set).
   - [`.github/workflows/publish-store.yml`](../.github/workflows/publish-store.yml) runs on that published `v*` release (not pre-releases, not `run-v*`), builds the `.msixupload`, uploads with `--noCommit`, sets listing **ReleaseNotes** from the same changelog section, then commits the Partner Center submission. Certification is asynchronous.

Release CI **fails** if the changelog section for that version is missing or empty, or if the tag version does not match both `AppVersion` and `Package.appxmanifest`.

Optional Store-only re-submit: run **Publish to Microsoft Store** with `workflow_dispatch` and the tag (for example `v0.2.3.0`).

## Changelog rules

- Source of truth: root [`CHANGELOG.md`](../CHANGELOG.md) ([Keep a Changelog](https://keepachangelog.com/) style).
- Published notes are **never** auto-generated from `git log` or GitHub auto release notes.
- Raycast keeps its own [`QuickShell.Raycast/CHANGELOG.md`](../QuickShell.Raycast/CHANGELOG.md) for Raycast Store Version History; do not merge it into the root file.

Extract a section locally:

```powershell
.\scripts\extract-changelog.ps1 -Version 0.2.3.0
```

## GitHub Release artifacts (`v*` tag)

| Artifact | Host | What it installs |
| --- | --- | --- |
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

## Microsoft Store

- Store ID: `9PC8S6LNRT3R` ([listing](https://apps.microsoft.com/detail/9PC8S6LNRT3R))
- Package: unsigned `.msixupload` from [`scripts/build-store.ps1`](../scripts/build-store.ps1) (Microsoft re-signs after certification)
- What's new: `listings.en-us.baseListing.releaseNotes`, patched from `CHANGELOG.md` via [`scripts/set-store-release-notes.ps1`](../scripts/set-store-release-notes.ps1)
- Secrets (already configured): `MSSTORE_TENANT_ID`, `MSSTORE_SELLER_ID`, `MSSTORE_CLIENT_ID`, `MSSTORE_CLIENT_SECRET`

## Why not one installer for all hosts?

A single EXE that installs CmdPal + Run + Raycast sounds convenient, but it is a poor default:

- **Different prerequisites**: PowerToys vs Raycast
- **Different activation**: COM registration vs Raycast Store extension
- **Different CPU support**: CmdPal/Run ship x64 + ARM64
- **Coupled releases**: a Raycast-only fix would force a CmdPal/Run reinstall

Keep **`tonythethompson.QuickShell`** as the PowerToys bundle (CmdPal + Run).

## CI workflow

| Workflow | Trigger | Channels |
| --- | --- | --- |
| [`release-extension.yml`](../.github/workflows/release-extension.yml) | `v*` tag or dispatch | GitHub Release + WinGet |
| [`publish-store.yml`](../.github/workflows/publish-store.yml) | published `v*` release (not pre-release) or dispatch | Microsoft Store package + What's new |
| [`release-run-plugin.yml`](../.github/workflows/release-run-plugin.yml) | `run-v*` tag | Run plugin GitHub Release only (does **not** publish to Store) |
