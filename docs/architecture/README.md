# QuickShell architecture (contributors)

This folder has two kinds of documents:

| Kind | Purpose |
|------|---------|
| **As-built tours** (below) | How the code works **today** — call paths, file maps, gotchas |
| **Proposals / audit** | Design intent for refactors (`0001`–`0005`, architectural audit) — may lag landed code |

Prefer the **as-built** pages when changing behavior. Treat numbered proposals as historical or planned work unless you verify the implementation still matches.

## Product shape

Quick Shell is a **Windows keyboard-first workspace launcher**:

- Save project folders as **workspaces** (multi-launch rows, optional companion app / dev server URL).
- Open them from **PowerToys Command Palette**, **PowerToys Run** (`qs`), or **Raycast for Windows**.
- Shared domain logic lives in **`QuickShell.Core`** (.NET). Raycast reimplements concepts in TypeScript and uses **`QuickShell.Suggest`** for command pills.

```
CmdPal (QuickShell/)  ──┐
Run (QuickShell.Run/) ──┼──► QuickShell.Core  (models, launch, health, persistence, intelligence)
Raycast (TS)          ──┘     (concepts + Suggest.exe; not the Core assembly)
```

Data root: `%LOCALAPPDATA%\QuickShell\` (`shortcuts.json`, `settings.json`, drafts, worktree branch targets).

## As-built tours

| Doc | Topic |
|-----|--------|
| [overview.md](./overview.md) | Solution map, layers, where to change what |
| [launch.md](./launch.md) | Launch pipeline, terminal resolve, grouping/tabs, argv, health, git gate |
| [persistence.md](./persistence.md) | `ShortcutRepository`, envelope, atomic writes, import/export, legacy migration |
| [forms.md](./forms.md) | CmdPal Adaptive Card forms, drafts, form undo vs list undo |
| [intelligence.md](./intelligence.md) | Project classification, setup seeds, suggestion pills |
| [companions.md](./companions.md) | Companion app catalog, detection, launch, args |
| [settings.md](./settings.md) | Global `settings.json` / host prefs |
| [cmdpal-surface.md](./cmdpal-surface.md) | Home list, deep links, fallback, provider |
| [git-and-discover.md](./git-and-discover.md) | Worktree targets, git gate UI, discover repos |
| [hosts.md](./hosts.md) | CmdPal vs Run vs Raycast parity |
| [post-launch.md](./post-launch.md) | Dev-server URL, companion timing, link actions |
| [trust-model.md](./trust-model.md) | Repository-owned trust, authorization, ingress, and threat model |
| [roadmap-next-steps.md](./roadmap-next-steps.md) | Clean / fix / improve / optimize priorities and PR sequence |
| [proposal-status.md](./proposal-status.md) | **Tier 0.1** — 0001–0005 landed / partial / not started |
| [parity-matrix.md](./parity-matrix.md) | **Tier 0.2** — CmdPal / Run / Raycast capability matrix |
| [CONTRIBUTING-architecture.md](./CONTRIBUTING-architecture.md) | **Tier 0.3** — when PRs must update tours |

## Proposals and audit

| Doc | Topic | Status (see inventory) |
|-----|--------|------------------------|
| [QuickShell-Architectural-Audit-2026-07-08.md](./QuickShell-Architectural-Audit-2026-07-08.md) | Snapshot review (risks, layering, ranked improvements) | Point-in-time |
| [0001-…](./0001-introduce-dependency-injection-composition-root.md) | DI + composition root | **Partial** |
| [0002-…](./0002-persistence-hardening-atomic-writes-schema-version.md) | Atomic writer + schema version | **Landed** (Core) |
| [0003-…](./0003-replace-string-command-routing-with-typed-descriptors.md) | Typed command routing | **Partial / mostly landed** |
| [0004-…](./0004-service-consolidation-registry-pattern.md) | Classifier / suggestion registry | **Not started** |
| [0005-…](./0005-formal-disposable-cancellation-ownership-expanded-tests.md) | Lifetime / cancellation / tests | **Partial** |

Do **not** re-implement 0001/0002 from scratch. Read [proposal-status.md](./proposal-status.md) first.

## Related (outside this folder)

- Root [README.md](../../README.md) — product features (user-facing)
- [AGENTS.md](../../AGENTS.md) — build/test constraints for coding agents
- [docs/getting-started.md](../getting-started.md) — end-user workflows
- [docs/performance-audit.md](../performance-audit.md) — performance notes (may be partially stale)

## Keeping docs honest

When you change a spine (launch grouping, persistence format, pill scoring, companion presets, settings keys, routing IDs, host parity), update the matching as-built page in the same PR. Prefer diagrams + file tables over exhaustive API listings.

**Required process:** [CONTRIBUTING-architecture.md](./CONTRIBUTING-architecture.md).
