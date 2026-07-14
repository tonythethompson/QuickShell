#Requires -Version 5.1
<#
.SYNOPSIS
    Merge QuickShell dev deploy workspaces into %LOCALAPPDATA%\QuickShell\shortcuts.json.

.DESCRIPTION
    Appends deploy shortcuts (ddeploy, dcmd, drun, dray) from scripts/dev-deploy-shortcuts.json.
    Safe for legacy array shortcuts.json (does not round-trip the full file through ConvertFrom-Json).

.PARAMETER RepoRoot
    Override the QuickShell repo path written into shortcut Directory fields.

.PARAMETER Force
    Remove any existing deploy shortcuts with the same names, then reinstall.

.EXAMPLE
    .\scripts\install-dev-deploy-shortcuts.ps1
#>
param(
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot),
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

$sourcePath = Join-Path $PSScriptRoot 'dev-deploy-shortcuts.json'
$shortcutsPath = Join-Path $env:LOCALAPPDATA 'QuickShell\shortcuts.json'
$backupPath = "$shortcutsPath.bak"

if (-not (Test-Path $sourcePath)) {
    throw "Missing template: $sourcePath"
}

$repoRootNormalized = (Resolve-Path -LiteralPath $RepoRoot).Path
$incomingRaw = (Get-Content $sourcePath -Raw).Trim()
$incomingRaw = $incomingRaw.Replace('A:\QuickShell', $repoRootNormalized)

if (-not $incomingRaw.StartsWith('[') -or -not $incomingRaw.EndsWith(']')) {
    throw "Template must be a JSON array: $sourcePath"
}

$deployMarker = '"Deploy all QuickShell"'

if (Test-Path $shortcutsPath) {
    Copy-Item -LiteralPath $shortcutsPath -Destination $backupPath -Force
    $targetRaw = (Get-Content $shortcutsPath -Raw).Trim()
}
else {
    New-Item -ItemType Directory -Force -Path (Split-Path $shortcutsPath -Parent) | Out-Null
    $targetRaw = '[]'
}

if ($targetRaw -match [regex]::Escape($deployMarker) -and -not $Force) {
    Write-Host 'Dev deploy shortcuts already present. Pass -Force to replace them.' -ForegroundColor Yellow
    return
}

if ($Force -and $targetRaw -match [regex]::Escape($deployMarker)) {
    Write-Warning 'Force replace requested. Restore shortcuts.json.bak manually if this goes wrong.'
}

if ($targetRaw.StartsWith('[')) {
    $inner = $incomingRaw.TrimStart('[').TrimEnd(']').Trim()
    $base = $targetRaw.TrimEnd()
    if ($base.EndsWith(']')) {
        $base = $base.Substring(0, $base.Length - 1).TrimEnd()
    }

    $separator = if ($base.EndsWith('[')) { '' } else { ",`r`n" }
    $merged = "$base$separator$inner`r`n]"
}
elseif ($targetRaw -match '"entries"\s*:\s*\[') {
    throw 'Versioned shortcuts.json envelope is not supported yet. Export to legacy array or add deploy shortcuts manually.'
}
else {
    throw "Unsupported shortcuts.json shape at $shortcutsPath"
}

[System.IO.File]::WriteAllText($shortcutsPath, $merged)

Write-Host "Installed dev deploy shortcuts -> $shortcutsPath" -ForegroundColor Green
Write-Host "Backup: $backupPath" -ForegroundColor DarkGray
Write-Host 'Reload Command Palette Extension in CmdPal to pick up new workspaces.' -ForegroundColor Yellow
