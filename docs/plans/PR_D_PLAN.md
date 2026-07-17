# PR D Completion Plan - Finish DI conversion for suggestion + companion helpers

## Current state (verified)

- Worktree: `A:\QuickShell.pr-d` on branch `pr/d-suggestion-companion-di`.
- Baseline: `dotnet build QuickShell.sln` succeeds and `QuickShell.Core.Tests` pass (646/646).
- Already converted:
  - `CommandSuggestionService` -> `ICommandSuggestionService` singleton instance.
  - `ProjectClassificationCache` -> `IProjectClassificationCache` singleton instance.
  - `ITaskSuggestionProvider` uses `Order` + `TaskSuggestionContext` + `CommandSuggestionPill`.
  - `WorkspaceSetupTaskSuggestionProvider`, `DockerComposeTaskSuggestionProvider`, `AgentCliSuggestionProvider` registered.
  - `ProjectAnalysisService` delegates pill orchestration to `ICommandSuggestionService`.
  - `IQuickShellServices` exposes `ICommandSuggestionService CommandSuggestions`.
  - `IQuickShellSettingsReader` and `ICompanionAppPreference` extracted to DI.

## What is still static and needs conversion

| Static helper | Why it still matters | Proposed DI owner |
|---|---|---|
| `CompanionAppCatalog` | Mutable executable/form-choice caches; called from UI, detector, launcher, validation, tests | `ICompanionAppCatalog` singleton |
| `WorkspaceCompanionSignals` | File/directory probes used by detector, launcher, validation | `IWorkspaceCompanionSignals` singleton |
| `CompanionAppArgumentValidation` | Rules reference `CompanionAppCatalog` and `WorkspaceCompanionSignals` | `ICompanionAppArgumentValidation` singleton |
| `CompanionAppNormalization` | Referenced by UI, launcher, repository, validation, seed factory | `ICompanionAppNormalization` singleton |
| `JetBrainsInstallDiscovery` | `ConcurrentDictionary` product cache | `IInstallDiscovery` singleton |
| `VisualStudioInstallDiscovery` | Static install cache; uses `CompanionAppCatalog` version constants | `IInstallDiscovery` singleton |
| `TaskTypeCommandSuggestion` | Still called by `ShortcutFormLaunchSection` and `TaskTypeCatalog.Build*Json` | Subsume into `IProjectAnalysisService` |
| `TaskTypeCatalog.BuildFormChoicesJson` / `BuildPickerChoicesJson` | Uses static `TaskTypeCommandSuggestion` | Add `BuildTaskTypeChoicesJson` to `IProjectAnalysisService` |
| `AgentCliSuggestion` | Static class duplicated by new `AgentCliSuggestionProvider` | Delete; update tests to use provider/`ICommandSuggestionService` |
| `FormCatalogPrewarm` | Calls `CompanionAppCatalog.BuildFormChoicesJson()` | Accept `ICompanionAppCatalog` in `Warm` |
| `CompanionAppFormEditor` / `CompanionAppFormJson` | Pure builders but reference `CompanionAppCatalog`/`CompanionAppArgumentValidation` | Keep static but add overloads that take the new interfaces, or wrap behind `ICompanionAppFormService` if pages diverge |

## Remaining work by phase

### Phase 1 - Finish suggestion orchestrator cleanup

1. Add `BuildTaskTypeChoicesJson` to `IProjectAnalysisService` / `ProjectAnalysisService` using the existing `GetAvailableTaskTypes` and `GetTaskTypeChoiceTooltip` helpers.
2. Move `TaskTypeCatalog.BuildFormChoicesJson` / `BuildPickerChoicesJson` implementation into `ProjectAnalysisService.BuildTaskTypeChoicesJson`.
3. Update `ShortcutFormLaunchSection.TryCreateCommandFromTaskType` to call `projectAnalysis.TrySuggestTaskCommand` instead of `TaskTypeCommandSuggestion.TrySuggest`.
4. Delete `TaskTypeCommandSuggestion.cs` (or make it internal and move remaining logic into `ProjectAnalysisService`).
5. Delete `AgentCliSuggestion.cs`; port any missing tests to `AgentCliSuggestionProvider` or `ICommandSuggestionService.GetPills`.
6. Update `TaskTypeCatalogTests` and `TaskTypeCommandSuggestionTests` to exercise `IProjectAnalysisService` / `ICommandSuggestionService` instead of static helpers.

### Phase 2 - Define companion abstractions

1. Add `ICompanionAppCatalog` (presets, install probes, form-choice JSON, validation helpers).
2. Add `IWorkspaceCompanionSignals` (marker probes for `.vscode`, `.sln`, Zed, Kiro, Windsurf, etc.).
3. Add `ICompanionAppArgumentValidation`.
4. Add `ICompanionAppNormalization`.
5. Add `IInstallDiscovery` with `TryResolveExecutable(string presetId)` and `TryInferPresetFromPath(string path)`.
6. Keep `CompanionAppFormEditor` / `CompanionAppFormJson` as pure helpers for now; add overloads that accept the interfaces. Create `ICompanionAppFormService` only if `ShortcutFormPage` and `WorkspaceEditor` call patterns cannot share the same shape.

### Phase 3 - Convert companion static classes to DI singletons

