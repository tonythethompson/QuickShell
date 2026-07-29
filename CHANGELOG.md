# Changelog

## [0.2.4.0] - 2026-07-29

### Changed

- Much smaller Command Palette package: Release MSIX is roughly ~30 MB after trim (down from ~126 MB) by dropping WinForms and stripping unused framework code, so installs and updates download less.
- Snappier search and list refresh by cutting allocations on hot paths (task search tokens, PATH lookups, display text, health-check parsing).
- Faster workspace suggestions and home-row enrichment with less redundant work while you browse and launch.
- Quicker terminal profile icon loading via Windows Terminal fragment discovery, icon resizing, and skip-reparse when fragment files have not changed.
- Smoother status and branch-picker refresh when drives come back online or a load fails (retryable errors, no stuck duplicate loads).

### Fixed

- Profile icons from Windows Terminal fragments (including relative paths and fragment `updates`) resolve more reliably.
- Blank `.ico` sources are handled without breaking valid PNG icons.
- Offline or repaired workspace folders re-probe after a short TTL instead of staying stuck until restart.

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
