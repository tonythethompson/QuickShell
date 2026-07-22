# Workspace trust boundary (as-built)

> **Release note (deferred):** Ship kill switch is `shared/workspace-trust-features.json`
> (`enabled: false`). Core embeds it; Raycast syncs a copy via `scripts/sync-workspace-trust-features.js`.
> Flip that one JSON to re-enable. While off: enforcement and Trust/Revoke/Untrusted chrome are hidden;
> load/save coerces existing `IsTrusted: false` rows to trusted so re-enable does not revive stale denials.
> Trust-specific tests override via `EnableForTests()` / `setWorkspaceTrustEnabledForTests(true)` inside
> the serialized `ShortcutRepositoryMutex` collection (Raycast: beforeEach/afterEach).

QuickShell treats workspace content and execution authority as separate concerns. A local `StoredWorkspace` combines a portable `TerminalShortcut` content object with repository-owned security metadata (`IsTrusted` and a monotonically increasing revision). Editor drafts, portable DTOs, exports, and Raycast import/export payloads contain content only.

## Ingress and transitions

- Existing local records and legacy migration start trusted. Manual creation and built-in curated templates are trusted; duplicates copy the source decision.
- Imported, restored, synced, downloaded, and community-template records are always untrusted, including ID/name collisions with trusted records.
- Ordinary updates preserve the currently persisted trust under the repository lock; submitted or stale trust values are ignored. History/undo/redo never silently grants trust.
- `GrantTrust` is a typed repository transition bound to a review token containing the authoritative revision and a digest of validated security-relevant content. A mismatch requires a fresh review. `RevokeTrust` is always available and is not a generic undo operation.
- Trust is a durable editable-container decision: later command, elevation, path, companion, or URL edits remain trusted until the user explicitly revokes trust. Confirmation says this plainly.

## Authorization

Every external effect resolves the current workspace by ID and passes through the action-specific Core policy before execution. The policy returns all issue codes, a deterministic primary denial, risk findings, the assessed revision, and the exact effective directory, URI, executable, working directory, arguments, and command values consumed by the launcher.

Untrusted workspaces may be viewed, edited, deleted, exported, and copied after path syntax validation. They may not launch terminals/rows, start companions, open HTTP(S) URLs, open dev servers, or open directories. Open Directory is additionally limited to existing rooted local drive paths; UNC, device, shell namespace, environment-variable, and relative paths are rejected. Copy Path may copy a syntactically valid nonexistent path. Symlinks and junctions are not resolved in v1, so “local” does not claim to prevent redirection through those filesystem features. Adversarial coverage for control-character commands, unsafe URL schemes, UNC/pipe/env open-directory paths, companion newline injection, and malformed/oversized import lives in `WorkspaceSecurityAdversarialTests` (Core) and `security.test.ts` (Raycast).


Structural validity is independent of trust. A trusted-but-invalid record remains blocked until repaired; an untrusted-but-valid record can be reviewed and confirmed. Commands and arguments are validated without generic trimming or rewriting.

## Threat model and non-goals

The boundary protects against surprising or malicious imported files, accidental execution before review, host-level bypasses, and malformed stored values reaching process/browser launch APIs. It does not protect against an attacker who can modify QuickShell persistence or the running process, a trusted command that downloads or executes more code, executable replacement after review, or a user trusting content without understanding its authority.

Trust is not a tamper-detecting hash. The review digest is a short-lived check that the content confirmed in the dialog is the same authoritative revision being granted.

Portable export deliberately omits trust, revisions, review tokens, and host/process metadata. Exporting a trusted workspace and importing it—even into the same installation—always creates an untrusted workspace.

