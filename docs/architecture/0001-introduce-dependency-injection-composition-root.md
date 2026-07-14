# PR: Introduce Dependency Injection + Composition Root in `QuickShell.Core`

**Status:** Partial — see [proposal-status.md](./proposal-status.md) (DI composition landed; many statics remain)
**Type:** Foundational Refactor / Architectural Improvement  
**Priority:** P0  
**Estimated Effort:** Medium (~1–2 days focused work)  
**Target Branch:** `main`  
**Depends on:** None  
**Enables:** Command routing hardening, service consolidation, improved testability, future multi-provider support

---

## Summary

This PR introduces a proper **composition root** and **constructor dependency injection** into `QuickShell.Core`. It replaces the current reliance on a central `QuickShellRuntimeServices` hub (and scattered direct instantiation / static helpers) with clean, interface-driven service registration.

This is the single highest-leverage change identified in the July 2026 Architectural Audit. It dramatically improves testability, decoupling, and the ability to safely evolve the rest of the codebase (typed command routing, classifier registry, persistence hardening, etc.).

**Current state (fact-checked):**
- `Microsoft.Extensions.DependencyInjection` is **not** referenced anywhere in the solution.
- `QuickShellRuntimeServices` is a small **static** class in the **extension** project (`QuickShell/Services/`), holding `Settings`, `Shortcuts` (`ShortcutRepository`), and `Drafts`.
- Only `IShortcutRepository` and `IDraftStore` exist today (under `Services/`). Types like `TerminalLauncher`, `WorkspaceHealthCheck`, `WorkspaceMapper`, and `GitRepoIndex` are largely **static** helpers — interfaces for them are new work.
- `Abstractions/` and `Composition/` folders do not exist yet.

---

## Motivation

From the Architectural Audit:

> The current design relies heavily on a central `QuickShellRuntimeServices` static hub and direct instantiation / factory / static helper calls scattered across `QuickShellCommandsProvider`, pages, and many of the ~90 files under `QuickShell.Core/Services`. This creates hidden coupling, poor testability, and difficulty evolving command routing or adding new workspace behaviors.

**Problems addressed:**
- Hidden coupling across dozens of narrow helpers (many static)
- Inability to easily unit test `QuickShellCommandsProvider` and pages in isolation
- Risk of lifetime/ownership issues in a long-lived Command Palette extension host
- Difficulty adding new features without increasing entanglement
- Brittle foundation for the rest of the roadmap

---

## Goals

1. Establish a clean composition root inside `QuickShell.Core` that owns service lifetimes.
2. Make the most critical services injectable via well-defined interfaces (`IShortcutRepository`, `ITerminalLauncher`, `IWorkspaceHealthChecker`, etc.).
3. Allow `QuickShellCommandsProvider` (and future providers/pages) to be constructed cleanly without static access.
4. Make `QuickShell.Core.Tests` able to spin up realistic scenarios with test doubles.
5. Keep the public surface of `QuickShell.Core` small and intentional.

**Non-Goals (this PR):**
- Full service consolidation / registry pattern for classifiers (next PR)
- Replacing string-based command ID routing (`ShortcutCommandIds.TryParse*`)
- Changing persistence format or adding atomic writes
- Introducing a heavy DI container (we use `Microsoft.Extensions.DependencyInjection`)

---

## Proposed Design

### New Folder Structure (added)

```
QuickShell.Core/
├── Abstractions/                          # NEW — public contracts only
│   ├── IShortcutRepository.cs
│   ├── ITerminalLauncher.cs
│   ├── ITerminalProfileResolver.cs
│   ├── IWorkspaceHealthChecker.cs
│   ├── IWorkspaceGitOperations.cs
│   ├── IWorkspaceMapper.cs
│   ├── IGitRepoIndex.cs
│   └── ...
├── Composition/                           # NEW
│   ├── QuickShellServiceCollectionExtensions.cs   # Registration logic
│   └── QuickShellCompositionRoot.cs               # Optional explicit root (if needed)
├── Services/                              # Existing (refactored to implement interfaces)
│   ├── ShortcutRepository.cs            → implements IShortcutRepository
│   ├── TerminalLauncher.cs              → implements ITerminalLauncher
│   ├── WorkspaceHealthCheck.cs          → implements IWorkspaceHealthChecker
│   └── ...
└── QuickShellRuntime.cs                   # NEW lightweight facade (recommended)
```

