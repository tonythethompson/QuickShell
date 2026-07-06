#!/usr/bin/env python3
"""Cursor sessionStart hook: inject PR review triage policy when an open PR has unresolved threads."""

from __future__ import annotations

import json
import os
import subprocess
import sys
import tempfile
from pathlib import Path

GRAPHQL_THREADS = """
query($owner: String!, $name: String!, $number: Int!) {
  repository(owner: $owner, name: $name) {
    pullRequest(number: $number) {
      number
      url
      reviewThreads(first: 100) {
        nodes { isResolved }
      }
    }
  }
}
"""

POLICY = """
## PR review triage policy

When working on a branch with an open pull request and you address review feedback:

1. Reply on each inline review thread with disposition:
   - Resolved in `<commit>` — summary, or
   - Not addressed — reason (out of scope, deferred, etc.)
2. Mark every triaged thread **Resolved** on GitHub (`resolveReviewThread`).
3. Optionally post one PR summary comment mapping reviewer → disposition.

Do not finish while unresolved review threads remain unless you are actively triaging them in the current turn.
""".strip()


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
    return Path(result.stdout.strip())


def get_open_pr(git_root: Path) -> dict | None:
    result = run(["gh", "pr", "view", "--json", "number,url,state"], cwd=git_root)
    if result.returncode != 0:
        return None
    try:
        pr = json.loads(result.stdout)
    except json.JSONDecodeError:
        return None
    return pr if pr.get("state") == "OPEN" else None


def count_unresolved(owner: str, repo: str, pr_number: int) -> tuple[str, int]:
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
        return "", 0

    try:
        pr = json.loads(result.stdout)["data"]["repository"]["pullRequest"]
    except (json.JSONDecodeError, KeyError, TypeError):
        return "", 0

    unresolved = sum(
        1
        for node in pr.get("reviewThreads", {}).get("nodes", [])
        if not node.get("isResolved")
    )
    return pr.get("url", ""), unresolved


def main() -> int:
    if run(["gh", "--version"]).returncode != 0:
        emit()
        return 0

    git_root = find_git_root(Path.cwd())
    if git_root is None:
        emit()
        return 0

    slug_result = run(["gh", "repo", "view", "--json", "nameWithOwner"], cwd=git_root)
    if slug_result.returncode != 0:
        emit()
        return 0

    try:
        owner, repo = json.loads(slug_result.stdout)["nameWithOwner"].split("/", 1)
    except (json.JSONDecodeError, KeyError, ValueError):
        emit()
        return 0

    pr = get_open_pr(git_root)
    if not pr:
        emit()
        return 0

    pr_url, unresolved = count_unresolved(owner, repo, int(pr["number"]))
    if unresolved <= 0:
        emit()
        return 0

    context = (
        f"{POLICY}\n\n"
        f"Active PR: #{pr['number']} ({pr_url or pr.get('url', '')})\n"
        f"Unresolved review threads right now: {unresolved}"
    )
    emit({"additional_context": context})
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
