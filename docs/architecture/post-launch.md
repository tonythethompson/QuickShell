# Post-launch actions: dev server and links (as-built)

What happens **after** (or alongside) terminal handoff on a **full** workspace open: companion app, browser/dev-server URL, and optional repo links from menus.

Terminal launch itself: [launch.md](./launch.md). Companions detail: [companions.md](./companions.md).

Companion and URL effects are authorized independently at the current workspace revision. Untrusted workspaces cannot start companions or open repository/dev-server links. See [trust-model.md](./trust-model.md).

## Full workspace open order (Core)

```
Health → directory → git gate
  → CompanionAppLauncher (if IncludeCompanionApp)   // before terminals; soft fail
  → Terminals (single / multi)
  → BuildPostLaunchResult
       → fold preflight warnings
       → companion soft fail message
       → WorkspaceDevServerActions (if IncludeDevServerLink)
```

Single-row `LaunchEntry` sets companion **and** dev-server includes to **false**.

## Dev server URL

### Stored fields

| Field | Role |
|-------|------|
| `DevServerUrl` | Absolute URL (e.g. `http://localhost:5173`) |
| `OpenDevServerOnLaunch` | Open in default browser on full workspace run |

### Open path

`WorkspaceDevServerActions`:

- `ShouldOpenOnWorkspaceLaunch` — flag + non-empty URL  
- `TryOpen` → `WorkspaceLinkActions.TryOpenLink` (`Process.Start` UseShellExecute)  
- Soft fail → warning in StayOpen message + diagnostics  

### Detection (seed / suggest)

`DevServerUrlDetection.TryDetectDevServerUrl(directory)`:

- Reads root `package.json` scripts `dev` / `start`  
- Extracts port from script or infers framework defaults  
- Returns `http://localhost:{port}`  

Used when creating/editing to prefill URL; user can clear/disable open-on-launch.

### Health interaction

`WorkspaceHealthCheck` port-in-use **warnings** only when:

- `OpenDevServerOnLaunch` and URL port matches an in-use port, or  
- a launch command embeds that port  

So “port busy” is a **running** signal, not a hard block.

## Companion (reminder)

Same full-open path; see [companions.md](./companions.md). Soft fail; on-demand via context menu.

## Other links

| Action | Mechanism |
|--------|-----------|
| **Repo URL** | Optional `RepoUrl` on shortcut; open from context / utility commands via `WorkspaceLinkActions` |
| **Folder in Explorer** | `FolderPathActions` / utility commands |

Not automatic on every launch unless product UI wires them.

## Raycast

`launch-executor.ts` matches Core order on full workspace open:

1. Companions (`runPostLaunchActions` phase `companions`)
2. Terminal groups
3. Dev server URL (phase `devServer`)

Companion and URL failures are soft **warnings**. Single-row launches omit both (`includeCompanion` / `includeDevServer` false).

## Diagnostics

`LaunchDiagnosticKind`:

- `CompanionAppLaunched` / `CompanionAppUnavailable`  
- `DevServerUrlOpened` / `DevServerUrlUnavailable`  

## Key files

| File | Role |
|------|------|
| `WorkspaceDevServerActions.cs` | Open URL on launch |
| `DevServerUrlDetection.cs` | Infer localhost URL |
| `WorkspaceLinkActions.cs` | Generic URL open |
| `CompanionAppLauncher.cs` | GUI app open |
| `ShortcutLaunchExecutor.BuildPostLaunchResult` | Orchestration |
| Raycast `post-launch-actions.ts` | TS parity |

## Gotchas

1. Dev server open does **not** wait for the server to listen — race with `npm run dev` is expected.  
2. Health “port in use” is advisory.  
3. Invalid URL fails validation on save when provided.  
4. Single-row launch skips both companion and dev-server auto-open.

## Related

- [launch.md](./launch.md)  
- [companions.md](./companions.md)  
- [forms.md](./forms.md) — form fields for URL/toggles  
