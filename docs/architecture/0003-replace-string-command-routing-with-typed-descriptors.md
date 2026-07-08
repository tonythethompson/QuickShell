# PR Proposal: 0003

**Title:**  
Replace Brittle String-Based Command Routing with Typed `CommandDescriptor` + Registry Pattern

**PR Type:** Foundational / Architectural  
**Priority:** P1 (High)  
**Estimated Size:** Medium to Large  
**Depends On:** #0001 (Introduce Dependency Injection + Composition Root) — strongly recommended  
**Benefits From:** #0002 (Persistence Hardening) — not strictly required  
**Enables:** Cleaner service consolidation, easier addition of new workspace actions / task types / git commands, safer deep linking, future multi-provider scenarios

---

### Motivation (from Architectural Audit)

The audit identified **High-severity** issues in **Core Functionality & Data Flow**:

> Command routing via `ShortcutCommandIds.TryParseOpen` / `TryParseOpenLaunch` etc. + factories (`ShortcutListItems.CreateOpen`, `ShortcutTaskActionListItems`). Fragile to refactoring; IDs become part of public contract; hard to add versioning or new command kinds.

**Current problems:**

- A growing number of static `TryParse*` methods scattered across `ShortcutCommandIds`, `ShortcutListItems`, `ShortcutTaskActionListItems`, and various `*Form*` classes.
- Command IDs are magic strings that must be kept in sync between list item creation and `GetCommandItem(string id)` handling.
- Adding a new command type (e.g., a new git worktree action, a new task suggestion, or a settings deep-link) requires touching multiple files and understanding implicit conventions.
- No clear ownership or single place to reason about all supported command kinds.
- Risk of ID collisions or silent fallback behavior as the feature surface grows (workspaces + launches + tasks + git operations + health actions + import/export + recents).
- Makes unit testing of command creation and deep linking painful.

This pattern worked when QuickShell was smaller. It is now a structural liability.

---

### Goals

1. Introduce a clean, typed `CommandDescriptor` (or `ICommandDescriptor`) concept.
2. Centralize command ID parsing, validation, and item creation behind a `ICommandRouter` / `CommandRegistry` service.
3. Make `QuickShellCommandsProvider.GetCommandItem(string id)` delegate to the registry instead of a large switch / chain of `TryParse*` calls.
4. Make adding new command types a **registration + implementation** task rather than "find all the places that parse IDs".
5. Preserve backward compatibility for any existing deep links or bookmarked command IDs.
6. Improve testability — command routing logic should be unit-testable in isolation.
7. Keep the public contract with the Command Palette host (string IDs on `CommandItem`) unchanged.

**Non-Goals (for this PR)**

- Full removal of all string IDs (they are required by the CmdPal SDK surface).
- Replacing the entire in-palette form system or draft handling.
- Introducing a visual command palette inside QuickShell itself.
- Changing how list items are rendered (only how they are created and routed).

---

### Proposed Design

#### Core Concepts

**1. `CommandDescriptor` (immutable record)**

```csharp
public sealed record CommandDescriptor(
    string Id,                    // Stable string ID passed to CmdPal host
    CommandKind Kind,             // Enum or string for categorization
    string? WorkspaceId = null,
    string? LaunchId = null,
    string? TaskId = null,
    object? Payload = null,       // Strongly-typed payload when useful
    bool RequiresElevation = false
);
```

**2. `CommandKind` enum** (or open string set for extensibility)

```csharp
public enum CommandKind
{
    OpenWorkspace,
    LaunchWorkspace,
    WorkspaceTaskAction,
    GitWorktreeCheckout,
    GitBranchSwitch,
    HealthCheckRefresh,
    CreateNewWorkspace,
    OpenSettings,
    ImportWorkspaces,
    ExportWorkspaces,
    // ... others
}
```

**3. `ICommandRouter` (or `ICommandRegistry`)** — the key new service

```csharp
public interface ICommandRouter
{
    /// <summary>
    /// Attempts to parse a raw command ID into a strongly-typed descriptor.
    /// </summary>
    bool TryParse(string rawId, out CommandDescriptor descriptor);

    /// <summary>
    /// Creates the appropriate CommandItem (or page) for the given descriptor.
    /// </summary>
    CommandItem? CreateCommandItem(CommandDescriptor descriptor, CommandContext context);

    /// <summary>
    /// Returns all top-level commands this router knows about (for the main page).
    /// </summary>
    IReadOnlyList<CommandItem> GetTopLevelCommands();
}
```

The implementation (`CommandRouter`) becomes the single owner of:

- All ID parsing logic (moved out of `ShortcutCommandIds` and the various `*ListItems` static classes)
- Factory logic currently spread across `ShortcutListItems`, `ShortcutTaskActionListItems`, etc.
- Registration of handlers for each `CommandKind`

#### Recommended Implementation Approach

**Option A (Recommended — Pragmatic)**  
Keep string IDs as the public contract.  
Introduce `CommandRouter` as a **stateful registry** that:

- On startup, registers handlers for each known `CommandKind`.
- `TryParse` uses a combination of prefix matching + payload extraction (or a more sophisticated parser if needed).
- `CreateCommandItem` dispatches to the registered handler for that kind.

This is evolutionary — we can migrate one command kind at a time.

**Option B (More ambitious)**  
Use a full visitor or strategy pattern with `ICommandHandler<TDescriptor>` registrations.  
Higher ceremony, bigger payoff if the number of command types continues to grow rapidly.

I recommend starting with **Option A** for this PR. It delivers most of the value with lower risk and smaller diff.

#### Integration Points

