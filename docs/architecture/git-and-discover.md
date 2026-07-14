# Git worktrees and discover repos (as-built)

Git integration beyond “run a command”: branch targets, launch gate, status UI, and discovering local repos to add as workspaces.

## Pieces

| Concern | Types |
|---------|--------|
| Status (branch, dirty, detached) | `WorkspaceGitOperations` / `WorkspaceGitOperationsService` |
| Target branch per worktree | `WorktreeBranchTargetStore` → `worktree-branch-targets.json` |
| Launch switch / dirty block | `WorkspaceGitLaunchGate` |
| Status snapshot for UI | `WorkspaceStatusService` + git fields on snapshot |
| Discover scan | `GitRepoDiscovery` + `GitRepoIndex` cache |
| UI | Discover page, worktree branch picker, workspace status |

## Worktree branch targets

File: `%LOCALAPPDATA%\QuickShell\worktree-branch-targets.json`  
Key: worktree identity from `WorkspaceGitOperations.TryResolveWorktreeKey(directory)` (not workspace Id).

API:

- `GetTargetForDirectory` / `TrySetTargetForDirectory` / `ClearTargetForDirectory`  
- Atomic writes via `IAtomicFileWriter`  
- Test overrides for path/get/set  

**Not** included in workspace export.

## Launch gate

See [launch.md](./launch.md). Summary:

1. No target → proceed  
2. Already on branch → proceed  
3. Dirty + `blockDirtyBranchSwitch` → StayOpen  
4. Else `TrySwitchBranch`  

Setting: `blockDirtyBranchSwitch` in [settings.md](./settings.md).

## Workspace status UI

`WorkspaceStatusPage` / form:

- Health findings + git HEAD + target + mismatch  
- Commands: switch branch…, use current branch  
- List badges: `WorkspaceStatusSnapshot` Attention (Blocking / Warning / Branch) and Activity (running)

Capture caches ~10s; health portion often `includeGit: false` with git loaded separately.

## Discover git repos

### Scan (`GitRepoDiscovery`)

- Walks common roots (`Projects`, `dev`, `repos`, user profile, …) + extra roots from saved shortcuts  
- Caps: depth, dirs scanned, max repos  
- Skips `node_modules`, `bin`, `obj`, etc.  
- Candidate: directory, name, optional remote URL, **ProjectClassification**

### Index (`GitRepoIndex`)

- Process-wide cache (~10 min lifetime)  
- Prewarm from provider using roots derived from existing shortcuts  
- Search by query; invalidate on reload  
- Must marshal waiters to extension sync context when set (CmdPal)

### UI (`DiscoverGitReposPage`)

- List/search candidates  
- Add as workspace (create seed / discover-create deep link)  
- Can apply setup suggestions + companion detection when creating  

CmdPal home and create flows link here. Raycast: `discover-git-repos.tsx` + `git-repo-discovery.ts` (TS reimplementation of scan ideas).

## Branch picker

`WorktreeBranchPickerPage` + commands:

- Deep links: picker page, select branch, clear target (`QuickShellDeepLinkIds`)  
- Select path uses gate (`SelectTargetBranch`) so dirty policy applies  

## Key files

| File | Role |
|------|------|
| `WorkspaceGitOperations.cs` | git status / switch / worktree key |
| `WorktreeBranchTargetStore.cs` | persistence |
| `WorkspaceGitLaunchGate.cs` | pre-launch |
| `GitRepoDiscovery.cs` / `GitRepoIndex.cs` | discover + cache |
| `GitRepoSearchRoots.cs` | roots from shortcuts |
| `Pages/DiscoverGitReposPage.cs` | CmdPal UI |
| `Pages/WorktreeBranchPickerPage.cs` | branch UI |
| `Pages/WorkspaceStatusPage.cs` | status |

## Tests

`WorktreeBranchTests`, `GitRepoIndexCacheTests`, health/git override tests, etc.

## Gotchas

1. Target is per **folder/worktree**, shared if two workspaces point at same path.  
2. Discover is best-effort and capped — not a full-disk index.  
3. Prewarm failure is silent; discover still works cold.  
4. Raycast does not share `worktree-branch-targets.json` with desktop automatically.

## Related

- [launch.md](./launch.md) — gate in launch order  
- [intelligence.md](./intelligence.md) — classification on discover candidates  
- [settings.md](./settings.md) — dirty block  
