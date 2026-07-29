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

`ApplyToShortcut`: if any launch/command already non-empty, **skip**; else replace launches with seed rows. Used by **Discover / git-create** (`WorkspaceSeedFactory`). Plain CmdPal/Run add/edit Browse–Paste does **not** auto-fill commands (pills only).

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

- Pill **button title** is the exact command (truncated); **tooltip** carries category / product name (`Test · npm test`, `Agent · Claude Code — …`)  
- Merge by **command string** (keep higher score)  
- Sort score ↓; take max (**20** slots; **12** visible by default = 3×4 rows + show more)  
- CmdPal template emits **only currently visible** pill actions (no mixed `$when` slots; those broke ActionSets)  
- Caps: scripts 40, docker services 20, pre-dedupe candidates 32  

`ApplyPill` targets the first empty `Command` draft or appends a populated command row (`LaunchRowListEditor`). It never overwrites an `OpenInTerminal` row. Terminal-only launches are created by the dedicated `Add terminal` editor action (row label remains `Open in terminal`), not by a suggestion pill; the former `Open directory only` pill is no longer emitted.

## Hosts

| Host | Integration |
|------|-------------|
| CmdPal | Adaptive Card pill actions on form (4 per row, 3 rows collapsed; template rebuilt for visible count; same-type pills grouped by `TypeTitle`, groups ordered by best score) |
| Run | `RunLaunchSuggestionPanel` |
| Raycast | `QuickShell.Suggest.exe` → JSON pills via `suggest-commands.ts` (form seeds + Actions/dropdown). Windows deploy/package scripts publish the CLI into Raycast assets and runtime resolution uses `environment.assetsPath`. Falls back to `project-setup-suggestion.ts` heuristics when Suggest.exe is missing (including macOS). `QUICKSHELL_SUGGEST_EXE` overrides the path in development. |

## Related helpers

`DockerComposeDiscovery`, `DevServerUrlDetection.FormatPackageScriptCommand`, `TaskTypeCommandSuggestion`, `SuggestionPillPresentation`, `GitRepoDiscovery` (uses classify for labels).

Architecture proposal **0004** discusses registry/plugin consolidation of this cluster; today it is static helpers + catalogs.

## Agent CLIs (PATH + project markers)

Agent CLI **pills** come from [`AgentCliCatalog`](../../QuickShell.Core/Services/AgentCliCatalog.cs) / [`AgentCliSuggestion`](../../QuickShell.Core/Services/AgentCliSuggestion.cs), merged in `CommandSuggestionService.GetPills`:

1. **PATH** — agent binaries such as `claude`, `codex`, `opencode`, `gemini`, `copilot`, `cursor-agent` / `agent`, `kiro-cli`, `grok`, `pi`, `kilocode`, `cmdc`, `agy`, `qwen`, `hermes`, `openclaw`, `cline`, `openhands`, `goose`, `aider`, `amp`, `auggie`, `autohand`, `cn`, `crush`, `devin`, `droid`, `jules`, `kimi`, `plandex` / `pdx`, `roo`, `vellum`, `oz`
2. **Marker fallback** — project files such as `CLAUDE.md`, `AGENTS.md`, `GEMINI.md`, `.github/copilot-instructions.md`, `.opencode/`, `.kiro/`, `.augment/`, `.factory/`, `crush.json`, `.plandex/`, etc.

All detected agent CLIs enter the ranked pill pool (no separate agent-only cap). Agent
scores stay below typical Build/API/Test/Frontend pills (`PathDetectedScore` 42,
`MarkerFallbackScore` 28) so project commands keep the early slots. The form shows the
first `SuggestionPillPresentation.DefaultVisibleSlots` (12) and offers **Show more
suggestions** when more remain. Copilot is detected as the `copilot` CLI (not bare
`gh`). They use task type `agent` and appear even when `ProjectStack.None`. They are
**not** auto-seeded into new workspaces and must **not** be treated as
[companions](./companions.md) (GUI apps).

## Key files

| File | Role |
|------|------|
| `ProjectClassifier.cs` / `ProjectClassification.cs` | Scan / DTO |
| `ProjectClassificationCache.cs` | Cache |
| `WorkspaceSetupSuggestion.cs` | Seeds |
| `TaskTypeCatalog.cs` / `TaskTypeCandidateBuilder.cs` | Types / scores |
| `CommandSuggestionService.cs` / `CommandSuggestionPill.cs` | Pills |
| `AgentCliCatalog.cs` / `AgentCliSuggestion.cs` | AI agent CLI pills (PATH + markers) |
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
