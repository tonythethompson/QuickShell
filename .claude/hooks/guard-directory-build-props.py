#!/usr/bin/env python3
"""PreToolUse hook: deny Edit/Write/MultiEdit to Directory.Build.props."""

from __future__ import annotations

import json
import sys

PROTECTED_BASENAME = "Directory.Build.props"


def is_protected(path: str) -> bool:
    if not path:
        return False
    normalized = path.replace("\\", "/").rstrip("/")
    return normalized.endswith(f"/{PROTECTED_BASENAME}") or normalized == PROTECTED_BASENAME


def collect_paths(tool_name: str, tool_input: dict) -> list[str]:
    paths: list[str] = []
    if tool_name in {"Edit", "Write"}:
        file_path = tool_input.get("file_path")
        if isinstance(file_path, str):
            paths.append(file_path)
        return paths

    if tool_name == "MultiEdit":
        file_path = tool_input.get("file_path")
        if isinstance(file_path, str):
            paths.append(file_path)
        return paths
    return paths


def deny(path: str) -> int:
    payload = {
        "hookSpecificOutput": {
            "hookEventName": "PreToolUse",
            "permissionDecision": "deny",
            "permissionDecisionReason": (
                f"Editing {PROTECTED_BASENAME} is blocked by project policy. "
                "This file pins the app version; use the release workflow instead."
            ),
        }
    }
    print(json.dumps(payload))
    print(f"Blocked edit to protected file: {path}", file=sys.stderr)
    return 2


def main() -> int:
    try:
        payload = json.load(sys.stdin)
    except json.JSONDecodeError:
        return 0

    tool_name = str(payload.get("tool_name") or "")
    tool_input = payload.get("tool_input")
    if not isinstance(tool_input, dict):
        return 0

    for path in collect_paths(tool_name, tool_input):
        if is_protected(path):
            return deny(path)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
