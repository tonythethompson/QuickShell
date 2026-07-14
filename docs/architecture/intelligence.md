# Project intelligence and suggestion pills (as-built)

Local-only folder analysis that powers **suggested commands** (pills), create/discover **seed launches**, and task-type metadata.

**Privacy:** reads only the workspace folder the user chose. No network.

## Pipeline

```
Directory on disk
  → ProjectClassifier.Classify
  → ProjectClassification (stacks + scripts/targets)
  → WorkspaceSetupSuggestion (seed tasks)
  → TaskTypeCandidateBuilder × TaskTypeCatalog (score by type)
  → CommandSuggestionService.GetPills (merge, sort, cap)
  → UI ApplyPill / ApplyToShortcut
```

Cache: `ProjectClassificationCache` (fingerprint, ~64 entries).

## Classification (`ProjectClassifier`)

Top-level markers (mostly) → `ProjectStack` flags and extracts:

| Stack examples | Markers / extracts |
|----------------|--------------------|
| Node / Bun / Turbo / Nx / monorepo | `package.json`, lockfiles, workspaces |
| DotNet | `*.sln`, `*.csproj` / runnable list |
| Rust, Python, Go, Docker, Deno | Cargo, pyproject, go.mod, compose, deno.json |
| Make / Just / Taskfile | targets / recipes / tasks |
| VS Code / devcontainer | tasks.json, `.devcontainer` |
| Maven / Gradle / Rails / Elixir / Procfile | build files, Spring sniff, etc. |

Empty stack → no pills / no seeds.

## Setup seeds (`WorkspaceSetupSuggestion`)

Short ordered default tasks (e.g. Node `dev`/`start`/`test`/`build`, `dotnet watch`, compose, cargo, …).

`ApplyToShortcut`: if any launch/command already non-empty, **skip**; else replace launches with seed rows. Used by create/discover when layout is clear.

## Task types (`TaskTypeCatalog`)

Fixed ids: `api`, `frontend`, `services` (incl. legacy `database`), `logs`, `test`, `build`, `none`.

Stored on `WorkspaceEntry.TaskType` for UI titles/icons and pill labels (`Frontend · npm run dev`).

## Candidates and scoring (`TaskTypeCandidateBuilder`)

Per task type, pool from:

1. Setup suggestions
2. Docker compose service suggestions
3. Node scripts / Deno tasks (capped)

Filter: score &gt; 0 and command not in **used** set (`TaskTypePickContext` from current launch rows). Heuristics on script names (`dev`, `test`, monorepo paths, …).

## Pills (`CommandSuggestionService`)

```csharp
GetPills(directory, usedCommands, maxCount = MaxPills)
```

- Merge by **command string** (keep higher score)
- Sort score ↓; take max (**16** slots; ~**8** visible + show more)
- Caps: scripts 40, docker services 20, pre-dedupe candidates 32

`ApplyPill` → first empty editor placeholder row or append (`LaunchRowListEditor`). Clear row → empty command + `taskType: none` → pill can return.

## Hosts

| Host | Integration |
|------|-------------|
| CmdPal | Adaptive Card pill actions on form |
| Run | `RunLaunchSuggestionPanel` |
| Raycast | `QuickShell.Suggest.exe` → `CommandSuggestionService.GetPills` JSON (`QUICKSHELL_SUGGEST_EXE` for dev) |

## Related helpers

`DockerComposeDiscovery`, `DevServerUrlDetection.FormatPackageScriptCommand`, `TaskTypeCommandSuggestion`, `SuggestionPillPresentation`, `GitRepoDiscovery` (uses classify for labels).

Architecture proposal **0004** discusses registry/plugin consolidation of this cluster; today it is static helpers + catalogs.

## Agent CLIs (not implemented as pills)

Manual multi-agent workspaces are supported (`claude` / `codex` / `opencode` as launch **commands**). PATH-based **agent pills** are a natural extension (see product discussions) but are **not** part of classification today. Do **not** put TUI agents in [companions.md](./companions.md) — companions are GUI apps.

## Key files

| File | Role |
|------|------|
| `ProjectClassifier.cs` / `ProjectClassification.cs` | Scan / DTO |
| `ProjectClassificationCache.cs` | Cache |
| `WorkspaceSetupSuggestion.cs` | Seeds |
| `TaskTypeCatalog.cs` / `TaskTypeCandidateBuilder.cs` | Types / scores |
| `CommandSuggestionService.cs` / `CommandSuggestionPill.cs` | Pills |
| `LaunchRowListEditor.cs` | Apply/clear rows |
| `QuickShell.Suggest/Program.cs` | CLI for Raycast |

## Tests

`CommandSuggestionServiceTests`, `ProjectSetupSuggestionTests`, `TaskType*Tests`, etc.

## Gotchas

1. Top-level bias — nested packages may not classify unless root signals exist.
2. Used filter is exact command string (`npm` vs `pnpm` differ).
3. Same command appears as one pill after merge.
4. TaskType is metadata; launch runs the raw command.

## Related

- [forms.md](./forms.md) — pill click + form undo
- [launch.md](./launch.md) — running the command
- [companions.md](./companions.md) — GUI apps, not agents
