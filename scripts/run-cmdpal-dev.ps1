#Requires -Version 5.1
<#
.SYNOPSIS
    Rebuild Quick Shell, install the dev MSIX, and restart Command Palette.

.DESCRIPTION
    Default dev loop for the refactored QuickShell extension:
      1. Stop Command Palette
      2. Build + sign + install Quick Shell MSIX (scripts/deploy.ps1)
      3. Start Command Palette again

    Shortcut data lives at %LOCALAPPDATA%\QuickShell\shortcuts.json (unchanged by refactor).

.PARAMETER UseLocalSdk
    Build Quick Shell against the local PowerToys CmdPal SDK.
    Requires a sibling PowerToys checkout. Do not use with retail PowerToys CmdPal.

.PARAMETER SkipDeploy
    Skip Quick Shell build/install; only restart Command Palette.

.PARAMETER DeployOnly
    Build/install Quick Shell only; do not restart Command Palette.

.PARAMETER UseDevCmdPal
    Start a local PowerToys dev CmdPal build after deploy. Default is retail PowerToys.

.EXAMPLE
    .\scripts\run-cmdpal-dev.ps1

.EXAMPLE
    .\scripts\run-cmdpal-dev.ps1 -UseLocalSdk

.EXAMPLE
    .\scripts\run-cmdpal-dev.ps1 -SkipDeploy
#>
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [switch]$UseLocalSdk,
    [switch]$SkipDeploy,
    [switch]$DeployOnly,
    [switch]$UseDevCmdPal,
    [switch]$SkipElevation,
    [switch]$RecreateCertificate
)

$ErrorActionPreference = 'Stop'
$ProjectRoot = Split-Path -Parent $PSScriptRoot
$DeployScript = Join-Path $PSScriptRoot 'deploy.ps1'

. (Join-Path $PSScriptRoot 'CmdPalLifecycle.ps1')

Push-Location $ProjectRoot
try {
    if (-not $SkipDeploy) {
        $deployArgs = @{
            Configuration       = $Configuration
            SkipElevation       = $SkipElevation
            RecreateCertificate = $RecreateCertificate
            NoRestartCmdPal     = $DeployOnly
            UseDevCmdPal         = $UseDevCmdPal
        }
        if ($UseLocalSdk) {
            $deployArgs.UseLocalCmdPalSdk = $true
        }

        Write-Host '=== Quick Shell: build + install MSIX ===' -ForegroundColor Cyan
        & $DeployScript @deployArgs
        if ($LASTEXITCODE -ne 0) {
            throw "deploy.ps1 failed with exit code $LASTEXITCODE"
        }
    }
    else {
        Write-Host 'Skipping Quick Shell deploy (-SkipDeploy).' -ForegroundColor DarkGray
        if (-not $DeployOnly) {
            Write-Host '=== Command Palette: restart ===' -ForegroundColor Cyan
            Stop-CmdPalProcesses
            Start-CommandPalette -ProjectRoot $ProjectRoot -Configuration $Configuration -UseDevCmdPal:$UseDevCmdPal
        }
    }

    Write-Host ''
    Write-Host 'Dev loop ready.' -ForegroundColor Green
    Write-Host '  1. Open Command Palette (Win+Alt+Space by default)'
    Write-Host '  2. Run: Reload Command Palette Extension'
    Write-Host '  3. Search: Quick Shell'
    Write-Host ''
    Write-Host "Shortcuts file: $env:LOCALAPPDATA\QuickShell\shortcuts.json" -ForegroundColor DarkGray

    if ($UseLocalSdk) {
        Write-Host 'Built with local CmdPal SDK. Use the PowerToys dev CmdPal build.' -ForegroundColor DarkGray
    }
}
finally {
    Pop-Location
}
