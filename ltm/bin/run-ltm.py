#!/usr/bin/env python3
"""Run ltm.py using python_cmd from ltm/config.json."""
from __future__ import annotations

import json
import os
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
CONFIG_PATH = ROOT / "config.json"
LTM_PY = Path(__file__).resolve().parent / "ltm.py"
ALLOWED_PYTHON_NAMES = frozenset({
    "python",
    "python3",
    "py",
    "python.exe",
    "python3.exe",
    "py.exe",
})


def _python_cmd() -> str:
    try:
        config = json.loads(CONFIG_PATH.read_text(encoding="utf-8"))
    except OSError:
        return "python"
    except json.JSONDecodeError:
        return "python"
    return config.get("python_cmd") or "python"


def _validated_python_cmd(raw: str) -> str:
    candidate = raw.strip() or "python"
    if Path(candidate).name.lower() in ALLOWED_PYTHON_NAMES:
        return candidate
    return "python"


def main() -> int:
    os.chdir(ROOT)
    cmd = [_validated_python_cmd(_python_cmd()), str(LTM_PY), *sys.argv[1:]]
    return subprocess.call(cmd)


if __name__ == "__main__":
    raise SystemExit(main())
