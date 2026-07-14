# PR Proposal: 0004 — Consolidate Discovery / Classifier / Task Suggestion Helpers via Registry + Plugin Pattern

**Title**  
Consolidate Discovery / Classifier / Task Suggestion Helpers (~90 `Services/` files; start with the intelligence cluster) into a Registry + Plugin Architecture

**PR Type**  
Architectural Refactor / Service Consolidation  
**Priority**  
P1 (High — directly addresses one of the largest maintainability risks identified in the audit)  
**Estimated Size**  
Large (but can be landed incrementally in 2–3 focused PRs if needed)  
**Depends On**  
- **#0001** (Dependency Injection + Composition Root) — **Strongly recommended**  
- #0002 (Persistence Hardening) — Beneficial but not required  
- #0003 (Typed Command Routing) — Loosely related; can proceed in parallel

**Enables**  
- Clean addition of new project classifiers, task suggesters, companion detectors, and future intelligence features  
- Dramatic reduction in hidden coupling between narrow `*Discovery` and `*Action` classes  
- Much easier onboarding for new contributors  
- Foundation for future `IWorkspaceActionProvider` or pluggable workspace intelligence

---

## Motivation (from Architectural Audit)

The audit identified a **High-severity** structural problem:

> **~90 files under `QuickShell.Core/Services`** (`*Discovery`, `*Actions`, `*Form*`, `CompanionApp*`, `DevServerUrlDetection`, `DockerComposeDiscovery`, package.json / `.csproj` / Taskfile detection inside `ProjectClassifier` and related helpers, `TaskTypeCommandSuggestion`, etc.). Many are `static`. This creates high cognitive load, risk of duplicated logic, and makes the “big picture” of how QuickShell understands a project folder hard to hold in one head.

This is classic **service explosion**. While each file often follows Single Responsibility on the surface, the **collective** design has poor cohesion at the architectural level. Adding a new project type, task suggestion, or companion app currently requires touching multiple places and understanding implicit contracts.

**Naming note:** Prefer real type names from the tree (`CompanionAppDetection`, `CompanionAppCatalog`, `ProjectClassifier`) — there is no standalone `PackageJsonClassifier` / `CompanionAppDetector` class.

This PR introduces a **registry + plugin pattern** (compile-time DI registration) so new intelligence capabilities become simple registrations rather than invasive changes.

---

## Goals

1. Establish a clean, extensible `IProjectClassifier` + `ITaskSuggestionProvider` abstraction layer.
2. Introduce a central `ProjectAnalysisRegistry` (or `IProjectAnalysisService`) that discovers and orchestrates registered implementations via dependency injection.
3. Consolidate related discovery logic where it makes sense (e.g., one `ProjectLayoutAnalyzer` instead of 8 separate file scanners).
4. Make adding new classifiers, task suggesters, or companion detectors a **one-line registration** task.
5. Significantly reduce cognitive load and hidden coupling across the `Services/` folder.
6. Preserve (and improve) the excellent project intelligence features that users love.

**Non-Goals (for this PR)**
- Full consolidation of *all* 50 services in a single PR (too risky and hard to review).
- Changing the public behavior or output of existing classifiers.
- Introducing a heavy plugin system with dynamic loading (MEF/Assembly scanning). We stay with compile-time DI registration.
- Touching form/draft infrastructure or command routing (those are separate concerns).

---

## Proposed Design

### New High-Level Abstractions

```csharp
// Core classification contract
public interface IProjectClassifier
{
    string Name { get; }
    int Priority { get; }                    // Higher = evaluated first
    ProjectType? Classify(string rootPath, ProjectLayout layout);
}

// Task / action suggestion contract
public interface ITaskSuggestionProvider
{
    string Name { get; }
    IEnumerable<TaskSuggestion> GetSuggestions(
        Workspace workspace,
        ProjectLayout layout,
        CancellationToken ct = default);
}

// Optional but recommended: unified project layout snapshot
public sealed record ProjectLayout(
    IReadOnlyList<string> Files,
    IReadOnlyList<string> Directories,
    bool HasGit,
    bool HasDockerCompose,
    bool HasPackageJson,
    bool HasCsproj,
    bool HasTaskfile,
    // ... other cheap-to-compute signals
    string? PrimaryLanguage
);
```

### Central Orchestrator / Registry

