# QuickShell Command Palette Deep-Link Schema

QuickShell uses opaque string IDs to let the Windows Command Palette (and future hosts) navigate pages and invoke commands. This document defines the supported ID formats. All construction and parsing is centralized in `CommandDescriptor` and `CommandIdParser`.

## Well-known IDs

| Command | ID | `CommandKind` |
|---|---|---|
| Settings | `com.quickshell.settings` | `OpenSettings` |
| Import conflict | `com.quickshell.import-conflict` | `ImportConflict` |
| Pending shortcut edit | `com.quickshell.pending-shortcut-edit` | `PendingShortcutEdit` |
| Discover git repos | `com.quickshell.discover-git-repos` | `DiscoverGitRepos` |
| Create workspace | `com.quickshell.shortcut-form.create` | `CreateWorkspace` |

## Prefixed IDs

IDs below are built from stable prefixes followed by arguments. Stable workspace/launch IDs are 32-character, lowercase, hex-only strings (no dashes), matching `CommandDescriptor.IsStableId`.

| Kind | Format | Example |
|---|---|---|
| `OpenWorkspace` | `com.quickshell.shortcut.open.<workspaceId>` | `com.quickshell.shortcut.open.a1b2...` |
| `OpenWorkspace` (admin) | `com.quickshell.shortcut.open.<workspaceId>.admin` | `com.quickshell.shortcut.open.a1b2...admin` |
| `OpenWorkspace` (standard) | `com.quickshell.shortcut.open.<workspaceId>.standard` | `com.quickshell.shortcut.open.a1b2...standard` |
| `OpenLaunch` | `com.quickshell.shortcut.open.<workspaceId>.launch.<launchId>` | `com.quickshell.shortcut.open.a1b2...launch.c3d4...` |
| `OpenLaunch` (admin/standard) | append `.admin` or `.standard` to the launch ID segment | `com.quickshell.shortcut.open.a1b2...launch.c3d4...admin` |
| `DiscoverCreateWorkspace` | `com.quickshell.discover.create.<hexUtf8Directory>` | directory is hex-UTF8 encoded and normalized via `Path.GetFullPath` |
| `WorkspaceStatus` | `com.quickshell.workspace-status.<workspaceId>` | |
| `WorktreeBranchPicker` | `com.quickshell.worktree-branch.picker.page.<workspaceId>` | |
| `WorktreeBranchSelect` | `com.quickshell.worktree-branch.select.<workspaceId>.<branch>` | branch may contain additional dots |
| `WorktreeBranchClear` | `com.quickshell.worktree-branch.clear.<workspaceId>` | |

The `...` segments in the examples are illustrative placeholders for the 32-character stable IDs; they are not part of the real ID.

### Variant suffixes

`.admin` and `.standard` are stripped during parsing and recorded via command-specific constructors, not the `CommandDescriptor` payload fields. When both `runAsAdmin` and `runAsStandard` are supplied, the suffix resolves to `.admin`; callers should not pass both flags as `true`, and `CommandDescriptor` treats it as a programming error in debug builds.

## In-page command IDs

The following IDs are used only inside CmdPal context menus and are not parsed as deep-links.

| Kind | Format |
|---|---|
| `FavoriteToggle` | `com.quickshell.shortcut.favorite.<hexUtf8Name>` |
| `FavoriteMove` | `com.quickshell.shortcut.move.<workspaceId>.<moveKind>` |

`FavoriteToggle` encodes the original workspace name so the ID is stable when the workspace has a legacy name-based key.

## Encoding helpers

- `EncodeNameKey` / `TryDecodeHexUtf8`: converts a UTF-8 string to lowercase hex and back.
- `EncodeDirectoryKey`: trims trailing directory separators, normalizes with `Path.GetFullPath` (best-effort), then hex-encodes.
- `IsStableId`: validates a 32-character lowercase hex string.
- `TryDecodeLegacyNameKey`: decodes a hex-UTF8 name key unless it is already a stable ID.

## Parsing precedence

`CommandIdParser.TryParse` checks in this order to avoid one pattern swallowing another:

1. Well-known singleton IDs.
2. `DiscoverCreateWorkspace` prefix.
3. `DiscoverGitRepos`.
4. Worktree branch prefixes (`clear`, `select`, `picker`).
5. `WorkspaceStatus`.
6. `OpenLaunch` (must contain `.launch.`).
7. `OpenWorkspace`.

`OpenLaunch` is checked before `OpenWorkspace` because an open-launch ID otherwise starts with the open prefix.
