# Proposal status inventory (0001–0005)

**Tier 0.1 deliverable**  
**As of:** 2026-07-13  
**Method:** Code inspection of `QuickShell.Core`, `QuickShell`, and tests vs proposal text.

Status legend:

| Status | Meaning |
|--------|---------|
| **Landed** | Goals substantially implemented; proposal text may still read as “proposed” |
| **Partial** | Core scaffolding exists; important proposal goals remain |
| **Not started** | Little or no matching implementation |
| **Obsolete claim** | Proposal “current state” section is outdated |

---

## Summary

| ID | Title | Status | Next decisive work |
|----|--------|--------|-------------------|
| **0001** | DI + composition root | **Partial** | Inject real launch path (not only facades); reduce static call sites |
| **0002** | Persistence hardening | **Landed** (minor follow-ups) | Optional: settings.json atomicity; worktree doc version field |
| **0003** | Typed command routing | **Partial** (mostly landed) | Freeze ID contract; avoid new ad-hoc `TryParse*` outside router |
| **0004** | Classifier / suggestion registry | **Not started** | Slim `ITaskSuggestionProvider` + agent pills |
| **0005** | Dispose / cancellation / tests | **Partial** | Root CTS for background work; more lifetime tests |

As-built tours: [README.md](./README.md). Roadmap: [roadmap-next-steps.md](./roadmap-next-steps.md).

---

## 0001 — Dependency injection + composition root

**Proposal file:** [0001-introduce-dependency-injection-composition-root.md](./0001-introduce-dependency-injection-composition-root.md)  
**Proposal header still says:** Proposed  

### Landed

| Claim / goal | Evidence |
|--------------|----------|
| `Microsoft.Extensions.DependencyInjection` | Package on Core + tests; used by CmdPal host |
| `Composition/` + `AddQuickShellCore` | `QuickShell.Core/Composition/QuickShellServiceCollectionExtensions.cs` |
| `Abstractions/` interfaces | `ITerminalLauncher`, `IWorkspaceHealthChecker`, `IGitRepoIndex`, `IWorkspaceGitOperations`, `ITerminalProfileResolver`, `IWorkspaceMapper` |
| Host DI | `AddQuickShellHost` → `AddQuickShellCore` + command routing; `QuickShellCommandsProvider` builds `ServiceProvider` |
| Repo + drafts + atomic writer DI | Registered singletons; composition tests |
| Tests | `QuickShellCompositionRootTests` |

### Still open / partial

| Gap | Notes |
|-----|--------|
| Many helpers remain **static** | `TerminalLauncher`, `WorkspaceHealthCheck`, `ShortcutLaunchExecutor`, `TerminalCatalog`, `GitRepoIndex`, classifiers, companions, etc. |
| Service wrappers only delegate | e.g. `TerminalLauncherService` → static `TerminalLauncher`; `WorkspaceHealthCheckerService` → static `WorkspaceHealthCheck` |
| Host still uses facade | `QuickShellServices.Current` (successor to runtime hub) used widely in pages/commands |
| Run plugin | Constructs `ShortcutRepository` / settings reader directly — not full composition root |

### Obsolete proposal claims

- “DI is **not** referenced” — **false today**  
- “`Abstractions/` and `Composition/` do not exist” — **false today**  
- “`QuickShellRuntimeServices` is the hub” — replaced/renamed pattern around **`QuickShellServices`**

**Verdict:** Foundation **partially landed**. Treat remaining work as Tier 1 “finish DI for hot paths,” not greenfield 0001.

---

## 0002 — Persistence hardening

**Proposal file:** [0002-persistence-hardening-atomic-writes-schema-version.md](./0002-persistence-hardening-atomic-writes-schema-version.md)

### Landed

