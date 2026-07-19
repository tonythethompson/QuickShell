# Command Palette surface (as-built)

How the CmdPal extension is structured: provider, home list, search, deep links, fallback.

## Extension entry

| Type | Role |
|------|------|
| `QuickShellExtension` | `IExtension` — exposes commands provider |
| `QuickShellCommandsProvider` | Composition root for host DI, top-level command, fallbacks, dispose |

Provider ctor (high level):

1. Settings manager  
2. `ServiceCollection` + `AddQuickShellHost` → Core + routing  
3. Bind `QuickShellServices`  
4. Home `QuickShellPage`  
5. Top-level `CommandItem` (create, settings, undo/redo context)  
6. Fallback registration  
7. Best-effort `GitRepoIndex.Prewarm`

`GetCommandItem(id)` → `ICommandRouter.TryHandle` then base.

## Home list (`QuickShellPage`)

`DynamicListPage` with debounced search (`SearchDebouncer`).

Typical structure (empty query):

1. **Create workspace**  
2. **Discover git repos**  
3. **Quick Shell settings**  
4. Sections: **Favorites**, **Recent** (if enabled), **Workspaces**  

Sections via `SectionListItems` / `ShortcutListItems`. Badges from `WorkspaceStatusService` + `ShortcutDisplayTags`. Context menus from `ShortcutContextCommands` (open, edit, pin, status, companion, admin, …).

Search non-empty:

- Workspace name / path / abbreviation matching (`ShortcutRepository.Search`)  
- **Task actions** (`SearchTaskActions`) — match launch label/command (e.g. `dev`)  

Empty state → create command + settings.

## Deep links / command routing

The deep-link contract is owned by `CommandDescriptor` (`QuickShell.Core/Services/CommandDescriptor.cs`). It defines all stable IDs, prefixes, suffixes, encoding helpers, and the nested parser. `CommandIdParser` is a thin adapter that delegates to `CommandDescriptor.Parser`. See the frozen contract in [deep-link-schema.md](./deep-link-schema.md).

Kinds (see `CommandKind.cs`):

| Kind | Purpose |
|------|---------|
| OpenWorkspace / OpenLaunch | Run full workspace or one row |
| CreateWorkspace / DiscoverCreate | Forms |
| DiscoverGitRepos | Discover page |
| WorkspaceStatus / WorktreeBranch* | Status + branch UI |
| OpenSettings / ImportConflict / PendingShortcutEdit | Settings flows |
| FavoriteToggle / FavoriteMove | In-page context-menu IDs only |

`CommandRouter` maps kind → `ICommandItemHandler` (DI-registered handlers in `Services/CommandRouting/`).

Proposal **0003** is largely about hardening this path; router + descriptors already exist.

## Fallback (root palette search)

`QuickShellFallback` + `QuickShellFallbackPage`:

- Appears in **root** Command Palette results without opening the extension first.  
- Uses home keywords / abbreviations (`SearchForRootPalette`).  
- Suppresses noise queries (see `ShouldSuppress`).  
- Can surface discover/create paths depending on query.

Reload extension invalidates git index and clears fallback cache.

## Navigation helpers

`QuickShellNavigation` — StayOpen, GoToSettings, page ids, toast-style messages without always dismissing.

## Keyboard shortcuts

`QuickShellKeyboardShortcuts` — Ctrl+N create, Ctrl+E edit, Ctrl+F favorite, undo/redo, admin open, etc. Wired on list/context items.

## Key files

| Area | Files |
|------|--------|
| Provider | `QuickShellCommandsProvider.cs`, `QuickShell.cs` |
| Home | `Pages/QuickShellPage.cs`, `Services/ShortcutListItems.cs`, `SectionListItems.cs` |
| Routing | `CommandRouter.cs`, `CommandIdParser.cs`, `CommandKind.cs`, `CommandDescriptor.cs`, `CommandRouting/*` |
| Fallback | `QuickShellFallback.cs`, `Pages/QuickShellFallbackPage.cs` |
| Context | `ShortcutContextCommands.cs` |
| Status | `Pages/WorkspaceStatusPage.cs` |

## Related

- [forms.md](./forms.md) — create/edit pages  
- [launch.md](./launch.md) — open commands  
- [git-and-discover.md](./git-and-discover.md) — discover + worktrees  
- [settings.md](./settings.md) — settings page  
