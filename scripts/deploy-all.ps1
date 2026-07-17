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

    Each surface is deployed independently. A Raycast failure does not roll back a
    successful CmdPal install; the summary at the end lists OK / FAILED / SKIPPED.

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
    .\scripts\ddeploy.ps1 -SkipRaycast -SkipElevation

.EXAMPLE
    .\scripts\deploy-all.ps1 -Configuration Release -SkipTests
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
    [switch]$RecreateCertificate,
    [switch]$RegenerateAssets
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

function Set-SurfaceResult {
    param(
        [System.Collections.Specialized.OrderedDictionary]$Results,
        [string]$Name,
        [string]$Status,
        [string]$Detail = ''
    )

    $Results[$Name] = if ($Detail) { "$Status`: $Detail" } else { $Status }
}

function Write-DeploySummary {
    param(
        [System.Collections.Specialized.OrderedDictionary]$Results
    )

    Write-Host ''
    Write-Host 'Deploy summary:' -ForegroundColor Cyan
    foreach ($entry in $Results.GetEnumerator()) {
        $color = switch -Regex ($entry.Value) {
            '^OK$' { 'Green' }
            '^SKIPPED' { 'DarkGray' }
            default { 'Red' }
        }
        Write-Host "  $($entry.Key): $($entry.Value)" -ForegroundColor $color
    }
}

Push-Location $ProjectRoot
$stoppedCmdPalHosts = $false
$stoppedRaycast = $false
$surfaceResults = [ordered]@{}
$cmdPalFailed = $false
$anyRequestedFailure = $false

