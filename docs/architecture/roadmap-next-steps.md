# Roadmap: clean, fix, improve, optimize

**Audience:** maintainers and coding agents  
**Status:** guidance (not a committed release plan)  
**Last updated:** 2026-07-13

**Principle:** protect the core loop (save → open correctly → tabs when possible), cut cognitive load in Core, don’t open a fourth host. Ship small, measurable wins.

---

## North star (next 1–2 quarters)

1. **Core stays the single brain** for CmdPal + Run.
2. **Raycast = deliberate parity budget**, not free-form drift.
3. **Complexity goes down or stays flat** while features go up.
4. **Docs stay honest** (as-built tours updated with spine changes).

---

## Tier 0 — Stop the bleeding (do first)

| # | Action | Why decisive |
|---|--------|----------------|
| **0.1** | **Inventory what’s already landed from 0001–0002** (DI, envelope, atomic writer, `WorkspacesChanged`) and mark proposals **landed / partial / obsolete** | Avoid re-implementing done work or designing against a dead audit |
| **0.2** | **Define a “parity matrix”** (1 page): CmdPal / Run / Raycast × launch, settings, health, git targets, import/export, pills | Stops accidental half-features and Raycast drift |
| **0.3** | **Rule: any launch/persistence/routing change updates the matching `docs/architecture/*` tour in the same PR** | Keep the as-built map from rotting |

**Exit criteria:** You can point at “current architecture truth” without re-auditing from zero.

See also: [README.md](./README.md) (as-built vs proposals), [hosts.md](./hosts.md).

---

## Tier 1 — High leverage engineering (next decisive work)

These change how fast you can ship everything else.

### 1. Finish DI + kill static hubs where it hurts

**What:** Promote the hot statics onto interfaces already half-there: launch orchestration, health, terminal resolve, git ops. Shrink remaining static facades.

**Why first:** Every new feature (agent pills, better health, Run tweaks) is cheaper with testable constructors.

**Not:** Full rewrite of ~100 service files in one PR. Order by call frequency:

1. `ShortcutLaunchExecutor` / `TerminalLauncher` (already overridable in places)
2. `WorkspaceHealthCheck`
3. `TerminalCatalog` resolve path
4. Git gate + worktree store

Related proposal: [0001-introduce-dependency-injection-composition-root.md](./0001-introduce-dependency-injection-composition-root.md) (verify what already landed).

### 2. Intelligence registry (0004, slim version)

**What:** One `ITaskSuggestionProvider` (or similar) list registered in DI:

- Project setup / Node scripts / Docker
- (Next) **Agent CLI PATH detectors** (`claude`, `codex`, `opencode`, …)

**Why:** Grow suggestions without another permanent static cluster.

**Do not** boil the ocean into one mega `ProjectLayoutAnalyzer` in the first PR.

Related: [intelligence.md](./intelligence.md), [0004-service-consolidation-registry-pattern.md](./0004-service-consolidation-registry-pattern.md).

### 3. Command routing freeze + document contract

**What:** Treat deep-link IDs as a **versioned public surface** (table in [cmdpal-surface.md](./cmdpal-surface.md) or a small command-id doc). Prefer handlers over new string parse branches.

**Why:** Fragile `TryParse*` is a recurring footgun; `CommandKind` + router already exist — lean on them.

Related proposal: [0003-replace-string-command-routing-with-typed-descriptors.md](./0003-replace-string-command-routing-with-typed-descriptors.md).

### 4. Persistence secondary stores audit

**What:** Confirm worktree targets + drafts are fully atomic; one migration story for envelope; one smoke test “corrupt shortcuts → bak / last good.”

**Why:** User trust is binary on “my list didn’t die.”

Related: [persistence.md](./persistence.md), [0002-persistence-hardening-atomic-writes-schema-version.md](./0002-persistence-hardening-atomic-writes-schema-version.md).

---

## Tier 2 — Product fixes that feel like quality

High user-visible ROI, constrained scope.

