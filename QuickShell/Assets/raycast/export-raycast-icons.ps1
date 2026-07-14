# Quick Shell Raycast extension icons (red >_ tile from quickshell-icon-raycast.svg).
# Deploys all PNGs under QuickShell.Raycast/assets/.
$ErrorActionPreference = 'Stop'

$raycastDir = $PSScriptRoot
$assetsDir = Split-Path $raycastDir -Parent
$repoRoot = Split-Path (Split-Path $assetsDir -Parent) -Parent
$iconSrc = Join-Path $assetsDir 'icon\quickshell-icon-raycast.svg'
$raycastAssets = Join-Path $repoRoot 'QuickShell.Raycast\assets'
$extensionSize = 512
$workspaceListSize = 128

function Export-Icon {
  param(
    [Parameter(Mandatory)] [string] $SvgPath,
    [Parameter(Mandatory)] [string] $OutPath,
    [Parameter(Mandatory)] [int] $Width
  )
  if (-not (Test-Path $SvgPath)) { throw "Missing source: $SvgPath" }
  npx --yes @resvg/resvg-js-cli $SvgPath $OutPath --fit-width $Width --shape-rendering 2 | Out-Null
  if ($LASTEXITCODE -ne 0) { throw "Export failed: $OutPath" }
}

New-Item -ItemType Directory -Force -Path $raycastAssets | Out-Null

$extensionIcon = Join-Path $raycastAssets 'extension-icon.png'
$extensionIconDark = Join-Path $raycastAssets 'extension-icon@dark.png'
$workspaceIcon = Join-Path $raycastAssets 'workspace-icon.png'

Export-Icon -SvgPath $iconSrc -OutPath $extensionIcon -Width $extensionSize
Copy-Item -Force $extensionIcon $extensionIconDark

Export-Icon -SvgPath $iconSrc -OutPath $workspaceIcon -Width $workspaceListSize

foreach ($commandIcon in @(
    'command-open.png',
    'command-create.png',
    'command-edit.png',
    'command-settings.png'))
{
  Copy-Item -Force $extensionIcon (Join-Path $raycastAssets $commandIcon)
}

Write-Host 'Quick Shell Raycast icon exports complete:'
Write-Host "  Extension + commands: $raycastAssets (${extensionSize}px)"
Write-Host "  Workspace list icon:  $workspaceIcon (${workspaceListSize}px)"