### Key Interfaces (initial set)

| Interface                        | Implementation                  | Lifetime   | Notes |
|----------------------------------|---------------------------------|------------|-------|
| `IShortcutRepository`            | `ShortcutRepository`            | Singleton  | **Already exists**; add events in #0002 |
| `IDraftStore`                    | `ShortcutDraftStore`            | Singleton  | **Already exists** |
| `ITerminalLauncher`              | `TerminalLauncher` (instance)   | Singleton  | Today: `static` class — convert |
| `ITerminalProfileResolver`       | `TerminalProfileResolver`       | Singleton  | Convert if currently static/helpers |
| `IWorkspaceHealthChecker`        | `WorkspaceHealthCheck`          | Transient  | Today: static — convert |
| `IWorkspaceGitOperations`        | `WorkspaceGitOperations`        | Transient  | Convert |
| `IWorkspaceMapper`               | `WorkspaceMapper`               | Singleton  | Today: static — convert |
| `IGitRepoIndex`                  | `GitRepoIndex`                  | Singleton  | Today: static — convert |

Additional interfaces (`IProjectClassifier`, task suggestion providers, etc.) will be added in follow-up PRs using the same pattern.

### Composition Root Approach

**Recommended: `Microsoft.Extensions.DependencyInjection`**

Create:

```csharp
public static class QuickShellServiceCollectionExtensions
{
    public static IServiceCollection AddQuickShellCore(this IServiceCollection services)
    {
        // Repositories
        services.AddSingleton<IShortcutRepository, ShortcutRepository>();
        services.AddSingleton<IWorkspaceMapper, WorkspaceMapper>();

        // Terminal
        services.AddSingleton<ITerminalProfileResolver, TerminalProfileResolver>();
        services.AddSingleton<ITerminalLauncher, TerminalLauncher>();

        // Git & Health
        services.AddSingleton<IGitRepoIndex, GitRepoIndex>();
        services.AddTransient<IWorkspaceGitOperations, WorkspaceGitOperations>();
        services.AddTransient<IWorkspaceHealthChecker, WorkspaceHealthCheck>();

        // Future: Add more here (classifiers, etc.)
        return services;
    }
}
```

Then at extension / provider startup (today composition happens inside `QuickShellCommandsProvider` ctor via `new QuickShellSettingsManager` + `QuickShellRuntimeServices.Initialize`):

```csharp
var services = new ServiceCollection();
services.AddQuickShellCore();
// register extension-only types (settings manager, pages factories) as needed
var serviceProvider = services.BuildServiceProvider();

var provider = new QuickShellCommandsProvider(serviceProvider);
```

`QuickShellCommandsProvider` constructor changes from parameterless / self-wiring to:

```csharp
public QuickShellCommandsProvider(IServiceProvider services)
{
    _services = services;
    _repository = services.GetRequiredService<IShortcutRepository>();
    // etc.
}
```

Keep `QuickShellRuntimeServices` temporarily as a thin shim that reads from the same root instances if pages still call statics; delete once call sites are migrated.

A lightweight `QuickShellServices` facade can be introduced if constructor bloat appears in pages.

**Package requirement:** add `Microsoft.Extensions.DependencyInjection` (and abstractions) via central package management — not present today.

---

## Files Changed (High Level)

