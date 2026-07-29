# Changelog

All notable changes to Quick Shell (CmdPal, Run, and shared Core) are documented here.

Format follows [Keep a Changelog](https://keepachangelog.com/). Versions match the MSIX / `AppVersion` four-part number (for example `0.2.3.0`).

Write for people installing from GitHub, WinGet, or the Microsoft Store: user-visible behavior, not PR titles or file names. Prefer **Added** / **Fixed** / **Changed** / **Removed**. Keep each release short enough for Store "What's new" (about 5–15 bullets).

While work is in progress, add bullets under **Unreleased**. Before tagging `vX.Y.Z.W`, move that content under a dated `## [X.Y.Z.W]` heading. Release CI fails if that section is missing or empty.

## [Unreleased]

## [0.2.3.0] - 2026-07-22

### Added
- Workspace trust model so untrusted folders are handled explicitly before launch.
- Faster first paint and launch via cached workspace snapshots, deferred icon work, and a revision-keyed launch plan cache.
- Richer root Command Palette search backed by a snapshot index.

### Fixed
- Home-pin context menus and list rendering edge cases.
- Folder picker no longer leaves an orphaned dialog after a timeout.
- "Open to Directory" suggestion pill applies the chosen path correctly.
- Bound shortcut-store lock waits and clearer logging when a lock is held too long.

### Changed
- Core services use real DI instead of static locators for launch, health, suggestions, and project analysis.
- Local UI strings for attention states and branch switching.
- Installer, CI, and packaging reliability for CmdPal and Run release artifacts.
