---
name: gh-graphql
description: >-
  Reliably invoke the GitHub GraphQL API (gh api graphql) and GitHub REST POST
  endpoints from this Windows/cmd.exe environment. Use when gh api graphql fails
  with "invalid value" / "received N args" errors, when resolving PR review
  threads, posting review-comment replies, or running any GitHub GraphQL/POST
  via the GitHub CLI in this repo.
---

# Reliable GitHub GraphQL & REST POST from this environment

In this Windows/cmd.exe shell, the obvious `gh api graphql -f query="..."` form
**fails** because the shell strips the inner double quotes from the GraphQL
string, so `gh` receives `owner: tonythethompson` unquoted and rejects it with
`Argument 'owner' ... has an invalid value`. Likewise `gh api --method POST`
with `-f`/multiple flags often errors with `accepts 1 arg(s), received N`.

This skill documents the working pattern (verified while resolving all 16 review
threads on PR #70).

## When to Use This Skill

- `gh api graphql -f query=...` errors with "invalid value" or "received N args"
- You need to run a GitHub GraphQL query or mutation (e.g. resolve review threads)
- You need to POST to the GitHub REST API from a script/batch context
- Any `gh api` call that involves quotes, JSON bodies, or `|` pipes breaks

## The Working Pattern

### 1. GraphQL: always use `--input` with a JSON file

Write the query/mutation to a `.json` file as `{"query": "..."}` (escape inner
double quotes as `\"`), then run:

```
gh api graphql --input _q.json
```

Example `_q.json`:

```json
{
  "query": "query { repository(owner: \"tonythethompson\", name: \"QuickShell\") { pullRequest(number: 70) { reviewThreads(first: 50) { nodes { id isResolved } } } } }"
}
```

**Never** use `gh api graphql -f query="..."` (quote stripping) and **never**
pipe its output with `|` (cmd.exe intercepts the pipe). Let it print to stdout,
or redirect with `>`:

```
gh api graphql --input _q.json > _out.json
```

### 2. Wrap in PowerShell when cmd quoting gets in the way

```
powershell -NoProfile -Command "gh api graphql --input _q.json > _out.json"
```

PowerShell 5 is in use here: no `??` operator, and you cannot chain
`| Out-File` / `| Set-Content` from cmd. For multi-step logic, write a `.ps1`
and run it:

```
powershell -NoProfile -ExecutionPolicy Bypass -File script.ps1
```

### 3. Resolve PR review threads (GraphQL mutation)

Fetch thread node IDs:

```json
{
  "query": "query { repository(owner: \"tonythethompson\", name: \"QuickShell\") { pullRequest(number: 70) { reviewThreads(first: 50) { nodes { id isResolved } } } } }"
}
```

Then for each unresolved thread (`isResolved: false`), resolve it:

```json
{
  "query": "mutation { resolveReviewThread(input: {threadId: \"PRRT_xxx\"}) { thread { isResolved } } }"
}
```

Run each via `gh api graphql --input _mut.json`. Parsing tip: read the output
file with PowerShell `ConvertFrom-Json` rather than `grep`/`findstr`.

### 4. REST review-comment replies (POST)

The reply path **must include the PR number**, otherwise it 404s:

```
POST repos/{owner}/{repo}/pulls/{pull_number}/comments/{comment_id}/replies
```

Body `{"body":"..."}` via `--input`:

```
echo {"body":"Resolved by commit a1d2858."} > _reply.json
gh api --method POST repos/tonythethompson/QuickShell/pulls/70/comments/3591819229/replies --input=_reply.json
```

(`gh api` GET with `--jq` works inline; only `graphql` and `POST` need `--input`.)

## Important Notes

- `gh api graphql -f query=...` is broken here — always prefer `--input <file>`.
- Never pipe `gh api graphql` output; use `>` to a file.
- GraphQL string literals need escaped `\"` inside the JSON `"query"` value.
- REST review-comment replies require `pulls/{number}/comments/{id}/replies`
  (with the PR number), not `pulls/comments/{id}/replies`.
- For parsing JSON output in scripts, use PowerShell `ConvertFrom-Json`, not
  `findstr` (which runs out of memory on large diffs and mishandles anchors).