```csharp
public interface IProjectAnalysisService
{
    ProjectType? DetectProjectType(string rootPath);
    IReadOnlyList<TaskSuggestion> GetTaskSuggestions(Workspace workspace);
    // Future: GetCompanionApps, GetDevServerUrls, etc.
}

internal sealed class ProjectAnalysisService : IProjectAnalysisService
{
    private readonly IEnumerable<IProjectClassifier> _classifiers;
    private readonly IEnumerable<ITaskSuggestionProvider> _suggestionProviders;
    private readonly IProjectLayoutAnalyzer _layoutAnalyzer; // optional consolidation point

    public ProjectAnalysisService(
        IEnumerable<IProjectClassifier> classifiers,
        IEnumerable<ITaskSuggestionProvider> suggestionProviders,
        IProjectLayoutAnalyzer? layoutAnalyzer = null)
    {
        _classifiers = classifiers.OrderByDescending(c => c.Priority);
        _suggestionProviders = suggestionProviders;
        _layoutAnalyzer = layoutAnalyzer ?? new DefaultProjectLayoutAnalyzer();
    }

    // Implementation that runs classifiers in priority order and aggregates suggestions
}
```

### Recommended Folder Structure (after this work)

```
QuickShell.Core/
├── Abstractions/
│   ├── Classification/
│   │   ├── IProjectClassifier.cs
│   │   ├── ITaskSuggestionProvider.cs
│   │   ├── ICompanionAppDetector.cs          (if kept separate)
│   │   └── IDevServerDetector.cs
│   └── ...
├── Classification/
│   ├── ProjectAnalysisService.cs
│   ├── ProjectAnalysisRegistry.cs            (or just use DI IEnumerable<T>)
│   ├── ProjectLayoutAnalyzer.cs              (new consolidated analyzer)
│   └── Classifiers/
│       ├── DotNetProjectClassifier.cs
│       ├── NodeProjectClassifier.cs
│       ├── DockerComposeClassifier.cs
│       ├── TaskfileClassifier.cs
│       └── ...
│   └── TaskSuggestions/
│       ├── DotNetTaskSuggestionProvider.cs
│       ├── NodeTaskSuggestionProvider.cs
│       ├── DockerTaskSuggestionProvider.cs
│       └── ...
├── Services/                                 (existing — will be gradually slimmed)
│   ├── ProjectClassifier.cs                  → moved / refactored into above
│   ├── TaskTypeCommandSuggestion.cs
│   ├── DockerComposeDiscovery.cs
│   ├── ... (many files become implementations of the new interfaces)
```

### Two Viable Registry Approaches

| Approach | Description | Recommendation |
|----------|-------------|----------------|
| **Pure DI `IEnumerable<T>`** (Recommended) | All implementations of `IProjectClassifier` are automatically injected via constructor. No extra registry class needed. | **Preferred** for this codebase. Simple, idiomatic .NET, zero magic. |
| **Explicit `ProjectAnalysisRegistry`** | Central class that holds a list of registered providers and allows runtime registration/unregistration. | Only if you need dynamic registration at runtime (unlikely for this use case). |

**Recommendation**: Use the **pure DI `IEnumerable<T>` approach** + a thin orchestrator service. It is the simplest, most testable, and most maintainable pattern once #0001 (DI) is in place.

---

## Impact on Existing Code

**High-impact areas that will change:**

- `ProjectClassifier.cs`, `TaskTypeCommandSuggestion.cs`, `DockerComposeDiscovery.cs`, companion/dev-server helpers (`CompanionAppDetection`, `DevServerUrlDetection`, …) — refactored into implementations of the new interfaces. Package.json / csproj signals live inside `ProjectClassifier` today; extract classifiers without inventing fictional type names.
- Any code that currently calls these helpers directly will go through `IProjectAnalysisService` instead.
- `QuickShellCommandsProvider` and page factories that build task suggestions will consume the new unified service.

**Low-risk areas:**

- Domain models (`Workspace`, `TerminalShortcut`, etc.) — unchanged.
- Persistence, launching, git operations, health checks — untouched by this PR.
- In-palette form editing — untouched.

---

## Migration / Rollout Strategy (Critical — Must Be Incremental)

Because there are ~90 files under `Services/` (not all of which are classifiers — forms/persistence/launch make up a large share), we **must not** attempt a big-bang refactor. Scope this PR to the intelligence/classification cluster only.

**Recommended Phased Approach:**

**Phase 1 (This PR — Foundation)**
- Introduce the core interfaces (`IProjectClassifier`, `ITaskSuggestionProvider`, `ProjectLayout` record).
- Create `IProjectAnalysisService` + `ProjectAnalysisService` implementation.
- Wire it via DI in `QuickShellServiceCollectionExtensions`.
- Migrate the **top 5–6 heaviest / most duplicated** classifiers first:
  1. Core project type detection (currently spread across multiple files)
  2. Task suggestion generation
  3. Docker Compose detection + actions
  4. Node / package.json detection
  5. .NET / .csproj detection
  6. Taskfile / Makefile detection

