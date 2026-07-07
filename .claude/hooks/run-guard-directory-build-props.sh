#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
guard_py="${script_dir}/guard-directory-build-props.py"

if command -v python3 >/dev/null 2>&1; then
  exec python3 "$guard_py"
fi

if command -v python >/dev/null 2>&1; then
  exec python "$guard_py"
fi

echo "guard-directory-build-props: python not found; skipping guard" >&2
exit 0
