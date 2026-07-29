# Quick Shell release smoke matrix

A compact, manual test pass to run for **every release candidate**, on real
hardware/VMs — not a substitute for automated tests, but automated tests
can't catch host-discovery, packaging, or upgrade-in-place regressions.
See [COMPATIBILITY_MATRIX.md](COMPATIBILITY_MATRIX.md) for the install-route
side of this (Store vs. WinGet vs. GitHub EXE); this doc is the
feature-behavior side.

Record results per RC as: `RC build | date | pass/fail | notes`. A single
"looks fine" pass is not a record — write down what you actually did if a row
fails, so the next RC doesn't repeat the investigation from scratch.

## Matrix

| Scenario | Minimum pass condition |
| --- | --- |
| Clean Windows 11 x64 | Install, reload CmdPal, create workspace, launch |
| Clean Windows 11 ARM64 | Same as above, on real ARM64 hardware or an ARM64 VM — do not assume x64 parity |
| Existing Quick Shell user upgrade | Install the previous public release (`git tag` for the version before the current RC), create workspaces/favorites/recents, then upgrade in place. All of it survives; settings.json values are preserved, not reset to defaults |
| **Windows Terminal default profile** | Launches in the correct working directory |
| **Custom Windows Terminal profile — MANDATORY** | Set a specific (non-"Default profile for this app") terminal profile as the default, then launch a shortcut. It must launch with **that** profile, not silently reset to Windows Terminal's default. See "Why this one is mandatory" below |
| WSL distro | Correct distro and working directory |
| PowerShell / pwsh / cmd | Correct executable, arguments, and quoting — verify with a directory path and a shortcut name that both contain spaces |
| Multi-launch workspace | Launch order matches the configured sequence; disabled entries are skipped; enabled entries all fire |
| Elevated launch | UAC prompt appears and elevated launch succeeds; the non-elevated launch path for the same shortcut still works afterward |
| Import/export | Merge keeps existing shortcuts and adds new ones without duplicating; Replace fully replaces without leaving orphaned entries; test both with an in-progress edit draft pending (see `PendingShortcutEditForm`) to confirm import doesn't corrupt it |
| Bad/malformed JSON | Manually corrupt `%LOCALAPPDATA%\QuickShell\shortcuts.json` (truncate it mid-write, or drop a stray brace). App must recover using `shortcuts.json.bak`, show an actionable message, not crash, and not silently discard the corruption without telling the user |
| Git repo discovery | Trigger discovery against a directory tree with 2000+ subdirectories. Scan must respect `MaxRepos`/`MaxDirectoriesScanned`, a manual refresh/cancel must not hang the UI, and CmdPal must stay responsive to other queries while discovery runs in the background |

## Why the custom-profile case is mandatory

This isn't a generic "be thorough" note — it's tracking a real regression.
The [performance-audit PR](docs/performance-audit.md) deferred default-profile
choice enumeration in `QuickShellSettingsManager` for startup performance, and
that change **broke validation** so a user's custom default terminal profile
got silently reset to "Default profile for this app" on every shortcut
launch, until they happened to open the Terminal Defaults settings form
(which repopulates the full choice list as a side effect). That specific bug
is fixed (`QuickShellSettingsManager.cs` now validates against the live
terminal catalog, not a stale ctor-seeded list), but the smoke matrix that
would have caught it before merge was marked incomplete. Don't let this
regress again — this row stays mandatory for every RC, not "when there's
time."

## Notes on the other rows

- **Elevated launch**: exercises `RunAsAdmin` on both the shortcut level and
  individual multi-launch entries — test both, they're independent flags.
- **Import/export**: the resolution options are literally "Merge" (keep
  yours, add new, rename duplicates) or "Replace all" (file only) per the
  README — confirm the actual behavior still matches that description; a
  wording drift there usually means a logic drift too.
- **Bad/malformed JSON**: two distinct, independently-tested recovery layers
  exist. (1) `EnsureConfigExists()` runs once per process; if shortcuts.json
  has no valid content, it automatically falls back to `shortcuts.json.bak`
  and then the legacy `%LOCALAPPDATA%\TerminalShortcutsCmdPal\shortcuts.json`
  path, then writes the recovered content back as the current file — this
  covers corruption discovered on a fresh app start. (2) `RestoreLastGoodLayout()`
  falls back to the in-memory layout this process already validated, for
  corruption that happens mid-session (e.g. an external process/manual edit
  clobbers the file while Quick Shell is still running). See
  `ShortcutCorruptionRecoveryTests.cs` for both paths under test. This row is
  about confirming both actually fire end-to-end through the real file system
  and UI, not just in a unit test with an in-memory stream.
- **Git repo discovery**: `GitRepoIndex`/`GitRepoDiscovery` were reworked to
  avoid holding a lock for the full scan duration and to parallelize with
  bounded workers — this row is the manual confirmation that the fix actually
  holds up against a real wide/deep directory tree, since the automated tests
  exercise the shape of the algorithm, not real disk I/O timing.

## What this matrix intentionally does not cover

Full UI snapshot/visual regression testing. The risky surface here is
persistence, host discovery, process launch, and packaging — not pixel
layout. Don't expand this matrix with visual checks until the rows above are
reliably green across releases.