**Phase 2 (Immediate follow-up PR)**
- Migrate Companion App detection
- Migrate Dev Server / URL detection
- Consolidate remaining file scanners into `ProjectLayoutAnalyzer`

**Phase 3 (Later)**
- Evaluate whether some `*Form*` or action-related classes can also benefit from a similar `IWorkspaceActionProvider` pattern.
- Delete old direct service classes once all call sites are migrated.

This keeps every PR reviewable and the extension functional at every step.

---

## Testing Strategy

- Unit tests for `ProjectAnalysisService` using test implementations of `IProjectClassifier` and `ITaskSuggestionProvider`.
- Golden-file or snapshot tests for `ProjectLayoutAnalyzer` against known project folder structures (dotnet, node, docker, mixed, etc.).
- Integration test that constructs a real `ServiceProvider`, registers several classifiers, and verifies correct ordering + aggregation.
- Verify that existing workspace intelligence behavior is **unchanged** from the user’s perspective (critical for a productivity tool).

---

## Risks & Trade-offs

| Risk / Concern | Likelihood | Impact | Mitigation / Trade-off |
|----------------|------------|--------|------------------------|
| Migration takes longer than expected because of hidden dependencies between old services | Medium | Medium | Strict incremental approach + good characterization tests before touching each classifier |
| Some developers prefer the “obvious file name” model over the registry pattern | Low | Low | The new structure is still very discoverable (`Classification/Classifiers/`). Document the mental model clearly in `docs/architecture.md` |
| Over-abstraction too early (creating interfaces for things that don’t need them) | Medium | Low | Start only with the two primary contracts (`IProjectClassifier` + `ITaskSuggestionProvider`). Add more only when real duplication appears |
| Performance regression from running many classifiers on every folder | Low | Medium | Classifiers must be **very cheap** (no I/O if possible). Use `ProjectLayout` snapshot + early exit. Profile before/after |
| Loss of some fine-grained control if everything goes through one orchestrator | Low | Low | The orchestrator is thin. Individual classifiers still have full power. We are not removing capability — we are organizing it |

**Overall Trade-off Summary**  
We accept a moderate increase in initial abstraction and migration effort in exchange for **dramatically better long-term maintainability, extensibility, and reduced cognitive load**. For a productivity tool that will continue to gain new project type support and intelligence features, this is the correct architectural direction.

---

## Suggested Commit Structure

```
refactor(classification): introduce IProjectClassifier + ITaskSuggestionProvider + ProjectAnalysisService

- Add core classification abstractions under Abstractions/Classification/
- Implement ProjectAnalysisService as DI-friendly orchestrator
- Migrate top 6 classifiers (dotnet, node, docker, taskfile, core type detection, task suggestions)
- Wire via AddQuickShellCore() in service collection extensions
- Add unit + golden-file tests for the new analysis pipeline
- Update call sites in QuickShellCommandsProvider and pages
```

---

## Next Steps After This PR (Recommended Order)

1. **This PR (#0004)** — Establish registry pattern + migrate first wave of classifiers
2. **Follow-up PR** — Complete migration of Companion App + Dev Server detectors + delete old direct classes
3. **Later** — Consider introducing `IWorkspaceActionProvider` for the action side (similar pattern)
4. **Documentation** — Add a clear “How Project Intelligence Works” section to `docs/architecture.md`
5. **Future** — If QuickShell ever supports third-party extensions, this registry becomes the natural extension point

---

## Final Recommendation

This is one of the most important maintainability improvements QuickShell can make right now. The current explosion of narrow discovery classes is the single biggest source of “I’m afraid to touch this area” friction in the codebase.

By introducing a clean `IProjectClassifier` / `ITaskSuggestionProvider` + DI-powered orchestrator, we turn a liability into a strength: QuickShell’s project intelligence becomes **explicitly extensible** instead of implicitly tangled.

**Do this work.** It will pay dividends every time someone adds support for a new project type, task runner, or companion tool in the future.

---

**Ready for implementation.**  
I can generate the actual code files for this PR (interfaces + `ProjectAnalysisService` + first-wave migrated classifiers + DI registration) whenever you want.

This pairs with #0001 (DI) and positions QuickShell for sustainable growth in project/workspace intelligence.

---

*Fact-checked July 2026: ~90 Services files; no PackageJsonClassifier type; CompanionAppDetection naming.*