try {
    $deployCmdPal = -not $SkipCmdPal
    $deployRun = -not $SkipRun
    $deployRaycast = -not $SkipRaycast

    if ($deployCmdPal -or $deployRun) {
        Write-Step 'Stopping PowerToys, Command Palette, and Run'
        Stop-CmdPalProcesses
        $stoppedCmdPalHosts = $true
    }

    if ($deployRaycast) {
        Write-Step 'Stopping Raycast'
        Stop-RaycastProcesses
        $stoppedRaycast = $true
    }

    if ($deployCmdPal) {
        Write-Step 'Command Palette: build + install MSIX'
        try {
            $deployArgs = @{
                Configuration       = $Configuration
                SkipElevation       = $SkipElevation
                RecreateCertificate = $RecreateCertificate
                NoRestartCmdPal     = $true
                UseDevCmdPal        = $UseDevCmdPal
                RegenerateAssets    = $RegenerateAssets
            }
            if ($UseLocalCmdPalSdk) {
                $deployArgs.UseLocalCmdPalSdk = $true
            }

            & $DeployScript @deployArgs
            if ($LASTEXITCODE -ne 0) {
                throw "deploy.ps1 failed with exit code $LASTEXITCODE"
            }

            Set-SurfaceResult -Results $surfaceResults -Name 'CmdPal' -Status 'OK'
        }
        catch {
            $cmdPalFailed = $true
            $anyRequestedFailure = $true
            Set-SurfaceResult -Results $surfaceResults -Name 'CmdPal' -Status 'FAILED' -Detail $_.Exception.Message
            Write-Host "CmdPal deploy failed: $($_.Exception.Message)" -ForegroundColor Red
        }
    }
    else {
        Set-SurfaceResult -Results $surfaceResults -Name 'CmdPal' -Status 'SKIPPED' -Detail '-SkipCmdPal'
    }

    if ($deployRun) {
        if ($cmdPalFailed) {
            $anyRequestedFailure = $true
            Set-SurfaceResult -Results $surfaceResults -Name 'Run' -Status 'SKIPPED' -Detail 'CmdPal failed'
            Write-Host 'Skipping Run deploy because CmdPal failed.' -ForegroundColor DarkGray
        }
        else {
            Write-Step 'PowerToys Run: build + deploy plugin'
            try {
                & $RunDeployScript -Configuration $Configuration -Deploy
                if ($LASTEXITCODE -ne 0) {
                    throw "build-run-plugin.ps1 failed with exit code $LASTEXITCODE"
                }

                Set-SurfaceResult -Results $surfaceResults -Name 'Run' -Status 'OK'
            }
            catch {
                $anyRequestedFailure = $true
                Set-SurfaceResult -Results $surfaceResults -Name 'Run' -Status 'FAILED' -Detail $_.Exception.Message
                Write-Host "Run deploy failed: $($_.Exception.Message)" -ForegroundColor Red
            }
        }
    }
    else {
        Set-SurfaceResult -Results $surfaceResults -Name 'Run' -Status 'SKIPPED' -Detail '-SkipRun'
    }

    if ($deployRaycast) {
        if ($cmdPalFailed) {
            $anyRequestedFailure = $true
            Set-SurfaceResult -Results $surfaceResults -Name 'Raycast' -Status 'SKIPPED' -Detail 'CmdPal failed'
            Write-Host 'Skipping Raycast deploy because CmdPal failed.' -ForegroundColor DarkGray
        }
        else {
            Write-Step 'Raycast: build extension'
            try {
                Deploy-RaycastExtension `
                    -ProjectRoot $ProjectRoot `
                    -SkipTests:$SkipTests `
                    -BuildOnly:$RaycastBuildOnly `
                    -StartDevServer:(-not $RaycastBuildOnly)

                Set-SurfaceResult -Results $surfaceResults -Name 'Raycast' -Status 'OK'
            }
            catch {
                $anyRequestedFailure = $true
                Set-SurfaceResult -Results $surfaceResults -Name 'Raycast' -Status 'FAILED' -Detail $_.Exception.Message
                Write-Host "Raycast deploy failed: $($_.Exception.Message)" -ForegroundColor Red
            }
        }
    }
    else {
        Set-SurfaceResult -Results $surfaceResults -Name 'Raycast' -Status 'SKIPPED' -Detail '-SkipRaycast'
    }

    if (-not $NoRestart) {
        if ($stoppedCmdPalHosts -and ($deployCmdPal -or $deployRun)) {
            Write-Step 'Restarting PowerToys / Command Palette'
            try {
                Start-CommandPalette -ProjectRoot $ProjectRoot -Configuration $Configuration -UseDevCmdPal:$UseDevCmdPal
            }
            catch {
                Write-Warning "CmdPal restart failed: $($_.Exception.Message). MSIX/plugin deploy may still have succeeded."
            }
        }

        if ($stoppedRaycast -and $deployRaycast) {
            Write-Step 'Restarting Raycast'
            if (-not (Start-RaycastApp)) {
                Write-Warning 'Raycast deploy finished but Raycast could not be restarted.'
            }
        }
    }

    Write-DeploySummary -Results $surfaceResults

    if (-not $anyRequestedFailure) {
        Write-Host ''
        Write-Host 'All requested variants deployed.' -ForegroundColor Green
    }
    else {
        Write-Host ''
        Write-Host 'Deploy finished with failures. See summary above.' -ForegroundColor Yellow
    }

    Write-Host ''
    Write-Host 'Next steps:'
    if ($deployCmdPal -and $surfaceResults.CmdPal -eq 'OK') {
        Write-Host '  CmdPal: open Command Palette, run Reload Command Palette Extension, search Quick Shell'
    }
    if ($deployRun -and $surfaceResults.Run -eq 'OK') {
        Write-Host '  Run: Alt+Space, type qs'
    }
    if ($deployRaycast -and $surfaceResults.Raycast -eq 'OK' -and -not $RaycastBuildOnly) {
        Write-Host '  Raycast: use the new develop terminal (npm run dev) or search QuickShell in Raycast'
    }
    Write-Host '  Dev deploy shortcuts: .\scripts\install-dev-deploy-shortcuts.ps1 (ddeploy, dcmd, drun, dray)'
    Write-Host ''
    Write-Host "Shared data: $env:LOCALAPPDATA\QuickShell\" -ForegroundColor DarkGray

    if ($anyRequestedFailure) {
        exit 1
    }
}
catch {
    Write-Host ''
    Write-Host "Deploy aborted: $($_.Exception.Message)" -ForegroundColor Red
    Write-DeploySummary -Results $surfaceResults

    if (-not $NoRestart) {
        if ($stoppedCmdPalHosts) {
            Write-Host 'Restarting PowerToys / Command Palette after aborted deploy...' -ForegroundColor Yellow
            try {
                Start-CommandPalette -ProjectRoot $ProjectRoot -Configuration $Configuration -UseDevCmdPal:$UseDevCmdPal
            }
            catch {
                Write-Warning "CmdPal restart failed: $($_.Exception.Message)"
            }
        }

        if ($stoppedRaycast -and -not $SkipRaycast) {
            Write-Host 'Restarting Raycast after aborted deploy...' -ForegroundColor Yellow
            Start-RaycastApp | Out-Null
        }
    }

    exit 1
}
finally {
    Pop-Location
}
