# QuickShell Command Palette Deep-Link Schema

This document is the frozen contract for all string IDs that the Windows Command Palette (and future hosts) use to request pages and commands from QuickShell. All construction, parsing, decoding, and validation helpers live in `CommandDescriptor` (`QuickShell.Core/Services/CommandDescriptor.cs`). `CommandIdParser` is a thin adapter that delegates to `CommandDescriptor.Parser`.

Changing any ID prefix, separator, suffix, or encoding rule without updating this document and the round-trip tests is a breaking change.

## Provider and host IDs

These IDs identify the extension, its home page, and its fallback command. They are not routed through `CommandDescriptor.Parser`.

| Purpose | Constant / factory | Raw ID |
|---|---|---|
| Provider | `CommandDescriptor.ProviderId` | `com.quickshell` |
| Home page | `CommandDescriptor.HomePageId` | `com.quickshell.home` |
| Fallback command | `CommandDescriptor.FallbackCommandId` | `com.quickshell.fallback` |

## Well-known deep-link IDs

These IDs are exact string matches and resolve to a single `CommandKind`.

| `CommandKind` | Raw ID | Backward compatibility |
|---|---|---|
| `OpenSettings` | `com.quickshell.settings` | Frozen since v0. |
| `ImportConflict` | `com.quickshell.import-conflict` | Frozen since v0. |
| `PendingShortcutEdit` | `com.quickshell.pending-shortcut-edit` | Frozen since v0. |
| `DiscoverGitRepos` | `com.quickshell.discover-git-repos` | Frozen since v0. |
| `CreateWorkspace` | `com.quickshell.shortcut-form.create` | Frozen since v0. |

## Prefixed deep-link IDs

IDs below are built from stable prefixes followed by arguments. Workspace and launch IDs are expected to be 32-character, lowercase, hex-only strings (no dashes), matching `CommandDescriptor.IsStableId`. Callers must use `CommandDescriptor` factories; hand-built strings are not allowed.

| `CommandKind` | Format | Example | Payload encoding rules | Backward compatibility |
|---|---|---|---|---|
| `OpenWorkspace` | `com.quickshell.shortcut.open.<workspaceId>` | `com.quickshell.shortcut.open.a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4` | `workspaceId` may be a stable 32-char hex ID or a legacy name key. | Frozen since v0. |
| `OpenWorkspace` (admin) | `com.quickshell.shortcut.open.<workspaceId>.admin` | `com.quickshell.shortcut.open.a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4.admin` | Append `.admin` after the workspace ID. | Frozen since v0. |
| `OpenWorkspace` (standard) | `com.quickshell.shortcut.open.<workspaceId>.standard` | `com.quickshell.shortcut.open.a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4.standard` | Append `.standard` after the workspace ID. | Frozen since v0. |
| `OpenLaunch` | `com.quickshell.shortcut.open.<workspaceId>.launch.<launchId>` | `com.quickshell.shortcut.open.a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4.launch.c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6` | Both IDs must be 32-char hex. The `.launch.` separator is checked before the bare `OpenWorkspace` prefix so launch IDs are never swallowed. | Frozen since v0. |
| `OpenLaunch` (admin/standard) | append `.admin` or `.standard` to the launch ID segment | `com.quickshell.shortcut.open.a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4.launch.c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6.admin` | Variant suffix is stripped during parsing; admin wins if both are supplied. | Frozen since v0. |
| `DiscoverCreateWorkspace` | `com.quickshell.discover.create.<hexUtf8Directory>` | `com.quickshell.discover.create.633a5c74657374` | Directory is trimmed of trailing separators, normalized with `Path.GetFullPath` (best-effort), then UTF-8 hex-encoded in lowercase. | Frozen since v0. |
| `WorkspaceStatus` | `com.quickshell.workspace-status.<workspaceId>` | `com.quickshell.workspace-status.a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4` | `workspaceId` must be 32-char hex. | Frozen since v0. |
| `WorktreeBranchPicker` | `com.quickshell.worktree-branch.picker.page.<workspaceId>` | `com.quickshell.worktree-branch.picker.page.a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4` | `workspaceId` must be 32-char hex. | Frozen since v0. |
| `WorktreeBranchSelect` | `com.quickshell.worktree-branch.select.<workspaceId>.<branch>` | `com.quickshell.worktree-branch.select.a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4.main` | `workspaceId` must be 32-char hex. `branch` is everything after the first dot following the prefix and may contain additional dots. | Frozen since v0. |
| `WorktreeBranchClear` | `com.quickshell.worktree-branch.clear.<workspaceId>` | `com.quickshell.worktree-branch.clear.a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4` | `workspaceId` must be 32-char hex. | Frozen since v0. |

### Variant suffixes

`.admin` and `.standard` are stripped during parsing. If both `runAsAdmin` and `runAsStandard` are supplied, the suffix resolves to `.admin`. Callers should not pass both flags as `true`; `CommandDescriptor` logs a diagnostic and treats it as a programming error rather than crashing.

## In-page command IDs

The following IDs are used only inside CmdPal context menus. They are intentionally not parsed as external deep-links by `CommandDescriptor.Parser`.

| `CommandKind` | Format | Example | Payload encoding rules |
|---|---|---|---|
| `FavoriteToggle` | `com.quickshell.shortcut.favorite.<hexUtf8Name>` | `com.quickshell.shortcut.favorite.6d792d776f726b7370616365` | Workspace name is UTF-8 hex-encoded in lowercase so legacy name-based keys remain stable. |
| `FavoriteMove` | `com.quickshell.shortcut.move.<workspaceId>.<moveKind>` | `com.quickshell.shortcut.move.a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4.Up` | `workspaceId` may be a stable ID or hex-encoded legacy name. `moveKind` is one of `Up`, `Down`, `ToTop`, `ToBottom`. |

## Form and page IDs

These IDs identify Adaptive Card forms and content pages. They are not parsed as deep-links, but their prefixes are owned by `CommandDescriptor` so the schema is in one place.

| Purpose | Factory | Raw ID example |
|---|---|---|
| New workspace form | `CommandDescriptor.NewWorkspaceFormPageId()` | `com.quickshell.shortcut-form.create.<guid>` |
| Edit workspace form | `CommandDescriptor.EditWorkspaceFormPageId(id)` | `com.quickshell.shortcut-form.edit.<id>` |
| Duplicate workspace form | `CommandDescriptor.DuplicateWorkspaceFormPageId()` | `com.quickshell.shortcut-form.duplicate.<guid>` |
| Shortcut details form | `CommandDescriptor.ShortcutDetailsPageId()` | `com.quickshell.shortcut.details.<guid>` |

## Encoding helpers

- `EncodeNameKey` / `TryDecodeHexUtf8`: converts a UTF-8 string to lowercase hex and back.
- `EncodeDirectoryKey`: trims trailing directory separators, normalizes with `Path.GetFullPath` (best-effort), then hex-encodes.
- `IsStableId`: validates a 32-character lowercase hex string.
- `TryDecodeLegacyNameKey`: decodes a hex-UTF-8 name key unless it is already a stable ID.

## Parsing precedence

`CommandDescriptor.Parser.TryParse` checks IDs in this order to avoid one pattern swallowing another:

1. Well-known singleton IDs.
2. `DiscoverCreateWorkspace` prefix.
3. Worktree branch prefixes (`clear`, `select`, `picker`).
4. `WorkspaceStatus`.
5. `OpenLaunch` (must contain `.launch.`).
6. `OpenWorkspace`.

`OpenLaunch` is checked before `OpenWorkspace` because an open-launch ID otherwise starts with the open prefix.
