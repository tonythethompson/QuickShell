# PR Proposal: 0005

**Status:** Partial — see [proposal-status.md](./proposal-status.md)

**Title:**  
Formalize `IDisposable` / `IAsyncDisposable` Ownership + Cancellation Token Discipline + Expanded Test Coverage

**PR Type:** Foundational / Reliability & Maintainability  
**Priority:** P1 (High — becomes increasingly important as the extension matures)  
**Estimated Size:** Medium  
**Depends On:** #0001 (Dependency Injection + Composition Root) — strongly recommended  
**Enables:** Safer long-running extension behavior, easier debugging of lifetime issues, confident refactoring of background work, and significantly higher test confidence  
**Blocks / Related:** Improves safety of #0002 (persistence), #0003 (command routing), and #0004 (service consolidation)

---

### Motivation (from Architectural Audit)

The original audit flagged **lifecycle and resource ownership** as a Medium-to-High risk area:

> "State & lifecycle management in a long-lived extension host (statics, draft stores, git index, file-backed settings).  
> Formalize extension lifecycle & resource ownership (dispose chains, file watchers if any, git index lifetime, background task cancellation)."

QuickShell runs inside the PowerToys Command Palette host, which is a long-lived process. The extension can be enabled/disabled, the host can restart, and users can keep the palette open for hours. **Fact-checked current state:**

