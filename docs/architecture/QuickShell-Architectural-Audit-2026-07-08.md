# QuickShell Architectural Audit
**Principal Software Architect Review** — July 2026

QuickShell is a feature-rich PowerToys Command Palette extension (with PowerToys Run and experimental Raycast adapters) that enables terminal-centric users to manage “workspaces” (project folders + multi-launch configurations), launch them in any discovered terminal profile (Windows Terminal, WSL, classic shells, Intelligent Terminal), handle git worktrees/branches, perform health checks, and edit everything in-palette. It is built on .NET 10 with the Microsoft.CommandPalette.Extensions SDK (v0.11.x), MSIX packaging, AOT/trimming, and a cleanly separated `QuickShell.Core` library.

---

## Executive Summary

### Overall Architectural Health: **B+ / Solid but Complex**

The foundation is strong:
- Clear project separation (`QuickShell.Core` is dependency-free and reusable by Run/Raycast)
- Modern .NET practices (source-generated JSON, AOT, single-file publish, CsWinRT interop)
- Faithful CmdPal SDK patterns (`IExtension` → `CommandProvider` → dynamic `GetCommandItem` + pages)
- Rich domain model that directly supports the product vision
- Pragmatic persistence (JSON in `%LOCALAPPDATA%\QuickShell`)
- Sophisticated terminal discovery/launching
- Visible attention to startup performance (tracing, pre-warm, lazy loading)

### Biggest Risks

1. **Complexity debt** from ~50 service classes in `QuickShell.Core/Services` (many narrow *Discovery*, *Action*, *Form*, and *Git* classes). This creates hidden coupling and makes the “big picture” hard to hold in one head.
2. **Brittle command routing** via string ID parsing (`ShortcutCommandIds.TryParse*`) and heavy reliance on a central `QuickShellRuntimeServices` (likely static-heavy).
3. **State & lifecycle management** in a long-lived extension host (statics, draft stores, git index, file-backed settings).
4. **In-palette editing surface** (forms, drafts, undo/redo, Adaptive Card or custom form JSON) is powerful but adds significant surface area and maintenance cost.
5. JSON persistence lacks explicit schema versioning, atomic writes, or robust concurrency guards.

### Opportunities

The `QuickShell.Core` library is an excellent asset. With modest refactoring it can become a reusable “workspace launch engine” for future CmdPal extensions or even a standalone companion app. The health-check + git + project-classification logic is genuinely differentiated.

### Top 5 Priority Improvements (ranked by impact × effort)

