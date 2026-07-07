# QuickShell Changelog

## [Lifecycle, manifest, and platform guards] - {PR_MERGE_DATE}

- Add Windows-only guard views and load-error toasts across commands
- Type commands with `LaunchProps`; support `fallbackText` and create `directory` argument
- Add extension-level Store keywords; rename changelog for Version History
- Document deeplinks and manifest conventions in README

## [Parity, performance, and Raycast UX] - {PR_MERGE_DATE}

- Add discover git repos, import/export, undo/redo, companion app, dev server links, and run-as-standard
- Memoize workspace health for list rendering and debounce recent-write persistence
- Use Raycast `showFailureToast`, `withCache`, `updateCommandMetadata`, and `Action.SubmitForm`
- Add command subtitles and root-search keywords for Store discoverability

## [Workspace form UX and home keyword search] - {PR_MERGE_DATE}

- Directory-first workspace form with auto-fill from project layout
- Machine-discovered Windows Terminal profiles and multi-command launches
- Home keyword search priority in Open Workspace

## [Initial Raycast extension] - {PR_MERGE_DATE}

- Open, create, edit workspace commands with Windows terminal launch support
- Favorites, recents, task search, and QuickShell settings
