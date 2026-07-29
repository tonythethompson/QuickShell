# Persistence (as-built)

How workspaces survive restarts: layout file, atomic writes, undo, import/export, legacy migration.

## Owner

`QuickShell.Core/Services/ShortcutRepository.cs` implementing `IShortcutRepository`.

Default path: `%LOCALAPPDATA%\QuickShell\shortcuts.json`.

## Layout model

On disk is not “only shortcuts” — it is a **layout**:

| Kind | Meaning |
|------|---------|
| **Shortcut** | `TerminalShortcut` (workspace) |
| **Separator** | Section header (`Type: "separator"`, optional `Title`) |

In memory:

- `_layout` — ordered entries (source of truth for order/sections)
- `_shortcuts` / by-name / by-id indexes — projections
- `_lastGoodLayout` — last successful parse (corruption shield)
- Undo/redo stacks — up to **25** layout snapshots

`GetById` / `GetByName` return **clones**. `*ReadOnly` returns live references — do not mutate casually.

## File format

**Write** always:

```json
{
  "version": 1,
  "entries": [ /* shortcuts + separators */ ]
}
```

`PersistenceVersion.Current = 1` (`PersistenceVersion.cs`). Serialize/parse: `ShortcutLayoutJson`.

Workspace trust metadata is local-persistence-only and is documented in [trust-model.md](./trust-model.md). Portable export omits it; direct legacy shortcut JSON remains readable and migrates to trusted metadata.

**Read** accepts:

| Root | Behavior |
|------|----------|
| JSON **array** (legacy v0) | Parse entries |
| **Object** with `entries` | Envelope; `version > Current` → reject |
| Invalid / empty / oversize | Fail → restore last good / empty |

Limits: max ~**2 MB**, max shortcut count via `ShortcutValidation.MaxShortcutCount`. Valid shortcut: non-empty **Name** + **Directory**.

JSON via source-generated `QuickShellJsonContext` (AOT-friendly).

## Load path

```
EnsureConfigExists
  create config dir
  if missing/empty: try .bak, then legacy TerminalShortcutsCmdPal path, else empty file
EnsureLoaded
  skip if mtime unchanged
  parse → NormalizeLayout (drop invalid, NormalizeShortcut, assign ids)
  ApplyLoadedLayout → TryMigrateLegacyWorkspaces
  if ids assigned → write heal
on failure → RestoreLastGoodLayout
```

## Write path

### Immediate (`SaveLayoutLocked`)

Used by Upsert, Delete, pin, Undo/Redo, Import, Reset:

1. Normalize + serialize envelope  
2. **`WriteLayoutAtomic`**  
3. Update indexes, `_lastGoodLayout`, mtime  
4. Raise **`WorkspacesChanged`**

### Atomic writer

`AtomicFileWriter` / `IAtomicFileWriter`:

```
write path.tmp → File.Replace(tmp, path, path.bak) or Move
Global\QuickShell_shortcuts_json mutex (cross-process)
```

In-process API serialized with `SemaphoreSlim`.

### Debounced (`MarkUsed` only)

`LastUsedUtc` updates schedule a **2s** timer flush; structural mutators **cancel** pending MarkUsed first. `FlushPendingWrites` / dispose flush pending.

## Mutations

Typical pattern:

```
WithLock {
  EnsureLoaded(); CancelPendingPersist();
  previous = clone(layout); mutate clone;
  RecordHistoryLayoutLocked(previous, next);
  SaveLayoutLocked(next);
}
```

**Upsert** preserves Id / pin / LastUsedUtc on replace; assigns new Guid for new workspaces; enforces unique names (case-insensitive).

## Import / export

| Op | Behavior |
|----|----------|
| **Export** | Current layout JSON to user path |
| **Import read** | Same parser as main store |
| **Merge** | Append; rename on name conflict; history + save |
| **Replace** | New layout from file; history + save |
| **Conflicts** | UI (`ImportConflictPage`) when names collide |