| Priority | Item | Notes |
|----------|------|--------|
| **P1** | **Agent CLI pills (PATH + markers)** | Landed in Core; `AgentCliSuggestion` — PATH primary, project markers as fallback. Not auto-seeded on create. |
| **P1** | **Companion: JetBrains routing** | Landed: `.idea` + stack signals → Rider / Android Studio / WebStorm / PyCharm / GoLand / CLion / IDEA; first installed match (+ last-used) |
| **P2** | **VS Code Insiders + AI IDEs** | Landed: Insiders, Devin (Windsurf paths), Antigravity, Kiro; GitKraken / Sourcetree |
| **P2** | **Notepad++ default args** | Landed: `{folder}` |
| **P2** | **Health: expand `same-as-previous` on multi-row Check** | Gap noted in [launch.md](./launch.md) |
| **P3** | **Raycast: document intentional gaps**; close only high-value items | Don’t promise full health/git targets unless committed |

Related: [companions.md](./companions.md), [intelligence.md](./intelligence.md), [hosts.md](./hosts.md).

---

## Tier 3 — Performance / UX polish (only with evidence)

Don’t optimize by vibe.

| Area | When to touch |
|------|----------------|
| Home list status / health | Profile cold open + 50–100 workspaces; then cache/TTL |
| `GitRepoIndex` prewarm | Only if discover feels slow |
| Adaptive Card form rebuilds | Only if create/edit jank is reported (3-row padding already helps) |
| MarkUsed debounce / clone volume | Follow [performance-audit.md](../performance-audit.md) only after **re-measuring** — that doc may be stale |

**Decisive rule:** one metric (e.g. provider ctor time, list reload ms, discover scan time) before a “perf” PR.

---

## Tier 4 — Explicit non-goals (for now)

Saying no is a strategy.

- Fourth host / standalone app
- Cloud sync of workspaces
- Full monorepo deep crawl for every `package.json`
- Agents as companion apps (use launch rows / pills instead)
- Big-bang rewrite of Adaptive Cards into WinUI “because forms suck” (keep until form bugs dominate)
- Raycast rewrite onto Core via FFI (not free; parity matrix first)

---

## Recommended sequence (next 4–6 PRs)

| PR | Scope | Outcome |
|----|--------|---------|
| **A** | Audit reconciliation + parity matrix + mark 000x status | Shared truth |
| **B** | DI: launch + health injectable/test seams expanded | Safer changes |
| **C** | Suggestion provider registry + **agent PATH pills** | Feature + architecture win |
| **D** | Companion JetBrains + Insiders (+ Devin/Antigravity/Kiro) | Landed (see [companions.md](./companions.md)) |
| **E** | Health same-as-previous resolve on full Check | Correctness |
| **F** | Raycast gap list + only high-value closes | Controlled parity |

After that: either **typed routing cleanup** or **startup perf** with numbers.

---

## Fix vs clean vs improve vs optimize

| Verb | Do this |
|------|---------|
| **Fix** | Multi-row health + same-as-previous; companion detection wrong IDE; document Raycast/desktop settings split |
| **Clean** | DI for hot paths; suggestion registry; freeze command IDs; retire stale proposal text |
| **Improve** | Agent pills; JetBrains companions; Insiders |
| **Optimize** | Only after measuring list/open/discover — not speculative micro-opts |

---

## Single most decisive step

If you only do one thing next:

> **PR A (truth) + PR C (registry + agent pills).**

A stops thrashing. C proves the intelligence layer can grow without more static soup **and** ships something users feel (agent multi-launch is already a manual pattern — make the product suggest it).

---

## What “done good enough” looks like

- New contributor (or agent) opens [README.md](./README.md) and can change launch without a full re-tour
- Adding a pill source is “one provider + registration,” not five files of tribal knowledge
- CmdPal and Run never diverge on open behavior
- Raycast gaps are listed and intentional
- Health and companions don’t lie about multi-row / installed tools

That’s a healthy trajectory: **keep the good core loop, tax complexity only when features pay for it.**

---

## Related as-built tours

| Doc | Topic |
|-----|--------|
| [overview.md](./overview.md) | Solution map |
| [launch.md](./launch.md) | Launch / health / tabs |
| [persistence.md](./persistence.md) | Store / undo / import |
| [forms.md](./forms.md) | Adaptive Cards / drafts |
| [intelligence.md](./intelligence.md) | Pills / classification |
| [companions.md](./companions.md) | GUI companions |
| [settings.md](./settings.md) | Global prefs |
| [cmdpal-surface.md](./cmdpal-surface.md) | Home / routing / fallback |
| [git-and-discover.md](./git-and-discover.md) | Worktrees / discover |
| [hosts.md](./hosts.md) | CmdPal / Run / Raycast |
| [post-launch.md](./post-launch.md) | Dev-server / companion timing |
