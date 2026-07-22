# Forms, drafts, and undo (as-built)

In-palette create/edit (CmdPal Adaptive Cards), disk drafts, and the **two** undo stacks.

## Surfaces

| Path | Implementation |
|------|----------------|
| Create / Edit / Duplicate | `ShortcutFormPage` + `ShortcutForm` (`QuickShell/Pages/`) over Core `IWorkspaceEditor` |
| Pending edit resume | `PendingShortcutEditPage` |
| Shared edit session | `QuickShell.Core/Services/WorkspaceEditor/` (`IWorkspaceEditor`, factory via `AddQuickShellCore`) |
| Run host editor | `QuickShell.Run/ShortcutWorkspaceEditorWindow.cs` (one-page WPF binder over the same Core session; 680×min 560) |
| Raycast | React form components + storage history (separate stack) |

## Form model

```
ShortcutFormPage / ShortcutWorkspaceEditorWindow
  └─ IWorkspaceEditorFactory → IWorkspaceEditor (Core session: draft, dirty, undo, save)
  └─ CmdPal: ShortcutForm (FormContent; thin event mapper)
        └─ IShortcutFormViewBuilder → TemplateJson / DataJson
  └─ Run: ScrollViewer + WPF controls (TryApplyHostFields)
        FormEditHistory lives on WorkspaceEditor (launch-row snapshots)
```

`IShortcutFormViewBuilder` (`ShortcutFormViewBuilder`) owns Adaptive Card construction via `ShortcutFormTemplateJson` (+ cache). `ShortcutForm` maps submit actions to `IWorkspaceEditor` and applies builder output. `WorkspaceEditor` lives in `QuickShell.Core/Services/WorkspaceEditor/` behind `IWorkspaceEditor`. Shared launch drafts have an explicit `LaunchRowKind`: `Command` or `OpenInTerminal`. CmdPal renders only real launches and supports a true zero-row state with `Add command` and `Open in terminal` actions; each row also has terminal profile, Admin, and remove controls. The Run host retains three visible WPF slots locally using nonpersistent `IsEditorPlaceholder` rows. Workspace-level “Always run as administrator” was removed from the CmdPal form; legacy `TerminalShortcut.RunAsAdmin` mirrors the first launch row on normalize.

Browse/Paste folder on the form fills name (if unset), repo URL, and Dev Server URL when empty. It does **not** auto-seed launch commands or companion apps. Suggestion pills add commands; companion presets stay user-chosen. Discover create seeds via `WorkspaceSeedFactory` (see [intelligence.md](./intelligence.md), [companions.md](./companions.md)).

Suggestion pills on the form call into [intelligence.md](./intelligence.md); companion fields into [companions.md](./companions.md).

## Adaptive Card loop

1. Host shows card from `IShortcutFormViewBuilder` (template + data JSON).  
2. Submit → `WorkspaceFormActionParser` → `IWorkspaceEditor.TryApplyInputs` / action methods.  
3. Editor raises `Changed` → form rebuilds via the view builder → optional disk draft persist.

## Two undo stacks

### 1) Form-local (`FormEditHistory`)

- Generic stack depth **25** (`FormEditHistory.cs`).  
- Snapshot = launch **rows** (both `OpenInTerminal` and command kinds) + expand-pills flag (not every name/path keystroke).
- Pushed before pill add, remove row, expand/collapse pills.
- `TryUndoEdit` / `TryRedoEdit` on the form.

### 2) Repository layout undo

- Full workspace list mutations (`ShortcutRepository` undo/redo).  
- See [persistence.md](./persistence.md).

### Combined CmdPal command

`WorkspaceFormUndoCommand` / Redo:

1. Try form undo first.  
2. If empty and callback wired → `Shortcuts.Undo()` / `Redo()` + list reload.

So Ctrl+Z on a form can undo list ops if the form history is empty.

## Disk draft (edit only)

| | |
|--|--|
| File | `shortcut-edit-draft.json` |
| API | `IDraftStore` / `ShortcutDraftStore` |
| Writes | Atomic via `IAtomicFileWriter` |

**Persisted only when editing** (`OriginalName` set). Create mode does not write this file.

`SaveIfDirty(editKey, draft, baseline, …)` — if equal to baseline, clear matching draft; else write.

Restore on open edit for matching name; weak event on `Drafts.Cleared` resets open form to saved workspace.

Pending UI can `TryCommitPending` → `ShortcutFormSave.TrySave` without reopening the full form.

## Save path

```
SaveCurrentDraft
  → ShortcutFormSave.TrySave(...)
       validate → build TerminalShortcut
       Shortcuts.Upsert(..., originalName)
       Drafts.Clear()
       onSaved()  // reload list
```

Blank `Command` drafts and Run-only WPF placeholders are trimmed. An explicit `OpenInTerminal` row persists as a launch whose on-disk `Command` remains `null`; saving with no real launch stays in the editor with `Add at least one launch.` Layout undo records the Upsert.

## Cancel / discard

```
Cancel
  clean → Clear draft, leave
  dirty → PersistEditDraftIfNeeded (edit), show discard Adaptive Card
Discard → clear draft and leave
Save from prompt → SaveCurrentDraft
```

## Key files

| File | Role |
|------|------|
| `Pages/ShortcutFormPage.cs` | Page + form submit/undo/draft |
| `ShortcutFormTemplateJson.cs` | Adaptive Card templates |
| `FormEditHistory.cs` | Form undo stack |
| `ShortcutDraftStore.cs` / `IDraftStore` | Disk draft |
| `ShortcutFormSave` (form draft store area) | Validate + Upsert |
| `WorkspaceFormUndoCommands.cs` | Form then list undo |
| `LaunchRowListEditor.cs` | Rows / ApplyPill / trim |

## Gotchas

1. Form undo ≠ full field history.  
2. One draft file / one pending edit key.  
3. Create abandon is not crash-recovered to disk.  
4. Form Ctrl+Z may hit repository undo when form history is empty.

## Related

- [persistence.md](./persistence.md) — Upsert / layout undo  
- [intelligence.md](./intelligence.md) — pills  
- [companions.md](./companions.md) — companion form fields  
