#Requires -Version 5.1

# Regenerates Quick Shell icon PNGs from SVG sources under QuickShell/Assets.

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$assetsDir = Join-Path $repoRoot 'QuickShell\Assets'
$iconScript = Join-Path $assetsDir 'icon\export-msix-icons.ps1'
$runScript = Join-Path $assetsDir 'run\export-run-icons.ps1'
$raycastScript = Join-Path $assetsDir 'raycast\export-raycast-icons.ps1'
$masterSvg = Join-Path $assetsDir 'icon\quickshell-icon.svg'
$logoSvg = Join-Path $repoRoot 'logo.svg'
$cmdPalIcon = Join-Path $repoRoot 'cmdpal-gallery\extensions\tonythethompson\quickshell\icon.png'

if (-not (Test-Path $masterSvg)) {
    throw "Missing master icon SVG: $masterSvg"
}

if (-not (Test-Path $iconScript)) {
    throw "Missing icon export script: $iconScript"
}

if (-not (Test-Path $runScript)) {
    throw "Missing run export script: $runScript"
}

if (-not (Test-Path $raycastScript)) {
    throw "Missing Raycast export script: $raycastScript"
}

& $iconScript
if ($LASTEXITCODE -ne 0) {
    throw "export-msix-icons.ps1 failed with exit code $LASTEXITCODE"
}

& $runScript
if ($LASTEXITCODE -ne 0) {
    throw "export-run-icons.ps1 failed with exit code $LASTEXITCODE"
}

& $raycastScript
if ($LASTEXITCODE -ne 0) {
    throw "export-raycast-icons.ps1 failed with exit code $LASTEXITCODE"
}

# Keep repo-root logo.svg aligned with the canonical master artwork.
Copy-Item -Force $masterSvg $logoSvg

$appTile300 = Join-Path $assetsDir 'StoreListing\AppTile_300x300.png'
if (Test-Path $appTile300) {
    New-Item -ItemType Directory -Force -Path (Split-Path $cmdPalIcon -Parent) | Out-Null
    Copy-Item -Force $appTile300 $cmdPalIcon
}

Write-Host 'Quick Shell assets generated:'
Write-Host "  Master SVG:  $masterSvg"
Write-Host "  MSIX PNGs:   $assetsDir"
Write-Host "  Run plugin:  $(Join-Path $repoRoot 'QuickShell.Run\Images')"
Write-Host "  Raycast:     $(Join-Path $repoRoot 'QuickShell.Raycast\assets')"