**High impact (will be modified):**
- `QuickShellCommandsProvider.cs` — constructor + `GetCommandItem` wiring
- `QuickShellExtension.cs` — service provider creation at startup
- `ShortcutRepository.cs`, `TerminalLauncher.cs`, `WorkspaceHealthCheck.cs`, etc. — implement new interfaces (mostly additive)
- Any page/list item factories doing `new FooService()` directly

**Low / no risk:**
- All domain models (`Workspace`, `TerminalShortcut`, etc.)
- JSON serialization context
- Most `*Form*` / `*Draft*` classes (can stay as-is for this PR)

---

## Migration Strategy (Incremental & Safe)

Because this is internal refactoring, we keep the app running throughout:

1. **Phase 1 (this PR)**: Extract the 6–7 core interfaces. Add `AddQuickShellCore()` extension. Wire `QuickShellCommandsProvider` to accept `IServiceProvider`. Keep existing static/factory paths working temporarily via a compatibility shim if needed.

2. **Phase 2 (immediate follow-up commit or same PR)**: Remove static access from provider and main pages. Delete shim.

3. **Phase 3 (subsequent PRs)**: Convert internal service-to-service calls to injected dependencies where beneficial.

Target: Land Phase 1 + 2 in one reviewable PR (< ~900 lines changed).

---

## Testing Strategy

- Unit tests for `QuickShellServiceCollectionExtensions` (verify registrations and lifetimes)
- Existing `QuickShell.Core.Tests` continue to pass
- Add 2–3 integration-style tests that construct a real `ServiceProvider` and exercise key paths (`IShortcutRepository` + `ITerminalLauncher`)
- Manual end-to-end verification in Command Palette (launch, edit, health checks)

---

## Risks & Trade-offs

| Risk                              | Likelihood | Impact | Mitigation |
|-----------------------------------|------------|--------|------------|
| Constructor bloat in pages        | Medium     | Medium | Introduce `QuickShellServices` facade or keyed services if needed |
| Lifetime mistakes (transient captured in singleton) | Medium | High | Clear documentation in the extensions class + simple rules |
| Temporary complexity during migration | High | Low | Keep PR focused, use clear commit messages, good PR description |
| Some services currently static-heavy | Medium | Medium | Convert them to instance + interface as part of this work |

**Trade-off Summary**  
We accept modest upfront ceremony (interfaces + registration) in exchange for dramatically better decoupling, testability, and long-term maintainability. This is the correct trade-off for a project of this complexity and feature richness.

---

## Commit Message (suggested)

```
refactor(core): introduce composition root and dependency injection for core services

- Add Abstractions/ folder with IShortcutRepository, ITerminalLauncher, IWorkspaceHealthChecker, etc.
- Add Composition/QuickShellServiceCollectionExtensions.cs
- Wire QuickShellCommandsProvider via IServiceProvider
- Update key services to implement interfaces (additive)
- Add basic lifetime documentation and usage guidance
```

---

## Post-PR Roadmap (Recommended Order)

1. **This PR** — DI + Composition Root (foundational)
2. **Next PR** — Persistence hardening (atomic writes + schema version) — now easy to inject/test
3. **Next PR** — Typed command routing / `CommandDescriptor` system (replace brittle `ShortcutCommandIds.TryParse*`)
4. **Following PRs** — Registry pattern for `IProjectClassifier` / task suggesters / companion detectors
5. **Later** — Formal `IDisposable` / cancellation ownership + expanded test coverage

---

## How to Review This PR

- Focus on the new `Abstractions/` contracts and the registration extension method.
- Verify that `QuickShellCommandsProvider` no longer has hidden static dependencies.
- Check that existing functionality is preserved during the migration window.
- Confirm testability improvement (can you now mock `IShortcutRepository` easily?).

---

**Ready for implementation.**  
Once this lands, the rest of the audit findings become much easier and safer to address.

---

*Generated as part of the QuickShell Architectural Audit (July 2026)*  
*Fact-checked: no DI today; RuntimeServices is extension-static; only IShortcutRepository/IDraftStore exist.*  
*Principal Software Architect Review*