- `QuickShellExtension` uses `ManualResetEvent` dispose signaling; it does **not** yet own a root `CancellationTokenSource`.
- `QuickShellCommandsProvider` already implements `IDisposable` and calls `QuickShellRuntimeServices.Dispose()` (disposes drafts + repository).
- `ShortcutRepository` and `SearchDebouncer` implement disposal (timer/mutex/etc.).
- Background work (`GitRepoIndex` pre-warm via `Task.Run`, best-effort catches) often **does not** observe a shared cancellation token.
- There are **no** `FileSystemWatcher`s in the repository today.
- As we add more services via DI (#0001) and registries (#0004), risk of leaked resources or tasks continuing after disable increases.
- Test coverage exists but is mostly unit-level; integration tests for lifetime/disposal are thin.

Without disciplined ownership, we risk:
- Resource leaks (file handles, git processes)
- Crashes or hangs on host shutdown
- Flaky or hard-to-reproduce bugs
- Difficulty reasoning about "what happens when the user disables QuickShell?"

This PR makes resource ownership **explicit, testable, and enforced by construction**.

---

### Goals

1. Establish a clear, documented ownership model for all long-lived services in `QuickShell.Core`.
2. Ensure every service that holds resources (file watchers, caches, background tasks, git index, etc.) properly implements `IDisposable` or `IAsyncDisposable`.
3. Propagate a root `CancellationToken` from `QuickShellExtension` → `QuickShellCommandsProvider` → all background work.
4. Make the DI container (from #0001) the source of truth for lifetimes (`AddSingleton`, `AddScoped`, disposal via `ServiceProvider`).
5. Significantly expand test coverage, especially:
   - Lifetime and disposal behavior
   - Cancellation propagation
   - Integration-style tests using a real `IServiceProvider`
6. Provide clear guidance (and ideally analyzer enforcement) so future contributors follow the same patterns.

**Non-Goals (for this PR)**
- Introducing a full actor model or complex orchestration framework.
- Changing the persistence format or command routing (those are separate PRs).
- Adding file watchers if they don't already exist (none today; this PR formalizes whatever exists).
- Perfect test coverage of every edge case — focus on high-value paths first.
- Claiming WebView2 is an active dependency — it is only a central PackageVersion pin today; unrelated to disposal.

---

### Proposed Design

#### 1. Core Concepts

| Concept                    | Description                                                                 | Where it lives                  |
|---------------------------|-----------------------------------------------------------------------------|---------------------------------|
| **Root Lifetime**         | A single `CancellationTokenSource` tied to the extension's lifetime        | `QuickShellExtension` → passed down |
| **Owned Services**        | Services that implement `IDisposable` / `IAsyncDisposable`                 | Registered with proper lifetime in DI |
| **Background Work**       | Any `Task` started with `Task.Run` or `Task.Factory.StartNew`              | Must accept and observe a `CancellationToken` |
| **Disposal Graph**        | Explicit parent → child ownership (provider owns pages, pages own services they create) | Documented + enforced via DI scopes where possible |

#### 2. Key Changes

**A. `QuickShellExtension` (entry point)**
- Create a root `CancellationTokenSource` on construction.
- On `Dispose()`, cancel the token, then dispose the `IServiceProvider` (or the root scope).
- Pass the `CancellationToken` (or a `QuickShellLifetime` wrapper) down to `QuickShellCommandsProvider`.

**B. `QuickShellCommandsProvider`**
- Accept `CancellationToken` (or `QuickShellLifetime`) in constructor.
- Implement `IDisposable` / `IAsyncDisposable`.
- When the provider is disposed (or when `GetCommandItem` / page creation happens after shutdown), respect the token.

**C. Long-lived Services (examples)**
- `GitRepoIndex` → implement `IDisposable`, accept `CancellationToken` for pre-warm and refresh work.
- `TerminalCatalog` / `TerminalProfileResolver` → implement `IDisposable` if they hold caches or watchers.
- `SearchDebouncer` → ensure internal timers/tasks are cancellable.
- Any service that starts background `Task`s must expose a way to cancel them.

**D. DI Registration (in `QuickShellServiceCollectionExtensions`)**
```csharp
services.AddSingleton<GitRepoIndex>();           // Will be disposed by container if it implements IDisposable
services.AddSingleton<ITerminalLauncher, TerminalLauncher>();
// For services that need the root token:
services.AddSingleton<QuickShellLifetime>(sp => new QuickShellLifetime(rootToken));
```

**E. `QuickShellLifetime` (new lightweight type)**
A simple record or class that holds:
- `CancellationToken Token { get; }`
- `void RequestShutdown()` (for tests or explicit shutdown)
- Optional `IAsyncDisposable` support

This avoids passing raw `CancellationTokenSource` everywhere.

#### 3. Recommended Patterns (to be documented in a new `docs/lifetime-and-disposal.md`)

1. **Prefer constructor injection** of `CancellationToken` or `QuickShellLifetime` over statics or ambient context.
2. **Every background `Task`** must be created with `Task.Run(..., cancellationToken)` or `CreateLinkedTokenSource`.
3. **Services that start work on construction** should do so lazily or in a controlled `StartAsync` method.
4. **Use `IAsyncDisposable`** for services that do async cleanup (e.g., flushing logs, closing git processes cleanly).
5. **Tests** should create a `ServiceProvider`, resolve the root services, perform work, then dispose the provider and assert no leaks.

---

### Impact on Existing Code

**Files that will change significantly:**

| File / Area                        | Change Type          | Notes |
|------------------------------------|----------------------|-------|
| `QuickShellExtension.cs`           | Moderate             | Add root `CancellationTokenSource`, pass lifetime down |
| `QuickShellCommandsProvider.cs`    | Moderate             | Accept lifetime token, implement `IDisposable` |
| `GitRepoIndex.cs`                  | Moderate             | Add `IDisposable`, make pre-warm cancellable |
| `TerminalCatalog.cs` / resolvers   | Low–Moderate         | Audit for resources; implement `IDisposable` if needed |
| `SearchDebouncer.cs`               | Low                  | Ensure timer/task cancellation |
| `ShortcutRepository.cs`            | Low                  | Already benefits from #0002; ensure file handles are owned |
| `QuickShell.Core.Tests`            | High (new tests)     | Add lifetime, disposal, and integration test fixtures |
| `QuickShellServiceCollectionExtensions.cs` | Low             | Add registration for `QuickShellLifetime` |

Most domain models and pure functions remain untouched.

---

### Migration / Rollout Strategy (Incremental & Safe)

**Phase 1 (this PR)**
- Introduce `QuickShellLifetime` and root token in `QuickShellExtension`.
- Make `QuickShellCommandsProvider` disposable and token-aware.
- Update the top 3–4 most critical background services (`GitRepoIndex`, health check paths, debouncer).
- Add disposal to the DI container (the container will automatically dispose `IDisposable` singletons when the root provider is disposed).
- Document the expected patterns in `docs/lifetime-and-disposal.md`.

**Phase 2 (immediate follow-up or same PR if small)**
- Audit remaining services for hidden resources.
- Convert existing `Task.Run` / fire-and-forget patterns to token-aware versions.
- Add basic lifetime tests.

**Phase 3 (later)**
- Add analyzer rules or architecture tests that forbid undisposed background work.
- Expand to any new file watchers or long-running processes.

We keep the extension fully functional throughout — disposal is additive and defensive.

---

### Testing Strategy

This PR **significantly expands test coverage** as a primary goal:

1. **New test fixture** — `QuickShellTestHost` or similar that creates a real `ServiceProvider`, starts the provider, and provides easy access to `QuickShellLifetime` for controlled shutdown.
2. **Lifetime tests**
   - Creating and disposing the full provider graph does not throw or leak.
   - Cancelling the root token causes background work (git pre-warm, debounced tasks) to stop promptly.
3. **Disposal tests**
   - Services that implement `IDisposable` are actually disposed by the container.
   - Multiple create/dispose cycles are safe (important for host restart scenarios).
4. **Integration tests**
   - End-to-end: Create provider → request a page → trigger background work → dispose → assert clean shutdown.
5. **Existing tests** continue to pass (they should require minimal or no changes if we are careful with defaults).

Target: Add 15–25 new focused tests in this PR.

---

### Risks & Trade-offs

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|----------|
| Over-engineering disposal for simple services | Medium | Low | Start with only the services that actually hold resources or start background work. Keep simple services as pure classes. |
| Breaking changes to constructor signatures | Medium | Medium | Use optional parameters or a `QuickShellLifetime?` that defaults to a "no-op / infinite" token during migration. |
| Tests become slower due to real `ServiceProvider` usage | Low | Low | Keep most new tests lightweight; use `ServiceCollection` + `BuildServiceProvider()` which is fast for this size. |
| Developers forget to pass / observe the token | Medium | High | Documentation + code review checklist + (later) analyzer. Make the happy path obvious. |
| Temporary complexity during migration | High | Low | Keep PR focused. Provide clear before/after examples in the PR description. |

**Trade-off Summary**  
We accept a modest increase in constructor parameters and a new lightweight `QuickShellLifetime` type in exchange for **dramatically clearer ownership semantics**, safer shutdown behavior, and the ability to write confident integration tests. This is the correct long-term trade-off for a Command Palette extension that runs inside a host process.

---

### Suggested Commit Structure

```
refactor(core): formalize IDisposable / IAsyncDisposable ownership and cancellation discipline

- Introduce QuickShellLifetime + root CancellationTokenSource in QuickShellExtension
- Make QuickShellCommandsProvider disposable and token-aware
- Update GitRepoIndex, SearchDebouncer, and key background services to respect cancellation
- Add QuickShellLifetime to DI container
- Add docs/lifetime-and-disposal.md with patterns and examples
- Expand QuickShell.Core.Tests with lifetime, disposal, and integration tests
```

---

### Position in the Overall Roadmap

This PR sits cleanly after the first four foundational PRs:

| Order | PR | Focus | Dependency | Status |
|-------|----|-------|------------|--------|
| 1 | 0001 | Dependency Injection + Composition Root | None | Proposed |
| 2 | 0002 | Persistence Hardening (atomic + schema) | #0001 | Proposed |
| 3 | 0003 | Typed Command Routing (`CommandDescriptor`) | #0001 | Proposed |
| 4 | 0004 | Service Consolidation / Registry Pattern | #0001 | Proposed |
| **5** | **0005** | **Formal IDisposable / Cancellation + Expanded Tests** | **#0001** (strong) | **This PR** |

After #0005, the codebase will have:
- Clean DI + composition
- Reliable persistence
- Robust command routing
- Extensible service discovery via registry
- Explicit, testable resource ownership and shutdown behavior

This puts QuickShell in an excellent position for future growth (companion UI, more providers, NuGet packaging of Core, etc.).

---

### Final Recommendation

This is not the most glamorous PR, but it is one of the most important for long-term reliability. Command Palette extensions live in a hostile environment (long-lived host, frequent enable/disable, user-driven lifetime). Making ownership explicit now prevents an entire class of hard-to-debug production issues later.

I am happy to generate the concrete code files for this PR (interfaces, `QuickShellLifetime`, updated `QuickShellExtension` + provider, test fixtures, and documentation stub) whenever you're ready.

---

**Would you like me to generate the actual code files for #0005 now?**  
Or would you prefer the code files for any of the earlier PRs (0001–0004) first? Or a combined "Foundational Phase 1–5 Roadmap" summary document?

Just say the word and I’ll generate the next artifacts under `docs/architecture/` or an implementation branch.

---

*Fact-checked July 2026: provider/runtime dispose exists; no root CTS yet; no FileSystemWatchers; WebView2 unused pin.*