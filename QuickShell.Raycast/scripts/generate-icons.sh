#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
ASSETS="$ROOT/assets"
REPO="$ROOT/.."

if ! command -v ffmpeg >/dev/null 2>&1; then
  echo "ffmpeg is required to regenerate Raycast icons." >&2
  exit 1
fi

render() {
  local input="$1"
  local output="$2"
  local size="${3:-512}"
  ffmpeg -y -i "$input" -vf "scale=${size}:${size}" -update 1 "$output" >/dev/null 2>&1
}

render "$REPO/QuickShell/Assets/logo-micro.svg" "$ASSETS/extension-icon.png"
render "$REPO/QuickShell/Assets/logo-run.dark.svg" "$ASSETS/extension-icon@dark.png"
render "$REPO/QuickShell/Assets/logo-micro.svg" "$ASSETS/workspace-icon.png" 128
cp "$ASSETS/extension-icon.png" "$ASSETS/command-open.png"
cp "$ASSETS/extension-icon.png" "$ASSETS/command-edit.png"
render "$REPO/QuickShell/Assets/icons8-folder-384.png" "$ASSETS/command-create.png"
render "$REPO/logo.svg" "$ASSETS/command-settings.png"

echo "Generated Raycast icons in $ASSETS"
