#Requires -Version 5.1
<#
.SYNOPSIS
    Stop PowerToys/CmdPal/Run/Raycast, rebuild, and deploy all QuickShell variants.

.DESCRIPTION
    Dev deploy loop for the three QuickShell surfaces:
      1. Command Palette (signed MSIX via deploy.ps1)
      2. PowerToys Run plugin (QuickShell.Run via build-run-plugin.ps1 -Deploy)
      3. Raycast extension (QuickShell.Raycast via npm build + ray develop)

    Stops host apps first so DLLs and the MSIX install are not locked, rebuilds each
    variant, then restarts PowerToys/CmdPal and Raycast.

.PARAMETER Configuration
    Build configuration for CmdPal MSIX and the Run plugin.

.PARAMETER SkipCmdPal
    Skip Command Palette MSIX build/install.

.PARAMETER SkipRun
    Skip PowerToys Run plugin build/deploy.

.PARAMETER SkipRaycast
    Skip Raycast extension build/deploy.

.PARAMETER SkipTests
    Skip QuickShell.Raycast npm test.

.PARAMETER RaycastBuildOnly
    Build the Raycast extension but do not start `npm run dev`.

.PARAMETER NoRestart
    Do not restart PowerToys/CmdPal or Raycast after deploy.

.PARAMETER UseLocalCmdPalSdk
    Build CmdPal against the sibling PowerToys CmdPal SDK.

.PARAMETER UseDevCmdPal
    Start a local PowerToys dev CmdPal build after deploy.

.PARAMETER SkipElevation
    Never relaunch deploy.ps1 as administrator.

.PARAMETER RecreateCertificate
    Recreate the dev MSIX signing certificate.

.EXAMPLE
    .\scripts\deploy-all.ps1

.EXAMPLE
    .\scripts\deploy-all.ps1 -Configuration Release -SkipTests

.EXAMPLE
    .\scripts\deploy-all.ps1 -SkipRaycast -UseDevCmdPal
#>
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [switch]$SkipCmdPal,
    [switch]$SkipRun,
    [switch]$SkipRaycast,
    [switch]$SkipTests,
    [switch]$RaycastBuildOnly,
    [switch]$NoRestart,
    [switch]$UseLocalCmdPalSdk,
    [switch]$UseDevCmdPal,
    [switch]$SkipElevation,
    [switch]$RecreateCertificate
)

$ErrorActionPreference = 'Stop'
$ProjectRoot = Split-Path -Parent $PSScriptRoot
$DeployScript = Join-Path $PSScriptRoot 'deploy.ps1'
$RunDeployScript = Join-Path $PSScriptRoot 'build-run-plugin.ps1'

. (Join-Path $PSScriptRoot 'CmdPalLifecycle.ps1')
. (Join-Path $PSScriptRoot 'RaycastLifecycle.ps1')

function Write-Step {
    param(
        [string]$Title
    )

    Write-Host ''
    Write-Host "=== $Title ===" -ForegroundColor Cyan
}

Push-Location $ProjectRoot
try {
    $deployCmdPal = -not $SkipCmdPal
    $deployRun = -not $SkipRun
    $deployRaycast = -not $SkipRaycast

    if ($deployCmdPal -or $deployRun) {
        Write-Step 'Stopping PowerToys, Command Palette, and Run'
        Stop-CmdPalProcesses
    }

    if ($deployRaycast) {
        Write-Step 'Stopping Raycast'
        Stop-RaycastProcesses
    }

    if ($deployCmdPal) {
        Write-Step 'Command Palette: build + install MSIX'
        $deployArgs = @{
            Configuration   = $Configuration
            SkipElevation     = $SkipElevation
            RecreateCertificate = $RecreateCertificate
            NoRestartCmdPal   = $true
            UseDevCmdPal      = $UseDevCmdPal
        }
        if ($UseLocalCmdPalSdk) {
            $deployArgs.UseLocalCmdPalSdk = $true
        }

        & $DeployScript @deployArgs
        if ($LASTEXITCODE -ne 0) {
            throw "deploy.ps1 failed with exit code $LASTEXITCODE"
        }
    }
    else {
        Write-Host 'Skipping Command Palette deploy (-SkipCmdPal).' -ForegroundColor DarkGray
    }

    if ($deployRun) {
        Write-Step 'PowerToys Run: build + deploy plugin'
        & $RunDeployScript -Configuration $Configuration -Deploy
        if ($LASTEXITCODE -ne 0) {
            throw "build-run-plugin.ps1 failed with exit code $LASTEXITCODE"
        }
    }
    else {
        Write-Host 'Skipping PowerToys Run deploy (-SkipRun).' -ForegroundColor DarkGray
    }

    if ($deployRaycast) {
        Write-Step 'Raycast: build extension'
        Deploy-RaycastExtension `
            -ProjectRoot $ProjectRoot `
            -SkipTests:$SkipTests `
            -BuildOnly:$RaycastBuildOnly `
            -StartDevServer:(-not $RaycastBuildOnly)
    }
    else {
        Write-Host 'Skipping Raycast deploy (-SkipRaycast).' -ForegroundColor DarkGray
    }

    if (-not $NoRestart) {
        if ($deployCmdPal -or $deployRun) {
            Write-Step 'Restarting PowerToys / Command Palette'
            Start-CommandPalette -ProjectRoot $ProjectRoot -Configuration $Configuration -UseDevCmdPal:$UseDevCmdPal
        }

        if ($deployRaycast) {
            Write-Step 'Restarting Raycast'
            if (-not (Start-RaycastApp)) {
                throw 'Raycast deploy was requested but Raycast is not installed or could not be started.'
            }
        }
    }

    Write-Host ''
    Write-Host 'All requested variants deployed.' -ForegroundColor Green
    Write-Host ''
    Write-Host 'Next steps:'
    if ($deployCmdPal) {
        Write-Host '  CmdPal: open Command Palette, run Reload Command Palette Extension, search Quick Shell'
    }
    if ($deployRun) {
        Write-Host '  Run: Alt+Space, type qs'
    }
    if ($deployRaycast -and -not $RaycastBuildOnly) {
        Write-Host '  Raycast: use the new develop terminal (npm run dev) or search QuickShell in Raycast'
    }
    Write-Host ''
    Write-Host "Shared data: $env:LOCALAPPDATA\QuickShell\" -ForegroundColor DarkGray
}
finally {
    Pop-Location
}
