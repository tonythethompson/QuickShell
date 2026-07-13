# Host parity matrix (CmdPal / Run / Raycast)

**Tier 0.2 deliverable**  
**As of:** 2026-07-13  

Compares product capabilities across hosts. Use this before adding a feature on only one surface.

Legend:

| Symbol | Meaning |
|--------|---------|
| **Full** | Implemented with intended behavior |
| **Partial** | Present but thinner, different, or incomplete vs desktop Core |
| **No** | Not implemented |
| **N/A** | Not applicable to that host |
| **Shared** | Same on-disk store / Core library as another host |

Data stores:

| Store | CmdPal | Run | Raycast |
|-------|--------|-----|---------|
| Workspaces | `%LOCALAPPDATA%\QuickShell\shortcuts.json` | **Shared** with CmdPal | Raycast storage blob (separate) |
| Settings | `%LOCALAPPDATA%\QuickShell\settings.json` | **Shared** with CmdPal | In Raycast `StoredData.settings` (+ prefs) |
| Edit draft | `shortcut-edit-draft.json` | Via editor (not same draft file UX) | Form-local / storage patterns |
| Worktree branch targets | `worktree-branch-targets.json` | **Shared** (via Core on launch) | **No** dedicated file |

---

## Core product loop

| Capability | CmdPal | Run | Raycast | Notes |
|------------|--------|-----|---------|--------|
| List / search workspaces | Full | Full (`qs` + global phrases) | Full | Scoring differs |
| Create / edit workspace | Full (Adaptive Cards) | Full (WPF editor) | Full (React form) | Different UX stacks |
| Delete / pin / reorder favorites | Full | Partial (context menu subset) | Partial (list actions) | Verify before assuming full parity |
| Multi-launch rows | Full | Full | Full | Same concept |
| Tabs vs separate windows | Full | Full | Full | `multiLaunchPresentation`; no `-w` on tab segments |
| Open elevated / standard | Full | Full | Partial | Check Raycast elevation path |
| Import / export JSON | Full | Via settings / shared file | Full (extension import-export) | Desktop shares one file; Raycast is separate blob |
| Layout undo / redo | Full | Partial (plugin + editor) | Full (storage history) | |
| Section separators | Full | Partial | Partial | Layout on disk supports them |

---

## Launch & terminals

| Capability | CmdPal | Run | Raycast | Notes |
|------------|--------|-----|---------|--------|
| Core `ShortcutLaunchExecutor` | Full | Full | **No** (TS reimplementation) | |
| Terminal catalog / profiles | Full | Full | Partial | TS catalog simplified |
| same-as-previous resolve | Full | Full | Full (loop-based) | |
| WSL path handling | Full | Full | Partial | Desktop richer |
| Launch diagnostics copy | Full | Partial | Partial | CmdPal status/diagnostics strongest |
| Single-row launch (no companion/dev-server) | Full | Full | Partial | |

See [launch.md](./launch.md), [hosts.md](./hosts.md).

---

## Settings

| Setting / behavior | CmdPal | Run | Raycast | Notes |
|--------------------|--------|-----|---------|--------|
| `terminalApplication` | Full | Full | Full | Desktop shared JSON |
| `defaultProfile` | Full | Full | Full | |
| `multiLaunchPresentation` | Full | Full | Full | |
| `recentWorkspaceCount` | Full | Full | Full | Semantics quirks on CmdPal text setting |
| `blockDirtyBranchSwitch` | Full | Full | **No** (not in Raycast schema) | **Intentional gap** until added |
| Refresh terminal list | Full | Partial | Partial | |

See [settings.md](./settings.md).

---

## Health, git, discover

| Capability | CmdPal | Run | Raycast | Notes |
|------------|--------|-----|---------|--------|
| Full `WorkspaceHealthCheck` | Full | Full (on launch) | **Partial** | Raycast: path/validation/plan; weak ports/processes/profiles |
| List badges / status snapshot | Full | Partial (subtitles/icons) | Partial | |
| Worktree target branch store | Full | Full (Core gate) | **No** | **Intentional gap** |
| Git launch gate (dirty block) | Full | Full | **No** / weak | Depends on settings + Core |
| Workspace status page | Full | No | No | CmdPal-only UI |
| Discover git repos | Full | No | Full | Different scanners |
| `GitRepoIndex` prewarm | Full | No | N/A | |

See [git-and-discover.md](./git-and-discover.md), [launch.md](./launch.md).

---

## Intelligence & companions

| Capability | CmdPal | Run | Raycast | Notes |
|------------|--------|-----|---------|--------|
| Project classification | Full (Core) | Full (Core) | Partial (TS setup suggest) | |
| Suggestion pills | Full | Full (panel) | Full via **Suggest.exe** | Same Core when Suggest ships |
| Setup seed on create/discover | Full | Partial | Partial | |
| Companion catalog / detection | Full | Full (editor/settings) | Partial | Raycast companion path/args; weaker presets |
| Dev server URL + open on launch | Full | Full | Full | Timing differs slightly |
| Agent CLI PATH pills | No | No | No | Roadmap Tier 2 / PR C |

See [intelligence.md](./intelligence.md), [companions.md](./companions.md), [post-launch.md](./post-launch.md).

---

## CmdPal-only chrome

| Capability | CmdPal | Run | Raycast |
|------------|--------|-----|---------|
| Root palette fallback / home keywords | Full | N/A (global query phrases instead) | N/A |
| Deep-link command router | Full | N/A | N/A |
| Pending edit draft page | Full | No | No |
| Import conflict Adaptive Card | Full | Different UX | Different UX |

See [cmdpal-surface.md](./cmdpal-surface.md).

---

## Intentional gaps (do not “fix” without a decision)

1. **Raycast does not share** `%LOCALAPPDATA%\QuickShell\` stores with desktop — import/export is the bridge.  
2. **Raycast has no** `blockDirtyBranchSwitch` / worktree target file — git gate parity is **not** claimed.  
3. **Raycast health** is a subset — not a bug until product wants parity.  
4. **Run** is a second desktop host on **shared** Core data — launch behavior must match CmdPal; UI chrome need not.  
5. **Suggest.exe** is required for Raycast pills in production packaging.

---

## How to use this matrix

- **New feature:** add a row; fill all three hosts; if one is “No,” say intentional or track work.  
- **Bug:** check whether the host is Partial by design before porting full Core behavior.  
- **PR review:** reject silent CmdPal-only Core changes that break Run launch without tests.

---

## Related

- [hosts.md](./hosts.md)  
- [proposal-status.md](./proposal-status.md)  
- [roadmap-next-steps.md](./roadmap-next-steps.md)  
