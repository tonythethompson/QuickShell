# Host parity matrix (CmdPal / Run / Raycast)

**Tier 0.2 deliverable**  
**As of:** 2026-07-20  

Compares product capabilities across hosts. Use this before adding a feature on only one surface.

**Phase 3 storage decision (locked):** Raycast keeps parallel `LocalStorage` (`quickshell-data`). Import/export is the bridge to desktop. Shared `%LOCALAPPDATA%\QuickShell\` stores, ETW/`QuickShellEventSource`, and git worktree targets remain intentional gaps (see below and [QuickShell-TechDebt-Phases.md](./QuickShell-TechDebt-Phases.md) Phase 3).

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
| Delete / pin / reorder favorites | Full | Partial (context menu subset) | Full (pin + move up/down) | |
| Multi-launch rows | Full | Full | Full | Same concept |
| Tabs vs separate windows | Full | Full | Full | `multiLaunchPresentation`; no `-w` on tab segments |
| Open elevated / standard | Full | Full | Full | Raycast elevation + mixed-admin grouping |
| Import / export JSON | Full | Via settings / shared file | Full (extension import-export) | Desktop shares one file; Raycast is separate blob; import always untrusted |
| Layout undo / redo | Full | Partial (plugin + editor) | Full (storage history) | |
| Section separators | Full | Partial | Full (Raycast-local `layoutEntries`) | Desktop shared layout file; Raycast blob parallel |

---

## Launch & terminals

| Capability | CmdPal | Run | Raycast | Notes |
|------------|--------|-----|---------|--------|
| Core `ShortcutLaunchExecutor` | Full | Full | **No** (TS reimplementation) | |
| Terminal catalog / profiles | Full | Full | Partial | TS catalog simplified |
| same-as-previous resolve | Full | Full | Full (loop-based) | |
| WSL path handling | Full | Full | Full (distro list when `wsl -l -q` works) | |
| Launch diagnostics copy | Full | Partial | Full (toast Copy Diagnostics) | Structured text summary; no ETW |
| Single-row launch (no companion/dev-server) | Full | Full | Full | |

See [launch.md](./launch.md), [hosts.md](./hosts.md).

---

## Settings

| Setting / behavior | CmdPal | Run | Raycast | Notes |
|--------------------|--------|-----|---------|--------|
| `terminalApplication` | Full | Full | Full | Desktop shared JSON |
| `defaultProfile` | Full | Full | Full | |
| `multiLaunchPresentation` | Full | Full | Full | |
| `recentWorkspaceCount` | Full | Full | Full | Semantics quirks on CmdPal text setting |
| `blockDirtyBranchSwitch` | Full | Full | Full (extension preference) | Raycast-local gate; not shared with desktop JSON |
| Refresh terminal list | Full | Partial | Partial | |

See [settings.md](./settings.md).

---

## Health, git, discover

| Capability | CmdPal | Run | Raycast | Notes |
|------------|--------|-----|---------|--------|
| Full `WorkspaceHealthCheck` | Full | Full (on launch) | **Partial** | Raycast: path/validation/plan + terminal host + WSL note + port-in-use **warnings**; no process-list |
| List badges / status snapshot | Full | Partial (subtitles/icons) | Partial | |
| Worktree target branch store | Full | Full (Core gate) | Full (Raycast-local `branchTargets` in blob) | **No shared** `worktree-branch-targets.json` |
| Git launch gate (dirty block) | Full | Full | Full (TS `git-launch-gate`) | Same rules; Raycast-local targets |
| Workspace status page | Full | No | No | CmdPal-only UI |
| Discover git repos | Full | No | Full | Different scanners |
| `GitRepoIndex` prewarm | Full | No | N/A | |

See [git-and-discover.md](./git-and-discover.md), [launch.md](./launch.md).

---

## Intelligence & companions

| Capability | CmdPal | Run | Raycast | Notes |
|------------|--------|-----|---------|--------|
| Project classification | Full (Core) | Full (Core) | Partial (Suggest.exe + local heuristics) | |
| Suggestion pills | Full | Full (panel) | Full via **Suggest.exe** (local fallback) | Form seeds from Suggest when CLI present |
| Setup seed on create/discover | Full | Partial | Full (Suggest + heuristics) | |
| Companion catalog / detection | Full | Full (editor/settings) | Partial (multi-row + presets + folder markers) | No JetBrains/vswhere walks |
| Dev server URL + open on launch | Full | Full | Full | Companions before terminals; URL after |
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

1. **Raycast does not share** `%LOCALAPPDATA%\QuickShell\` stores with desktop — parallel LocalStorage; import/export is the bridge.  
2. **Raycast git targets** live in the LocalStorage blob (`branchTargets`), not Core `worktree-branch-targets.json`. Dirty block uses preference `blockDirtyBranchSwitch` (default true).  
3. **Raycast health** covers path/validation/plan + terminal host + WSL note + port-in-use warnings; process-list checks remain desktop-only.  
4. **Run** is a second desktop host on **shared** Core data — launch behavior must match CmdPal; UI chrome need not.  
5. **Suggest.exe** is preferred for Raycast pills; local folder heuristics are the fallback when the CLI is missing.  
6. **ETW / `QuickShell-Diagnostics` EventSource** is desktop Core/CmdPal only — not a Raycast port target.
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
