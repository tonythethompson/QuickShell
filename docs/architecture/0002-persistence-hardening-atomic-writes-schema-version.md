**Proposed Refactor PR**

**Title:**  
Persistence Hardening: Shared Atomic Writer + Explicit Schema Versioning for Secondary Stores and Shortcuts Envelope

**PR Type:** Reliability / Data Integrity  
**Priority:** P1 (High — addresses audit risk #4, reframed after code review)  
**Estimated Size:** Small–Medium (extract helper + envelope migration + secondary stores)  
**Depends On:** #0001 (Introduce Dependency Injection + Composition Root)  
**Enables:** Safer schema evolution, easier testing of persistence layer, future import/export robustness, consistent atomicity across all JSON stores

---

### Motivation (from Architectural Audit — fact-checked)

**What is already solid today**

- `ShortcutRepository.WriteLayoutAtomic` already writes `shortcuts.json` via temp file + `File.Replace` (with `.bak`) and a named mutex. Claiming “no atomic writes for shortcuts” would be incorrect.
- Legacy migration (`WorkspaceLegacyMigration`, alternate-source import) already exists.
- Source-generated `QuickShellJsonContext` is in place for DTOs.

**Real gaps**

1. **No schema version** — `shortcuts.json` is a **root JSON array** (`ShortcutLayoutJson`). Future format changes have no header to key migrations.
2. **Secondary stores are not atomic** — `WorktreeBranchTargetStore` uses `File.WriteAllText`; `ShortcutDraftStore` uses `WriteAllTextAsync` without temp+replace.
3. **Settings** live in `settings.json` via CmdPal `JsonSettingsManager` / `QuickShellJsonSettingsStore` (extension project), not Core’s `ShortcutRepository` — harden separately if needed; do not assume Core owns this write path.
4. **`IShortcutRepository` has no change events** — consumers take static/direct references; no `WorkspacesChanged` / similar for reactive UI.
5. **No shared `IAtomicFileWriter`** — atomic logic is private to `ShortcutRepository`; other writers duplicate weaker patterns.

While single-user desktop usage makes concurrent modification rare, crashes during secondary-store saves can still corrupt branch targets or drafts. As schema evolves (health snapshots, richer metadata), an unversioned root array becomes a liability.

This PR hardens the foundation **after** DI is in place so the repository can cleanly receive an atomic writer and migrator.

---

### Goals

1. Extract a reusable atomic write helper matching today’s shortcuts behavior (temp → `File.Replace` / `File.Move` + optional backup).
2. Apply that helper to `WorktreeBranchTargetStore` and draft persistence.
3. Introduce an explicit integer schema version via a **document envelope** for shortcuts (and optional version on worktree targets).
4. Preserve 100% backward compatibility with existing user data (root array = version 0).
5. Expose change events from `IShortcutRepository` so UI and other consumers can react without polling.
6. Make persistence paths configurable via options (injected) where practical.

**Non-Goals (for this PR)**
- Rewriting `WriteLayoutAtomic` as if it did not exist — **extract and reuse**
- Full event sourcing or WAL
- Compression or encryption of the store
- Moving to SQLite / LiteDB
- Changing the public `Workspace` / `TerminalShortcut` *domain* shape (layout on-disk envelope may change)
- Assuming `settings.json` is written by Core (it is owned by CmdPal settings manager in the extension)

---

### Proposed Design

#### 1. New / Changed Types in `QuickShell.Core`

```
QuickShell.Core/
├── Abstractions/   (or Services/ if keeping flat — follow #0001)
│   └── IAtomicFileWriter.cs
├── Persistence/
│   ├── AtomicFileWriter.cs           # Extracted from WriteLayoutAtomic semantics
│   ├── JsonPersistenceOptions.cs
│   └── Schema/
│       └── PersistenceVersion.cs
├── Services/
│   ├── ShortcutRepository.cs         # Use writer + versioned envelope + events
│   ├── ShortcutLayoutJson.cs         # Accept Array (v0) or Object envelope (v1+)
│   ├── WorktreeBranchTargetStore.cs  # Atomic writes + optional Version
│   └── ShortcutDraftStore.cs         # Atomic writes
```

#### 2. Core Interfaces (additive)

**`IAtomicFileWriter.cs`**
```csharp
public interface IAtomicFileWriter
{
    void WriteAllBytesAtomic(string path, byte[] contents);
    void WriteAllTextAtomic(string path, string contents);
    // Prefer mirroring existing mutex/backup behavior for hot paths
}
```

For v1, keep migration logic in `ShortcutLayoutJson` / `ShortcutRepository` rather than a heavy plugin migrator.

#### 3. Schema Version Strategy

**Current on-disk shape (v0):**
```json
[ { "name": "...", "directory": "...", ... }, ... ]
```

**Proposed v1:**
```json
{
  "version": 1,
  "entries": [ { "name": "...", "directory": "...", ... }, ... ]
}
```

- `PersistenceVersion.Current = 1`
- On load: `JsonValueKind.Array` → version 0; object with `version`/`entries` → current
- On first save after upgrade: rewrite as v1 envelope atomically (existing `.bak` remains valuable)
- Document in `docs/persistence-schema.md` (or inline)

`WorktreeBranchTargetsDocument` can gain an optional `Version` property without changing the map shape.

#### 4. Atomic Write Implementation (match existing shortcuts)

```csharp
public sealed class AtomicFileWriter : IAtomicFileWriter
{
    public void WriteAllBytesAtomic(string path, byte[] contents)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tempPath = path + ".tmp";
        var backupPath = path + ".bak";
        File.WriteAllBytes(tempPath, contents);
        if (File.Exists(path))
            File.Replace(tempPath, path, backupPath, ignoreMetadataErrors: true);
        else
            File.Move(tempPath, path);
        // cleanup leftover .tmp best-effort
    }
}
```

Prefer byte[] for shortcuts (matches today’s `Serialize` → `WriteAllBytes`). Text overload for worktree/drafts.

#### 5. Changes to `ShortcutRepository`

- Inject `IAtomicFileWriter` (and options) via DI from #0001
- Replace private `WriteLayoutAtomic` body with calls to the shared writer (keep mutex if desired for process-wide lock)
- Raise:
  ```csharp
  public event EventHandler? WorkspacesChanged;
  ```
- Keep `WorkspaceLegacyMigration` paths; ensure they feed the same load/save pipeline

---

### Impact on Existing Code

**Files that will change significantly:**
- `ShortcutRepository.cs`, `ShortcutLayoutJson.cs`
- `WorktreeBranchTargetStore.cs`, `WorktreeBranchTargetsDocument.cs`
- `ShortcutDraftStore.cs` (write path)
- `QuickShellServiceCollectionExtensions.cs` (from #0001) — register writer + options
- Import/export paths that assume a root array — must accept both shapes

**Files that stay largely untouched:**
- Domain models (`TerminalShortcut`, etc.)
- Terminal launching / health / forms (except listening to new events if wired)

---

### Migration / Rollout Strategy

1. **Phase 1**: Extract `IAtomicFileWriter`; shortcuts continue writing **v0 array** but through the helper (behavior-preserving).
2. **Phase 2**: Teach load to accept envelope; start writing v1; leave v0 readable forever.
3. Apply helper to worktree + drafts in the same PR if the diff stays reviewable.
4. Add events on repository last (depends on #0001 for clean consumers).

Crash mid-write still leaves either old or new file thanks to `File.Replace` (already true for shortcuts; newly true for secondary stores).

---

### Testing Strategy

- Unit test `AtomicFileWriter` (temp cleanup, replace, first-create `Move`)
- Repository tests:
  - Load v0 array → save → disk has `"version": 1` envelope
  - Load v1 envelope round-trip
  - Leftover `.tmp` does not prevent recovery
  - `WorkspacesChanged` fires on Upsert/Delete/Import/Reset
- Worktree/draft crash-style tests for atomic path
- Golden fixtures from real `%LOCALAPPDATA%\QuickShell\shortcuts.json` samples

---

### Risks & Trade-offs

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|----------|
| Envelope migration breaks import/export or Raycast-adjacent tooling that assumes a root array | Medium | High | Dual-read forever; document format; update export helpers in same PR |
| `File.Replace` permission / volume edge cases | Low | Medium | Keep existing fallbacks from `WriteLayoutAtomic` |
| Treating settings.json as a Core store | — | — | Out of scope; document ownership in extension |
| Redoing work already present in shortcuts | High if unreviewed | Wasted effort | This proposal explicitly **extracts** existing atomics |

**Trade-off Summary**  
Modest schema migration risk in exchange for evolvable format + consistent atomicity. Do **not** spend this PR reinventing shortcuts atomics.

---

### Suggested Commit Structure

```
refactor(persistence): extract atomic writer, version shortcuts envelope, harden secondary stores

- Extract IAtomicFileWriter from ShortcutRepository.WriteLayoutAtomic semantics
- Migrate shortcuts.json root array (v0) → versioned envelope (v1) with dual-read
- Apply atomic writes to WorktreeBranchTargetStore and ShortcutDraftStore
- Expose WorkspacesChanged on IShortcutRepository
- Register writer in AddQuickShellCore; add migration + atomic tests
```

---

### Next Steps After This PR

1. **This PR (#0002)** — Shared atomics + schema version + events  
2. **#0003** — Typed command routing  
3. **#0004** — Classifier registry  
4. **When needed** — v2 envelope fields (e.g. cached health)  
5. **Optional** — Align extension `settings.json` save path with the same writer if CmdPal store API allows

---

**Final Recommendation**  
High ROI once scoped correctly: build on existing `WriteLayoutAtomic`, version the layout format, and fix the stores that are still non-atomic. Combined with #0001, persistence becomes testable and evolvable without a false “greenfield atomic writes” narrative.

---

*Updated July 2026 after fact-check against `ShortcutRepository`, `ShortcutLayoutJson`, and secondary stores.*  
*Generated as part of the QuickShell Architectural Audit (July 2026)*
