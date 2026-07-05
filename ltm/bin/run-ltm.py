#!/usr/bin/env python3
"""Run ltm.py using python_cmd from ltm/config.json."""
from __future__ import annotations

import json
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
CONFIG_PATH = ROOT / "config.json"
LTM_PY = Path(__file__).resolve().parent / "ltm.py"


def _python_cmd() -> str:
    try:
        config = json.loads(CONFIG_PATH.read_text(encoding="utf-8"))
    except OSError:
        return "python"
    except json.JSONDecodeError:
        return "python"
    return config.get("python_cmd") or "python"


def main() -> int:
    cmd = [_python_cmd(), str(LTM_PY), *sys.argv[1:]]
    return subprocess.call(cmd)


if __name__ == "__main__":
    raise SystemExit(main())
