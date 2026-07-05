# Quick Shell distribution compatibility matrix

Quick Shell ships through three install routes, and each one carries a different
integration story for **Command Palette (CmdPal)** and **PowerToys Run**. This
doc is the single source of truth for what each route is supposed to do. When
that doesn't hold, it's a bug — see [README.md § Troubleshooting](README.md#troubleshooting)
for the specific failure modes users have already hit.

Fill in **Verify** cells from an actual clean-VM run before each release; don't
assume unchanged behavior carries over from the last version. Record the
result inline (date + build + pass/fail) rather than leaving a bare checkmark,
so a regression is traceable to the release that introduced it.

## Install route support matrix

| Install route | CmdPal | PowerToys Run | Upgrade behavior | Uninstall behavior |
|---|---|---|---|---|
| Microsoft Store | Required (this is the only thing the Store package installs) | Optional — user must separately grab the `QuickShell.Run-*.zip` from GitHub Releases and drop the plugin in manually; Store package does **not** include it | Verify | Verify |
| WinGet | Required | Required — bundled into the same Inno Setup installer as CmdPal; both register on one install | Verify | Verify |
| GitHub installer (EXE) | Required | Required — same installer artifact as WinGet (`winget/tonythethompson.QuickShell.installer.yaml` points at the same GitHub Release asset) | Verify | Verify |

Source of truth for the route descriptions: [docs/install.md](docs/install.md).
WinGet and the GitHub EXE installer are **the same binary** distributed two
ways, not two independently-built packages — a bug in one is a bug in both.

## What "Verify" means, concretely

For each route, on a clean Windows 11 VM (see the ARM64 row in
[RELEASE_SMOKE_MATRIX.md](RELEASE_SMOKE_MATRIX.md) too — architecture matters
for Store/MSIX vs. the Inno installer), confirm:

| Check | Pass condition |
|---|---|
| Install completes | No error dialog; correct entries appear under Settings → Apps |
| CmdPal discovers the extension | Search "Quick Shell" in Command Palette without running "Reload Command Palette Extension" first — if it doesn't show, that's the flow the README's "Extension missing after install" note exists for |
| PowerToys Run discovers the plugin (WinGet/GitHub only) | `qs` prefix works in PowerToys Run (Alt+Space) without a PowerToys restart being *required* — if it needs a restart, the install docs already say so; confirm that's still true and not worse |
| No duplicate entries | Exactly one "Quick Shell" listing in Settings → Apps and in Command Palette's extension list — this is the exact failure the README's "Duplicate or broken Quick Shell in Windows Settings" section covers |
| Upgrade from previous public version | Install the last public release (see `git tag` for the prior version, e.g. one before the current release tag), create a workspace, then upgrade in place. Workspaces, favorites, recents, and settings must survive — see [RELEASE_SMOKE_MATRIX.md](RELEASE_SMOKE_MATRIX.md) row "Existing Quick Shell user upgrade" |
| Old extension version doesn't survive upgrade | After upgrading, Command Palette should show only the new version's behavior/branding — no stale extension instance still registered alongside the new one |
| Uninstall removes binaries, keeps user data by default | After uninstall: install directory gone, Settings → Apps entry gone, but `%LOCALAPPDATA%\QuickShell\shortcuts.json` (and `.bak`) still present unless the user deliberately deleted it. This is deliberate — don't "fix" it into deleting user data on uninstall without a separate, explicit decision |

## Known product-risk areas (already documented, not hypothetical)

These are called out because [README.md § Troubleshooting](README.md#troubleshooting)
already has entries for them — i.e., real users have hit them:

- **Extension missing after install** — CmdPal doesn't discover Quick Shell
  until "Reload Command Palette Extension" is run. Every route above should
  be tested for whether this manual step is actually still necessary.
- **Duplicate or broken Quick Shell in Windows Settings** — an old installer
  left behind alongside a new one. Directly relevant to the "upgrade" and
  "old extension version" checks above.
- **Shortcuts disappeared after an update** — data migration risk. Covered in
  depth by the persistence migration test plan (see the release smoke matrix
  and the upcoming persistence-migration test suite).

## Open questions this matrix does not answer yet

- Whether the Microsoft Store build and the WinGet/GitHub Inno build can
  coexist on the same machine without producing a duplicate Command Palette
  registration (a user who installs via Store, then later grabs the GitHub
  EXE for the Run plugin, is exactly the scenario the "Optional ZIP/manual"
  cell above assumes — it needs an explicit clean-VM pass, not an assumption).
- ARM64 parity for the Store route specifically (the WinGet/GitHub manifest
  already lists separate x64/arm64 installers; confirm the Store package is
  also architecture-neutral or has both).
