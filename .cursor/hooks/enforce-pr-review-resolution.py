#!/usr/bin/env python3
"""Cursor stop hook: block agent completion while GitHub PR review threads need triage."""

from __future__ import annotations

import json
import os
import subprocess
import sys
import tempfile
from pathlib import Path

MAX_FOLLOWUPS = 3
GRAPHQL_THREADS = """
query($owner: String!, $name: String!, $number: Int!) {
  repository(owner: $owner, name: $name) {
    pullRequest(number: $number) {
      number
      url
      reviewThreads(first: 100) {
        nodes {
          id
          isResolved
          path
          comments(first: 1) {
            nodes {
              author { login }
              body
            }
          }
        }
      }
    }
  }
}
"""


def emit(payload: dict | None = None) -> None:
    print(json.dumps(payload or {}))


def run(cmd: list[str], *, cwd: Path | None = None) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        cmd,
        cwd=cwd,
        text=True,
        encoding="utf-8",
        errors="replace",
        capture_output=True,
        check=False,
    )


def find_git_root(start: Path) -> Path | None:
    result = run(["git", "-C", str(start), "rev-parse", "--show-toplevel"])
    if result.returncode != 0:
        return None
    root = Path(result.stdout.strip())
    return root if root.is_dir() else None


def gh_available() -> bool:
    return run(["gh", "--version"]).returncode == 0


def get_repo_slug(git_root: Path) -> str | None:
    result = run(["gh", "repo", "view", "--json", "nameWithOwner"], cwd=git_root)
    if result.returncode != 0:
        return None
    try:
        return json.loads(result.stdout)["nameWithOwner"]
    except (json.JSONDecodeError, KeyError):
        return None


def get_open_pr(git_root: Path) -> dict | None:
    result = run(
        [
            "gh",
            "pr",
            "view",
            "--json",
            "number,url,state,headRefName",
        ],
        cwd=git_root,
    )
    if result.returncode != 0:
        return None
    try:
        pr = json.loads(result.stdout)
    except json.JSONDecodeError:
        return None
    if pr.get("state") != "OPEN":
        return None
    return pr


def get_unresolved_threads(owner: str, repo: str, pr_number: int) -> tuple[str, list[dict]]:
    payload = {
        "query": GRAPHQL_THREADS,
        "variables": {"owner": owner, "name": repo, "number": pr_number},
    }
    with tempfile.NamedTemporaryFile("w", suffix=".json", delete=False, encoding="utf-8") as handle:
        json.dump(payload, handle)
        temp_path = handle.name

    try:
        result = run(["gh", "api", "graphql", "--input", temp_path])
    finally:
        os.unlink(temp_path)

    if result.returncode != 0 or not result.stdout:
        return "", []

    try:
        data = json.loads(result.stdout)
    except json.JSONDecodeError:
        return "", []

    pr = (
        data.get("data", {})
        .get("repository", {})
        .get("pullRequest")
    )
    if not pr:
        return "", []

    threads = []
    for node in pr.get("reviewThreads", {}).get("nodes", []):
        if node.get("isResolved"):
            continue
        comment_nodes = node.get("comments", {}).get("nodes", [])
        author = "unknown"
        preview = ""
        if comment_nodes:
            author = comment_nodes[0].get("author", {}).get("login", "unknown")
            body = comment_nodes[0].get("body", "")
            preview = " ".join(body.split())[:160]
        threads.append(
            {
                "id": node.get("id", ""),
                "path": node.get("path", ""),
                "author": author,
                "preview": preview,
            }
        )

    return pr.get("url", ""), threads


def build_followup(pr_number: int, pr_url: str, threads: list[dict], loop_count: int) -> str:
    lines = [
        f"PR review triage is incomplete for PR #{pr_number} ({pr_url}).",
        "",
        f"There are {len(threads)} unresolved inline review thread(s). You must triage every thread on GitHub before ending this turn.",
        "",
        "For each thread:",
        "- **Resolved in code**: reply with `Resolved in <commit> — <what changed>`, then mark the thread **Resolved**.",
        "- **Intentionally not addressed**: reply with `Not addressed — <reason>`, then mark the thread **Resolved** anyway (resolved means triaged, not necessarily fixed).",
        "",
        "Use GitHub GraphQL via `gh api graphql`:",
        "- `addPullRequestReviewThreadReply` to reply on the thread",
        "- `resolveReviewThread` to mark it resolved",
        "",
        "Optionally post one PR summary comment listing each reviewer finding and its disposition.",
        "",
        "Unresolved threads:",
    ]

    for index, thread in enumerate(threads, start=1):
        preview = thread["preview"]
        if preview:
            preview = f" — {preview}"
        lines.append(
            f"{index}. `{thread['path']}` (@{thread['author']}){preview}"
        )

    if loop_count > 0:
        lines.extend(
            [
                "",
                f"Follow-up {loop_count + 1}/{MAX_FOLLOWUPS}: threads are still unresolved. Finish triage now.",
            ]
        )

    return "\n".join(lines)


def main() -> int:
    try:
        hook_input = json.load(sys.stdin)
    except json.JSONDecodeError:
        emit()
        return 0

    if hook_input.get("status") != "completed":
        emit()
        return 0

    loop_count = int(hook_input.get("loop_count") or 0)
    if loop_count >= MAX_FOLLOWUPS:
        emit()
        return 0

    if not gh_available():
        emit()
        return 0

    workspace_roots = hook_input.get("workspace_roots") or []
    start = Path(workspace_roots[0]) if workspace_roots else Path.cwd()
    if not start.exists():
        emit()
        return 0

    git_root = find_git_root(start)
    if git_root is None:
        emit()
        return 0

    slug = get_repo_slug(git_root)
    if not slug or "/" not in slug:
        emit()
        return 0

    owner, repo = slug.split("/", 1)

    pr = get_open_pr(git_root)
    if not pr:
        emit()
        return 0

    pr_url, threads = get_unresolved_threads(owner, repo, int(pr["number"]))
    if not threads:
        emit()
        return 0

    message = build_followup(int(pr["number"]), pr_url or pr.get("url", ""), threads, loop_count)
    emit({"followup_message": message})
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
