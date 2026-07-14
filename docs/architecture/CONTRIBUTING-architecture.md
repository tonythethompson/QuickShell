# Contributing: keep architecture docs honest

**Tier 0.3 deliverable**

As-built tours under `docs/architecture/` are the **current-state** map for contributors and coding agents. Proposals (`0001`–`0005`) and the July audit may lag. Do not let the tours rot.

---

## Required: update tours with spine changes

If your PR changes behavior or contracts in any of the areas below, **update the matching tour in the same PR** (not a follow-up “docs later” promise).

| Area | Tour to update |
|------|----------------|
| Solution layout, DI composition, “where to change what” | [overview.md](./overview.md) |
| Launch, grouping/tabs, terminal resolve, argv, health, git gate | [launch.md](./launch.md) |
| `shortcuts.json`, atomic writes, undo, import/export, legacy | [persistence.md](./persistence.md) |
| Adaptive Card forms, drafts, form undo | [forms.md](./forms.md) |
| Classification, pills, setup seeds, Suggest CLI | [intelligence.md](./intelligence.md) |
| Companion catalog, detection, launch, args | [companions.md](./companions.md) |
| Global settings keys / host prefs | [settings.md](./settings.md) |
| Home list, deep links, fallback, provider | [cmdpal-surface.md](./cmdpal-surface.md) |
| Worktree targets, discover repos, status UI | [git-and-discover.md](./git-and-discover.md) |
| CmdPal vs Run vs Raycast differences | [hosts.md](./hosts.md) + **[parity-matrix.md](./parity-matrix.md)** |
| Dev-server URL, post-open links, companion timing | [post-launch.md](./post-launch.md) |
| Priority / roadmap sequence | [roadmap-next-steps.md](./roadmap-next-steps.md) (only if priorities change) |
| Proposal landed status | [proposal-status.md](./proposal-status.md) + Status line on the `000x` file |

Also update [README.md](./README.md) if you add a **new** tour file.

---

## What counts as a “spine change”

Update docs when you change any of:

- On-disk formats or settings **keys**
- Launch grouping rules, elevation, or argv construction
- Health blocking vs warning semantics
- Deep-link ID formats or `CommandKind` set
- Host parity (e.g. Raycast gains git gate)
- Public behavior users or other hosts rely on

**Skip** tour edits for pure renames inside a single file, comment-only PRs, or tests that lock existing behavior with no contract change. When unsure, update the tour — short PR note is enough.

---

## Style

- Prefer call graphs, tables, and file maps over essay form.
- Mark intentional host gaps in [parity-matrix.md](./parity-matrix.md).
- Do not rewrite proposal docs as “done” without updating [proposal-status.md](./proposal-status.md).

---

## Agents

`AGENTS.md` points here. Before large refactors, read the relevant tour + parity matrix. After implementation, patch the tour in the same change set.

---

## Related

- [README.md](./README.md) — index
- [proposal-status.md](./proposal-status.md) — 0001–0005 inventory
- [parity-matrix.md](./parity-matrix.md) — host comparison