1. **Introduce a proper composition root + dependency injection** in Core (and wire it into the CmdPal provider) — highest leverage for testability and decoupling.
2. **Replace/harden string-based command ID routing** with a typed command registry or stronger factory + visitor pattern.
3. **Consolidate or registry-ize the explosion of *Discovery/*Classifier/*Action services** (project type detection, task suggestions, companion apps, dev-server detection).
4. **Strengthen the persistence layer** (atomic writes, schema version, backup on migration, clearer `IShortcutRepository` contract with events).
5. **Formalize extension lifecycle & resource ownership** (dispose chains, file watchers if any, git index lifetime, background task cancellation).

---

## Detailed Findings

### 1. Overall Architecture & Plugin Design

**Strengths**
- `QuickShellExtension : IExtension, IDisposable` is minimal and correct. It only exposes `ProviderType.Commands` via `QuickShellCommandsProvider` and uses the standard `ManualResetEvent` dispose signaling pattern expected by the host.
- `QuickShellCommandsProvider : CommandProvider` follows SDK idioms well: static top-level `CommandItem` → `QuickShellPage`, context commands for Create/Settings/Undo-Redo, lazy fallback page, and `GetCommandItem(string id)` for deep linking into specific workspaces/launches.
- Excellent project separation: `QuickShell.Core` has **zero** dependency on CmdPal SDK or UI. `QuickShell.Run` is a thin keyword adapter. `QuickShell.Raycast` proves reusability.
- MSIX + Package.appxmanifest + modern publish settings (AOT, trimming, single-file) are appropriate for a CmdPal extension.

**Issues**

| Severity | Evidence | Impact | Recommended Fix | Trade-offs |
|----------|----------|--------|------------------|------------|
| **High** | `QuickShellCommandsProvider` constructor and `GetCommandItem` do heavy string ID parsing and delegate to many `QuickShell.*` factories/services. No visible DI container. | Hard to unit-test providers/pages in isolation; adding new command types or pages increases coupling. | Introduce `IServiceProvider` / simple composition root in Core. Register services, repositories, and factories. Wire from `Program.cs` or extension startup. Expose a `QuickShellServices` facade for the provider. | Small upfront cost; big win for testability and future multi-provider scenarios. Prefer constructor injection over static `QuickShellRuntimeServices`. |
| **Medium** | Only `Commands` provider is implemented; `GetProvider` returns `null` for Settings/Navigation/etc. | Misses CmdPal extension points that could provide richer settings UI or navigation. | Implement additional providers (`ISettingsProvider`, `INavigationProvider`) if/when the SDK surface grows, or keep focused if current page-based approach suffices. | Avoid over-engineering; current design is pragmatic. |
| **Low** | Partial class on `QuickShellExtension` + GUID attribute. | Minor — suggests possible generated code or future COM registration needs. | Document the partial class intent. Ensure GUID is stable across versions. | Negligible. |

### 2. Core Functionality & Data Flow

**Strengths**
- Rich, well-named domain: `Workspace` / `WorkspaceEntry` / `TerminalShortcut` / `WorkspaceTaskAction`, separate `WorkspaceDiskRecord`.
- Dedicated `ShortcutRepository` + `IShortcutRepository` + `WorktreeBranchTargetStore` + `WorkspaceMapper` + legacy migration.
- Sophisticated launch path: `TerminalLauncher` + `ShortcutLaunchExecutor` + `TerminalProfileResolver` + `TerminalCatalog` + git checkout gate (`WorkspaceGitLaunchGate`, `WorkspaceGitOperations`).
- Health checks (`WorkspaceHealthCheck`, `ShortcutHealth`, port/process signals, git dirty state) and diagnostics report are user-visible value.
- Project classification + quick command suggestion (`ProjectClassifier`, `TaskTypeCommandSuggestion`, `DockerComposeDiscovery`, `Package.json` / `.csproj` / Taskfile detection) is clever and productivity-enhancing.
- Pre-warm of `GitRepoIndex` + `SearchDebouncer` + startup tracing show performance awareness.

**Issues**

| Severity | Evidence | Impact | Recommended Fix | Trade-offs |
|----------|----------|--------|------------------|------------|
| **High** | ~50 narrowly scoped service files (`*Discovery`, `*Actions`, `*Form*`, `CompanionApp*`, `DevServerUrlDetection`, etc.). Many appear to be static or tightly interdependent. | High cognitive load; risk of duplicated logic (e.g., multiple places discovering package.json or docker-compose); difficult to evolve consistently. | Create a small registry or plugin-style `IProjectClassifier` / `ITaskSuggestionProvider` interface. Register implementations centrally. Consolidate related discovery into cohesive services (e.g., one `ProjectLayoutAnalyzer`). | Loses some “obviousness” of file names; gains coherence. Do incrementally — start with the heaviest used classifiers. |
| **High** | Command routing via `ShortcutCommandIds.TryParseOpen` / `TryParseOpenLaunch` etc. + factories (`ShortcutListItems.CreateOpen`, `ShortcutTaskActionListItems`). | Fragile to refactoring; IDs become part of public contract; hard to add versioning or new command kinds. | Introduce a typed command descriptor or use SDK `Command` / `ICommandItem` more directly with stable IDs + payload objects. Or adopt a small command router with pattern matching. | More types initially; far more robust long-term. |
| **Medium** | JSON persistence (`shortcuts.json`, `worktree-branch-targets.json`, `settings.json`) via source-generated `QuickShellJsonContext`. Separate files for branch targets. Legacy migration exists. | No visible atomic write / temp-file + rename pattern; no explicit schema version header; potential for partial writes or concurrent modification (though rare for single-user desktop). | Wrap writes in `ShortcutRepository` with atomic helper (temp file + `File.Replace` or `File.Move` with retry). Add a top-level `version` field + migration pipeline. Expose `Changed` events. | Small perf cost on writes; big reliability win. |
| **Medium** | `QuickShellRuntimeServices` (inferred central hub) + draft stores (`ShortcutDraftStore`, `ShortcutFormDraftStore`) + recents + git index. | State lifetime tied to extension lifetime; risk of stale data or leaks if dispose is incomplete. | Make state explicit and owned. Use `IDisposable` hierarchies. Consider weak events or `IObservable` for settings/workspace changes instead of direct event wiring. | Slightly more code; much clearer ownership. |
| **Low** | Health checks and launch diagnostics exist and are exposed in UI. | Good, but execution appears synchronous in some paths. | Keep health checks fast or make non-blocking where possible; surface “last checked” timestamps. | Already mostly addressed. |

### 3. Maintainability & Technical Debt

**Strengths**
- `QuickShell.Core.Tests` project exists.
- Clear naming conventions and many small, focused classes (single-responsibility on the surface).
- Undo/redo support via context commands is a nice touch for in-palette editing.

**Issues**

| Severity | Evidence | Impact | Recommended Fix | Trade-offs |
|----------|----------|--------|------------------|------------|
| **High** | Large number of small service classes with likely cross-dependencies (health ↔ git ↔ launch ↔ form). | “Shotgun surgery” risk when changing Workspace or Launch model. New contributors will struggle to see the forest. | Create a lightweight layered architecture diagram (or ADR) and a `QuickShell.Core.Abstractions` or `Domain` namespace for core contracts. Add high-level sequence diagrams for “Launch Workspace” and “Edit in Palette”. | Documentation effort; prevents future debt. |
| **Medium** | Heavy use of forms/drafts for in-palette CRUD (`ShortcutForm*`, `AdaptiveCardFormJson`, `FormPayloadMerge`, etc.). | This layer is complex and CmdPal-form primitives may evolve. | Treat the form layer as a distinct bounded context. Consider extracting a small “WorkspaceEditor” service that the provider consumes. Long-term: evaluate whether a lightweight companion settings window (WinUI) would be simpler for heavy editing. | In-palette is a differentiator; don’t remove it lightly. |
| **Low** | StyleCop + analyzers present; nullable enabled. | Good hygiene. | Enforce analyzers as errors in CI. Add architecture tests (e.g., “Core must not reference UI namespaces”) if using ArchUnitNET or similar. | Minor. |

### 4. Performance & Responsiveness

**Strengths**
- Startup tracing (`StartupPerformanceTrace`), lazy fallback page, background `Task.Run` for git pre-warm, `SearchDebouncer`.
- Filtering largely delegated to CmdPal host (correct).
- AOT + trimming + single-file publish keeps binary size reasonable for an extension.

**Issues**

| Severity | Evidence | Impact | Recommended Fix | Trade-offs |
|----------|----------|--------|------------------|------------|
| **Medium** | Health checks, git status, and project classification may run on list render or selection in some paths. | Perceived lag if list grows to dozens of workspaces with many launches. | Cache health snapshots with TTL or invalidate on relevant file/git events. Make expensive checks explicit “Refresh” actions. | Cache invalidation complexity vs. snappiness. |
| **Low** | Pre-warm is best-effort (`try/catch`). | Silent failure possible on startup. | Log (to a file or ETW) when pre-warm fails; surface in diagnostics. | Negligible cost. |

### 5. User Experience & Reliability

**Strengths**
- Excellent error surfacing via health badges, status snapshot, and launch diagnostics copy.
- Import/export, favorites, recents, section headers, elevated launch (`Ctrl+Enter`), home keywords — all thoughtful.
- Companion app / dev server / browser link actions add real workflow value.

**Issues**

| Severity | Evidence | Impact | Recommended Fix | Trade-offs |
|----------|----------|--------|------------------|------------|
| **High** | Elevated launches and arbitrary command execution in user-controlled workspaces. | Security surface (though user-initiated). Path traversal or malicious workspace JSON could be problematic if import is trusted. | Validate/sanitize paths and commands on load and before launch. Consider a “trusted workspace” flag or hash for imported sets. Document the trust model clearly. | Slight friction on import; necessary for safety. |
| **Medium** | Many edge cases handled (missing folder, invalid terminal profile, dirty git tree gating, port-in-use warnings). | Some paths still have silent catches (pre-warm). | Add structured logging or a diagnostics log file that users can attach to issues. Make `LaunchDiagnosticsReport` richer. | Logging adds a dependency; worth it for supportability. |
| **Low** | Settings split between `settings.json` (global terminal defaults) and per-workspace data. | Minor inconsistency risk on migration. | Already has legacy migration; keep it maintained. | — |

### 6. Windows / PowerToys Best Practices

**Strengths**
- Proper MSIX packaging, AppX manifest, publisher identity, certificate handling for Store vs. sideload.
- Uses WindowsAppSDK, CsWinRT, modern .NET 10 — aligned with current PowerToys direction.
- Respects terminal host preferences and discovers real user profiles (WT settings.json, WSL, PATH).
- PowerToys Run integration via thin plugin is clean.

**Issues**

| Severity | Evidence | Impact | Recommended Fix | Trade-offs |
|----------|----------|--------|------------------|------------|
| **Medium** | `UseWindowsForms = true` in both Core and main project. | Suggests legacy folder picker or dialog usage. In a modern CmdPal extension this can pull in WinForms dependencies unnecessarily. | Replace with `Microsoft.WindowsAPICodePack` or pure WinRT folder picker / `StorageFile` APIs if possible. Or isolate WinForms usage. | Minor binary size win; cleaner dependency graph. |
| **Low** | WebView2 is referenced centrally. | Not obviously used in core CmdPal flow (perhaps future companion UI or settings webview?). | Audit actual usage. If unused, remove to reduce attack surface and size. | — |

---

## Foundational Phase Roadmap (PRs #0001–#0005)

**Goal**  
Transform QuickShell’s architectural foundation over five coordinated, incremental PRs. This phase directly attacks the highest-severity risks identified in the audit:

- Hidden coupling via ~50 narrow service classes and static hubs  
- Brittle string-based command routing  
- Fragile JSON persistence (no atomicity, no schema versioning)  
- Service explosion and implicit dependencies in project intelligence  
- Unclear resource ownership and lifecycle in a long-lived Command Palette host  

**Guiding Principles**
- **DI-first**: #0001 is the critical enabler; everything after becomes dramatically simpler, testable, and maintainable.
- **Incremental & safe**: Use compatibility shims and dual-paths during migration so the extension remains fully functional.
- **Testability as outcome**: Every PR must improve (or at least not harm) the ability to write fast, isolated tests.
- **Preserve UX & performance**: No user-visible behavior changes or startup regressions during this phase.

See individual PR proposals in `docs/prs/` for full motivation, design, trade-offs, and commit messages:
- `0001-introduce-dependency-injection-composition-root.md`
- `0002-persistence-hardening-atomic-writes-schema-version.md`
- `0003-replace-string-command-routing-with-typed-descriptors.md`
- `0004-service-consolidation-registry-pattern.md`
- `0005-formal-disposable-cancellation-ownership-expanded-tests.md`

### Recommended Order & Dependency Graph

| Order | PR | Title | Depends On | Primary Risk Mitigated | Key Deliverable |
|-------|----|-------|------------|------------------------|-----------------|
| 1 | **#0001** | Dependency Injection + Composition Root | None | Hidden coupling, poor testability, static hub | `IServiceProvider` / `QuickShellServices` facade wired into `QuickShellCommandsProvider` + top services |
| 2 | **#0002** | Persistence Hardening (Atomic Writes + Schema Version) | #0001 | Data loss on crash, unversioned format, ad-hoc migration | `IAtomicFileWriter`, versioned JSON header, `WorkspacesChanged` events on `IShortcutRepository` |
| 3 | **#0003** | Typed Command Routing (`CommandDescriptor` + Registry) | #0001 | Fragile `TryParse*` string parsing, magic IDs | `CommandDescriptor`, `ICommandRouter`, clean `GetCommandItem` delegation |
| 4 | **#0004** | Service Consolidation via Registry Pattern | #0001 (strong) | ~50 narrow `*Discovery` / `*Classifier` classes, shotgun surgery | `IProjectClassifier`, `ITaskSuggestionProvider`, `IProjectAnalysisService` + `IEnumerable<T>` DI pattern |
| 5 | **#0005** | Formal `IDisposable` / Cancellation Ownership + Expanded Tests | #0001 (strong) | Resource leaks, unclear shutdown, weak test coverage | `QuickShellLifetime`, proper `IDisposable` hierarchy, `QuickShellTestHost` fixture, significantly expanded test suite |

### Detailed PR Breakdown with Suggested Code Snippets

#### #0001 — Dependency Injection + Composition Root (Foundation — Do First)

**Why it matters**  
Without DI, every subsequent improvement (atomic persistence, typed routing, registries, lifetime management) requires painful manual wiring or statics. This is the single highest-leverage change.

**Key Changes**
- New `QuickShell.Core/Abstractions/` and `Composition/` folders
- `QuickShellServiceCollectionExtensions.AddQuickShellCore()`
- `QuickShellCommandsProvider` receives `IServiceProvider` (or a thin `QuickShellServices` facade)
- Top services (`IShortcutRepository`, `ITerminalLauncher`, `IWorkspaceHealthChecker`, etc.) get interfaces + constructor injection

**Suggested Code Snippet — Registration**

```csharp
// QuickShell.Core/Composition/QuickShellServiceCollectionExtensions.cs
public static class QuickShellServiceCollectionExtensions
{
    public static IServiceCollection AddQuickShellCore(this IServiceCollection services)
    {
        // Core data & launch
        services.AddSingleton<IShortcutRepository, ShortcutRepository>();
        services.AddSingleton<ITerminalLauncher, TerminalLauncher>();
        services.AddSingleton<ITerminalProfileResolver, TerminalProfileResolver>();
        services.AddSingleton<IWorkspaceMapper, WorkspaceMapper>();

        // Health & Git
        services.AddTransient<IWorkspaceHealthChecker, WorkspaceHealthCheck>();
        services.AddTransient<IWorkspaceGitOperations, WorkspaceGitOperations>();

        // Facade for provider consumption (keeps constructor manageable)
        services.AddSingleton<QuickShellServices>();

        return services;
    }
}
```

**Suggested Code Snippet — Provider Consumption (after #0001)**

```csharp
public sealed class QuickShellCommandsProvider : CommandProvider
{
    private readonly QuickShellServices _services;

    public QuickShellCommandsProvider(IServiceProvider serviceProvider)
    {
        _services = serviceProvider.GetRequiredService<QuickShellServices>();
        // ... rest of initialization
    }

    public override CommandItem? GetCommandItem(string id)
    {
        // Now delegates cleanly to _services.CommandRouter or similar (see #0003)
        ...
    }
}
```

**Migration Note**  
Keep existing static access paths temporarily behind a compatibility shim so the app continues to run while you convert call sites.

#### #0002 — Persistence Hardening (Atomic Writes + Schema Version)

**Why it matters**  
Current JSON writes are not atomic. A crash during save can corrupt `shortcuts.json`. There is also no explicit schema version, making future format changes risky.

**Key Changes**
- `IAtomicFileWriter` + `AtomicFileWriter` (uses `File.Replace` for true atomicity on Windows)
- Top-level `version` field in JSON files + lightweight migration pipeline on load
- `IShortcutRepository` gains `WorkspacesChanged` / `SettingsChanged` events (now easy to inject and consume)

**Suggested Code Snippet — Atomic Write Helper**

```csharp
// QuickShell.Core/Persistence/AtomicFileWriter.cs
public sealed class AtomicFileWriter : IAtomicFileWriter
{
    public void WriteAllTextAtomic(string path, string content)
    {
        var tempPath = Path.Combine(Path.GetDirectoryName(path)!, 
                                    Path.GetRandomFileName() + ".tmp");
        File.WriteAllText(tempPath, content, Encoding.UTF8);
        File.Replace(tempPath, path, null);   // atomic on Windows
    }
}
```

**Suggested Code Snippet — Versioned Load (inside ShortcutRepository)**

```csharp
private (int Version, JsonDocument Doc) LoadWithVersion(string fileName)
{
    var path = GetFullPath(fileName);
    if (!File.Exists(path)) return (1, null);

    using var doc = JsonDocument.Parse(File.ReadAllText(path));
    var version = doc.RootElement.TryGetProperty("version", out var v) ? v.GetInt32() : 0;
    return (version, doc);
}
```

#### #0003 — Typed Command Routing (`CommandDescriptor` + Registry)

**Why it matters**  
`ShortcutCommandIds.TryParseOpen`, `TryParseOpenLaunch`, etc. + scattered factory methods are fragile. Adding any new command type requires hunting multiple files.

**Key Changes**
- `CommandDescriptor` record + `CommandKind` enum
- `ICommandRouter` with `TryHandle(string id, out CommandItem? item)`
- `QuickShellCommandsProvider.GetCommandItem` becomes a one-line delegation

**Suggested Code Snippet — CommandDescriptor**

```csharp
public sealed record CommandDescriptor(
    string Id,
    CommandKind Kind,
    string? WorkspaceId = null,
    string? LaunchId = null,
    // ... other payload
    object? Payload = null);
```

**Usage in Provider (post #0003)**

```csharp
public override CommandItem? GetCommandItem(string id)
    => _services.CommandRouter.TryHandle(id, out var item) ? item : null;
```

#### #0004 — Service Consolidation via Registry Pattern

**Why it matters**  
~50 narrow service classes (`DockerComposeDiscovery`, `PackageJsonClassifier`, `TaskTypeCommandSuggestion`, `CompanionAppDetector`, …) create implicit coupling and make evolution painful.

**Key Changes**
- `IProjectClassifier` + `ITaskSuggestionProvider` interfaces
- `ProjectLayout` cheap snapshot record
- `IProjectAnalysisService` orchestrator that aggregates `IEnumerable<IProjectClassifier>`
- Pure DI `IEnumerable<T>` pattern — no heavy plugin framework required

**Suggested Code Snippet — Registry via DI**

```csharp
// Registration (in AddQuickShellCore)
services.AddSingleton<IProjectClassifier, DotNetProjectClassifier>();
services.AddSingleton<IProjectClassifier, NodePackageJsonClassifier>();
services.AddSingleton<IProjectClassifier, DockerComposeClassifier>();
services.AddSingleton<IProjectClassifier, TaskfileClassifier>();
// ... more as they are migrated

services.AddSingleton<IProjectAnalysisService, ProjectAnalysisService>();
```

**Orchestrator Consumption**

```csharp
public sealed class ProjectAnalysisService : IProjectAnalysisService
{
    private readonly IEnumerable<IProjectClassifier> _classifiers;

    public ProjectAnalysisService(IEnumerable<IProjectClassifier> classifiers)
        => _classifiers = classifiers;

    public ProjectLayout Analyze(string folderPath)
    {
        var layout = new ProjectLayout(folderPath);
        foreach (var classifier in _classifiers)
            classifier.Classify(layout);
        return layout;
    }
}
```

#### #0005 — Formal `IDisposable` / Cancellation Ownership + Expanded Tests

**Why it matters**  
Long-lived extension host + background work (`GitRepoIndex` pre-warm, debouncers, health checks) currently has implicit ownership. Risk of leaks or stale state on shutdown/reload.

**Key Changes**
- `QuickShellLifetime` lightweight holder (owns root `CancellationTokenSource`)
- Proper `IDisposable` / `IAsyncDisposable` on services that hold resources
- `QuickShellExtension` creates and disposes the root lifetime
- New `QuickShellTestHost` test fixture that spins up a real `ServiceProvider` + lifetime scope

**Suggested Code Snippet — Lifetime Holder**

```csharp
public sealed class QuickShellLifetime : IDisposable
{
    private readonly CancellationTokenSource _cts = new();

    public CancellationToken Token => _cts.Token;

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
```

**Test Fixture Sketch (greatly expands coverage)**

```csharp
public sealed class QuickShellTestHost : IDisposable
{
    public ServiceProvider Services { get; }
    public QuickShellLifetime Lifetime { get; }

    public QuickShellTestHost()
    {
        var services = new ServiceCollection();
        services.AddQuickShellCore();
        services.AddSingleton<QuickShellLifetime>();
        Services = services.BuildServiceProvider();
        Lifetime = Services.GetRequiredService<QuickShellLifetime>();
    }

    public void Dispose() => Services.Dispose();
}
```

### Integrated Testing Strategy Across All Five PRs

- **Unit tests** — Every new interface gets a test double; existing services get constructor-injection tests.
- **Integration tests** — Use `QuickShellTestHost` (from #0005) to exercise real `ServiceProvider` graphs for:
  - Launch paths
  - Persistence round-trips + migration
  - Command routing end-to-end
  - Project analysis with multiple classifiers registered
- **Lifecycle / Disposal tests** — Verify that cancelling the root token stops background work and that `IDisposable` chains are honored.
- **Architecture tests** (optional but recommended) — Enforce “Core has no static mutable state after #0001” and “Abstractions project has no implementation dependencies”.
- **Migration safety** — Property-based or snapshot tests for JSON loading of v0 (current) → v1 (versioned) data.

### Risk Mitigation for the Entire Foundational Phase

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| Temporary dual maintenance (old static paths + new DI paths) | High | Medium | Keep shims short-lived; delete in same PR or immediate follow-up |
| Constructor bloat in pages / providers | Medium | Medium | Use `QuickShellServices` facade + keyed services where appropriate |
| Lifetime mistakes (capturing transient in singleton) | Medium | High | Clear lifetime rules documented in `QuickShellServiceCollectionExtensions`; simple analyzer or code review checklist |
| Migration data loss during persistence hardening | Low | Critical | Atomic writes + backup-on-first-migration + extensive migration tests in #0002 |
| Over-engineering the registry pattern (#0004) | Medium | Low | Start with only the top 6 classifiers in #0004; leave the long tail for later PRs |

### Post-Foundational Opportunities (After #0005)

- Publish `QuickShell.Core` as a reusable NuGet package
- Evaluate a lightweight companion WinUI settings window for heavy editing (reducing form complexity inside CmdPal)
- Adopt richer CmdPal provider types (`ISettingsProvider`, `INavigationProvider`) when the SDK surface expands
- Consider moving to a more structured store (e.g., SQLite + migrations) if workspace count grows significantly

This five-PRs plan is deliberately scoped to be achievable in 6–10 weeks of focused work while delivering outsized long-term maintainability gains.

---

## Final Recommendation

QuickShell is already one of the more ambitious and useful CmdPal extensions. The core domain logic is valuable. The main risk is that the rich feature set has produced a large number of specialized classes whose interactions are not yet explicitly managed.

Addressing the coupling, routing, and persistence concerns incrementally will make the codebase significantly more pleasant to maintain and extend while preserving (and even enhancing) the excellent user experience you have built.

---

*This audit was performed as a Principal Software Architect review focused on structural and architectural issues.*  
*Ready for phased implementation starting with the Dependency Injection composition root PR.*