Import is **undoable** as one layout history step.

## Legacy migration

On load, `WorkspaceLegacyMigration`:

1. If `%LOCALAPPDATA%\QuickShell\workspaces.json` exists  
2. Parse `WorkspaceDiskRecord` list → normalize → `TerminalShortcut`  
3. Merge (rename collisions) + save  
4. Archive to `workspaces.json.migrated`

Also first-time import from `shortcuts.json.bak` or old product path `TerminalShortcutsCmdPal\shortcuts.json`.

## Sibling stores

| File | Role |
|------|------|
| `shortcut-edit-draft.json` | Form draft for **edit** (see [forms.md](./forms.md)) |
| `worktree-branch-targets.json` | Git targets (atomic writer; not in workspace export) |
| `settings.json` | Host preferences (not `ShortcutRepository`) |

## Raycast

`QuickShellStorage` (`QuickShell.Raycast/src/lib/storage.ts`) is the Raycast persistence spine. It mirrors desktop layout / undo / recent-write debounce over the Raycast `LocalStorage` API and **does not** share `%LOCALAPPDATA%\QuickShell\` unless the user imports or exports JSON.

| Key (`schema.ts`) | Owner | Role |
|-------------------|-------|------|
| `quickshell-data` (`STORAGE_KEY`) | `QuickShellStorage` | Live `StoredData` blob (workspaces, layout, security, branch targets, settings mirror) |
| `quickshell-data.bak` (`BACKUP_STORAGE_KEY`) | `QuickShellStorage` | Durable reset-all snapshot; Raycast-local only (not the desktop `.bak` beside `shortcuts.json`) |

**Mutation serialization.** Public mutators (save, upsert/delete, pin/reorder, import, reset/restore, trust, flush of debounced recent writes, …) run through an in-process write queue (`withWriteLock`). Concurrent Raycast commands cannot silently clobber each other with a last-writer-wins race. Nested composition inside a held lock calls private `saveUnlocked` / `flushRecentWritesUnlocked` so callers always queue at the public boundary.

**Reset all / restore.** `resetAll()` writes the current cache to `BACKUP_STORAGE_KEY`, then clears workspaces / layout / security / branch targets while preserving settings, and records an undo snapshot. Recovery:

1. **In-session:** Undo (same as other layout mutations; lost if the extension process restarts).
2. **After restart:** `restoreFromBackup()` reloads the durable backup key into the live store. Corrupt backup JSON is discarded (key cleared) with a clear message rather than hard-failing forever.

UI: Transfer actions on the workspaces hub (`open-workspace.tsx`) — confirmed **Reset All Workspaces…** and **Restore Backup…** when a backup exists.

## Key files

| File | Role |
|------|------|
| `ShortcutRepository.cs` | Load/save/undo/import |
| `ShortcutLayoutJson.cs` | Parse/serialize |
| `AtomicFileWriter.cs` | Temp + replace |
| `WorkspaceLegacyMigration.cs` | Old workspaces.json |
| `ShortcutDraftStore.cs` | Edit drafts |
| `WorktreeBranchTargetStore.cs` | Branch targets |

## Tests

`ShortcutCorruptionRecoveryTests`, `ShortcutImportExportTests`, `ShortcutPersistenceMigrationTests`, `ShortcutLayoutEnvelopeTests`, `ShortcutRepositoryWorkspacesChangedTests`, `AtomicFileWriterTests`, etc.

## Gotchas

1. Layout is king; arrays/indexes are projections.  
2. MarkUsed can drop if process dies within debounce.  
3. Separators only round-trip via full layout export/import.  
4. Settings and branch targets are **not** in workspace export.  
5. Proposal doc 0002 may describe gaps already fixed — verify code.

## Related

- [forms.md](./forms.md) — draft file + form undo  
- [overview.md](./overview.md) — data directory map  