1. `CompanionAppCatalog` -> `internal sealed class CompanionAppCatalog : ICompanionAppCatalog`; move `PresetExecutableCache`, `_cachedFormChoicesJson`, and the `TryResolveExecutableOverride` test seam to instance fields/constructor. Inject `IEnumerable<IInstallDiscovery>` for JetBrains/VisualStudio resolution.
2. `WorkspaceCompanionSignals` -> instance class implementing `IWorkspaceCompanionSignals`.
3. `CompanionAppArgumentValidation` -> instance class implementing `ICompanionAppArgumentValidation` (inject `ICompanionAppCatalog` and `IWorkspaceCompanionSignals`).
4. `CompanionAppNormalization` -> instance class implementing `ICompanionAppNormalization` (inject `ICompanionAppCatalog` for constants if needed).
5. `JetBrainsInstallDiscovery` / `VisualStudioInstallDiscovery` -> singletons implementing `IInstallDiscovery` with instance caches; move VS version range constants out of `CompanionAppCatalog` to avoid circular lookups.
6. Update `CompanionAppPreference` to use `ICompanionAppCatalog` instead of static catalog constants.

### Phase 4 - Update detector/launcher

1. `CompanionAppDetector` constructor injects `ICompanionAppCatalog`, `IWorkspaceCompanionSignals`, `ICompanionAppPreference`.
2. `CompanionAppLauncher` constructor injects `ICompanionAppCatalog`, `ICompanionAppNormalization`, `ICompanionAppArgumentValidation`, `ICompanionAppPreference`, `IWorkspaceCompanionSignals`, `IProcessStarter`.
3. Convert `CompanionAppLauncher.ExpandArguments` from static to instance.

### Phase 5 - Update UI and form callers

1. Add `ICompanionAppCatalog`, `IWorkspaceCompanionSignals`, `ICompanionAppArgumentValidation`, `ICompanionAppNormalization` to `IQuickShellServices`; update `QuickShellServices` constructor and `QuickShellCommandRoutingServiceCollectionExtensions` factory.
2. `ShortcutFormPage`: replace static `CompanionAppCatalog`, `CompanionAppFormEditor`, `CompanionAppFormJson`, `CompanionAppArgumentValidation` calls with `_services.CompanionAppsCatalog` / `_services.CompanionSignals` / `_services.CompanionValidation` / `_services.CompanionNormalization` (or inject specific interfaces).
3. `WorkspaceEditor`: same companion replacements.
4. `ShortcutFormTemplateJson`: pass `IQuickShellServices` into companion form JSON generation; use injected catalog/validation.
5. `ShortcutDraftStore`, `ShortcutLaunchNormalization`, `ShortcutValidation`, `ShortcutHealth`, `ShortcutRepository`, `WorkspaceSeedFactory`, `WorkspaceUtilityCommands`: replace static `CompanionAppNormalization` / `CompanionAppCatalog` with injected services.
6. `FormCatalogPrewarm.Warm`: accept `ICompanionAppCatalog`; update `QuickShellCommandsProvider.KickoffFormCatalogPrewarm` to resolve it.

### Phase 6 - Run plugin and CLI

1. Verify `QuickShell.Run/RunLaunchSuggestionPanel.cs` and `ShortcutWorkspaceEditorWindow.cs` use `ICommandSuggestionService` and not static `CommandSuggestionService`.
2. Verify `QuickShell.Suggest/Program.cs` resolves `ICommandSuggestionService` from its service provider/manual bundle.

### Phase 7 - Tests and fakes

1. Update `TestQuickShellServicesFactory` to expose/register new companion interfaces.
2. Add `FakeInstallDiscovery` and optionally `FakeCompanionAppCatalog` / `FakeWorkspaceCompanionSignals` for tests that need controlled install discovery.
3. Update `CompanionAppLauncherTests`, `WorkspaceUtilityTests`, `ProjectSetupSuggestionTests`, `AgentCliSuggestionTests`, `TaskTypeCatalogTests`, `TaskTypeCommandSuggestionTests` to use DI services/fakes.
4. Remove static test override seams (`TryResolveExecutableOverride`, `ReadLastUsedOverride`, `WriteLastUsedOverride`) and replace with fake interface registrations.

### Phase 8 - Verification

1. `dotnet build QuickShell.sln`
2. `dotnet test QuickShell.Core.Tests/QuickShell.Core.Tests.csproj`
3. `scripts/deploy.ps1` + CmdPal reload smoke test (pills, task type dropdown, companion app picker/launch).

## Acceptance criteria

- `CompanionAppCatalog`, `WorkspaceCompanionSignals`, `CompanionAppArgumentValidation`, `CompanionAppNormalization`, `JetBrainsInstallDiscovery`, `VisualStudioInstallDiscovery`, `AgentCliSuggestion`, and `TaskTypeCommandSuggestion` are no longer `internal static class` (constants-only `TaskTypeCatalog` may remain static).
- All previous callers use constructor-injected interfaces.
- Build succeeds, all tests pass, and CmdPal form behavior (suggestion pills, task type dropdown, companion apps) is unchanged.

## Risks and notes

- `CompanionAppCatalog` has ~24 call sites across UI, core, and tests; this is the widest change. Consider doing it before smaller UI callers so the service surface is stable.
- `VisualStudioInstallDiscovery` currently reads `CompanionAppCatalog.PresetVersionRanges`; move those constants to the discovery class or to `ICompanionAppCatalog` to avoid circular dependencies.
- `IQuickShellServices` will grow with the new companion properties; if it becomes unwieldy, split into `ICompanionAppFormService` later, but only if `ShortcutFormPage` and `WorkspaceEditor` need different shapes.
- Primary checkout `A:\QuickShell` has an unstaged `WorkspaceEditor.cs` change; all remaining work must stay in `A:\QuickShell.pr-d`.