- `QuickShellCommandsProvider` constructor receives `ICommandRouter` (via DI from #0001).
- `GetCommandItem(string id)` becomes a one-liner delegation to `_router.TryParse(id, out var desc) ? _router.CreateCommandItem(desc, context) : null`.
- All list item creation factories (`CreateOpen`, `CreateLaunch`, task action items, etc.) move inside the router or become strategies registered with it.
- Context commands (Create, Settings, Undo/Redo, etc.) can also be expressed as descriptors.

---

### Impact on Existing Code

**High-impact files (will change significantly):**

- `QuickShellCommandsProvider.cs` — constructor + `GetCommandItem` simplified dramatically.
- `ShortcutCommandIds.cs` — largely deprecated or reduced to constants + parsing helpers (internal to router).
- `ShortcutListItems.cs` and `ShortcutTaskActionListItems.cs` — logic moves into router handlers.
- Any page or list builder that currently calls static `Create*` methods.

**Medium-impact files:**

- Various `*Form*` and draft-related classes (if they produce command items).
- Git-related action classes (`WorkspaceGitLaunchGate`, etc.).

**Low / No impact:**

- Domain models (`Workspace`, `TerminalShortcut`, `WorkspaceTaskAction`, etc.).
- `ShortcutRepository` and persistence layer.
- Terminal launching and health check logic.

---

### Migration / Rollout Strategy (Incremental & Safe)

Because command routing touches the heart of the user-facing behavior, we must migrate carefully:

1. **Phase 1 (this PR)**: Introduce `CommandDescriptor`, `CommandKind`, and `ICommandRouter` + a basic `CommandRouter` implementation.  
   Wire it via DI.  
   Keep all existing static `TryParse*` and `Create*` methods working as a **compatibility layer** (they delegate to the new router internally).  
   Update only the main `GetCommandItem` path to use the router.

2. **Phase 2 (immediate follow-up or same PR if diff is manageable)**: Migrate the most common command kinds (Open Workspace, Launch Workspace, basic Task Actions) fully into the router. Remove the old static methods for those kinds.

3. **Phase 3**: Migrate remaining kinds (git worktree, health actions, import/export, settings deep links, etc.).

4. **Phase 4**: Delete the old `ShortcutCommandIds.TryParse*` surface and the scattered factory classes.

We should aim to land a working Phase 1 in this PR so the extension continues to function normally while the refactor is in progress.

---

### Testing Strategy

- Unit tests for `CommandRouter.TryParse` covering all current ID formats + edge cases (malformed, unknown, versioned future IDs).
- Unit tests for `CommandRouter.CreateCommandItem` for each `CommandKind`.
- Integration test that constructs a `ServiceProvider` (from #0001), resolves `ICommandRouter`, and exercises the full `GetCommandItem` path for several representative commands.
- Manual end-to-end test in Command Palette (especially deep linking into specific workspaces and launches).
- Verify that any existing user-saved deep links or command history continue to work.

---

### Risks & Trade-offs

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|----------|
| Breaking existing deep links or command history during migration | Low | High | Maintain full backward compatibility in Phase 1; add explicit versioned ID support if needed later |
| Router becomes a new god class | Medium | Medium | Keep handlers as separate small classes registered into the router (composition over inheritance) |
| Performance regression on command lookup | Low | Low | Use a `Dictionary<string, ...>` or compiled prefix tree for hot paths; measure before/after |
| Increased complexity for simple commands | Medium | Low | The compatibility layer + incremental migration keeps simple cases simple |
| Future CmdPal SDK changes to command model | Low | Medium | The registry pattern actually makes us more resilient to SDK evolution |

**Trade-off Summary**  
We accept a moderate increase in initial abstraction (one new service + descriptor type) in exchange for dramatically better maintainability, extensibility, and safety when adding new command types. This is the correct long-term trade-off for a feature-rich extension like QuickShell.

---

### Suggested Commit Structure

```
refactor(core): introduce CommandDescriptor + ICommandRouter to replace scattered TryParse* logic

- Add CommandKind enum and CommandDescriptor record
- Add ICommandRouter interface and initial CommandRouter implementation
- Wire router into QuickShellCommandsProvider via DI (#0001)
- Move Open/Launch workspace command creation behind router (Phase 1)
- Keep backward-compatible static surface during transition
- Add unit tests for parsing and command item creation
```

---

### Next Steps After This PR (Recommended Order)

1. **#0001** — DI + Composition Root (already proposed)
2. **#0002** — Persistence Hardening (atomic writes + schema version)
3. **This PR (#0003)** — Typed command routing / `CommandDescriptor` + Registry
4. **Next** — Registry pattern for `IProjectClassifier`, task suggesters, companion app detectors, and dev server discovery (the ~50 narrow service classes problem)
5. **Later** — Formalize `IDisposable` / background task / cancellation ownership across the extension lifetime
6. **Future** — Evaluate whether a lightweight companion WinUI settings window would reduce in-palette form complexity for heavy editing scenarios

---

**Final Recommendation**

This is the natural third foundational refactor after DI and persistence. Together, #0001 + #0002 + #0003 will give QuickShell a significantly more robust and evolvable core architecture while preserving (and improving) the excellent user experience.

The command routing layer is currently one of the most "change-sensitive" parts of the codebase. Hardening it now, while the number of command kinds is still manageable, is the right time.

---

**Would you like me to:**

1. Generate the **actual code files** for #0003 ( `CommandDescriptor.cs`, `CommandKind.cs`, `ICommandRouter.cs`, `CommandRouter.cs`, updated `QuickShellCommandsProvider.cs` sketch, and registration code for the DI container from #0001)?
2. Generate the code files for **#0001** and/or **#0002** first (so we have the foundation)?
3. Create a combined "Foundational Phase (0001+0002+0003)" planning document?
4. Adjust the scope or design of this 0003 proposal before generating code?

Just say the word and I’ll drop the next artifacts into `/home/workdir/artifacts/QuickShell/`.