| Goal | Evidence |
|------|----------|
| Shared `IAtomicFileWriter` / `AtomicFileWriter` | `Services/IAtomicFileWriter.cs`, `AtomicFileWriter.cs` |
| Shortcuts versioned envelope | `ShortcutLayoutJson.Serialize` writes `version` + `entries`; `PersistenceVersion.Current = 1`; array still readable as legacy |
| Atomic secondary stores | `ShortcutDraftStore` + `WorktreeBranchTargetStore` use atomic writer |
| `WorkspacesChanged` | `IShortcutRepository` + `ShortcutRepository` |
| Inject writer into repository | Constructor takes `IAtomicFileWriter` |

### Minor residual gaps

| Item | Notes |
|------|--------|
| Worktree document schema version | `WorktreeBranchTargetsDocument` is targets map only — no integer envelope (optional) |
| Settings.json atomicity | Still host-owned (`QuickShellJsonSettingsStore` / CmdPal settings); out of Core scope in proposal |
| Proposal text “current gaps” | Largely **obsolete** |

**Verdict:** **Landed** for Core goals. Mark proposal as implemented; only optional polish remains.

---

## 0003 — Typed command routing

**Proposal file:** [0003-replace-string-command-routing-with-typed-descriptors.md](./0003-replace-string-command-routing-with-typed-descriptors.md)

### Landed

| Goal | Evidence |
|------|----------|
| `CommandDescriptor` / `CommandKind` | Core services |
| `ICommandIdParser` / `CommandIdParser` | Parses deep-link IDs → descriptors |
| `ICommandRouter` / `CommandRouter` | Host `Services/CommandRouter.cs` |
| Handler registry | `Services/CommandRouting/*` + `AddQuickShellCommandRouting` |
| `GetCommandItem` delegates | `QuickShellCommandsProvider` → `_commandRouter.TryHandle` |

### Residual

| Gap | Notes |
|-----|--------|
| ID string builders still scattered | `ShortcutCommandIds`, `QuickShellDeepLinkIds` (expected; need a frozen contract doc) |
| Some list factories still create items with string IDs | Fine if parse path is single-router |

**Verdict:** **Partial → mostly landed**. Remaining work is contract hygiene + not regressing to parallel parse chains (see Tier 1 / roadmap).

---

## 0004 — Service consolidation / registry

**Proposal file:** [0004-service-consolidation-registry-pattern.md](./0004-service-consolidation-registry-pattern.md)

### Landed

- None of `IProjectClassifier`, `ITaskSuggestionProvider`, `IProjectAnalysisService`, or central registry found in Core.

### Still as described

- Large `Services/` surface; static discovery/classifier/suggestion helpers
- New pill sources still require multi-file awareness

**Verdict:** **Not started.** Primary Tier 1/C roadmap item.

---

## 0005 — Dispose / cancellation / tests

**Proposal file:** [0005-formal-disposable-cancellation-ownership-expanded-tests.md](./0005-formal-disposable-cancellation-ownership-expanded-tests.md)

### Landed

| Item | Evidence |
|------|----------|
| Provider dispose | Unsubscribes settings, disposes page/fallback, unbinds services, disposes `ServiceProvider` |
| Repository / drafts / debouncer disposable | Present |
| Composition tests | Some DI resolve tests |

### Still open

| Gap | Notes |
|-----|--------|
| No root `CancellationTokenSource` on extension | Background `GitRepoIndex.Prewarm` / `Task.Run` not cancelled on dispose |
| Static caches (`GitRepoIndex`, classification cache) | Lifetime not tied to provider CTS |
| Lifetime-focused integration tests | Thin vs proposal ambition |

**Verdict:** **Partial** — dispose exists; cancellation ownership and expanded lifetime tests do not match the proposal.

---

## Recommended doc updates when proposals change

When landing remaining work, update this file’s Status column **and** the Status line at the top of each `000x-*.md` in the same PR.

---

## Related

- [roadmap-next-steps.md](./roadmap-next-steps.md) — Tier 0 complete when this inventory + parity matrix + doc rule exist  
- [parity-matrix.md](./parity-matrix.md) — Tier 0.2  
- [CONTRIBUTING-architecture.md](./CONTRIBUTING-architecture.md) — Tier 0.3  
