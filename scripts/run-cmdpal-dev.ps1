#Requires -Version 5.1
<#
.SYNOPSIS
    Rebuild Quick Shell, install the dev MSIX, and restart Command Palette.

.DESCRIPTION
    Default dev loop for the refactored QuickShell extension:
      1. Build + sign + install Quick Shell MSIX (scripts/deploy.ps1)
      2. Restart Command Palette (PowerToys dev build when available)
      3. Remind you to reload extensions

    Shortcut data lives at %LOCALAPPDATA%\QuickShell\shortcuts.json (unchanged by refactor).

.PARAMETER UseLocalSdk
    Build Quick Shell against the local PowerToys CmdPal SDK (hover-action APIs).
    Requires A:\PowerToys checkout. Do not use with retail PowerToys CmdPal.

.PARAMETER SkipDeploy
    Skip Quick Shell build/install; only restart Command Palette.

.PARAMETER DeployOnly
    Build/install Quick Shell only; do not restart Command Palette.

.PARAMETER UseRetailCmdPal
    Do not launch the PowerToys dev CmdPal build; restart retail PowerToys instead.

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
    [switch]$UseRetailCmdPal,
    [switch]$SkipElevation,
    [switch]$RecreateCertificate
)

$ErrorActionPreference = 'Stop'
$ProjectRoot = Split-Path -Parent $PSScriptRoot
$DeployScript = Join-Path $PSScriptRoot 'deploy.ps1'
$PowerToysRoot = Join-Path (Split-Path $ProjectRoot -Parent) 'PowerToys'
$PowerToysDevScript = Join-Path $PowerToysRoot 'tools\build\run-cmdpal-dev.ps1'

function Get-CmdPalDevExecutable {
    param([string]$Configuration)

    $candidates = @(
        Join-Path $PowerToysRoot "x64\$Configuration\WinUI3Apps\CmdPal\Microsoft.CmdPal.UI.exe"
        Join-Path $PowerToysRoot "src\x64\$Configuration\WinUI3Apps\CmdPal\Microsoft.CmdPal.UI.exe"
    )

    foreach ($path in $candidates) {
        if (Test-Path $path) {
            return $path
        }
    }

    return $null
}

function Stop-CmdPalProcesses {
    foreach ($name in @('Microsoft.CmdPal.UI', 'PowerToys')) {
        Get-Process -Name $name -ErrorAction SilentlyContinue | ForEach-Object {
            Write-Host "Stopping $($_.ProcessName) (PID $($_.Id))..." -ForegroundColor Yellow
            Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
        }
    }

    Start-Sleep -Milliseconds 500
}

function Start-DevCommandPalette {
    param(
        [string]$Configuration,
        [switch]$UseRetailCmdPal
    )

    if (-not $UseRetailCmdPal -and (Test-Path $PowerToysDevScript)) {
        Write-Host 'Restarting dev Command Palette (PowerToys run-cmdpal-dev.ps1)...' -ForegroundColor Cyan
        & $PowerToysDevScript -Configuration $Configuration -NoKill
        return
    }

    $devExe = Get-CmdPalDevExecutable -Configuration $Configuration
    if (-not $UseRetailCmdPal -and $devExe) {
        Write-Host "Launching dev CmdPal: $devExe" -ForegroundColor Cyan
        Start-Process -FilePath $devExe -WorkingDirectory (Split-Path $devExe -Parent)
        return
    }

    $powerToysExe = @(
        "${env:ProgramFiles}\PowerToys\PowerToys.exe"
        "${env:LocalAppData}\Microsoft\PowerToys\PowerToys.exe"
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1

    if ($powerToysExe) {
        Write-Host "Launching retail PowerToys: $powerToysExe" -ForegroundColor Cyan
        Start-Process -FilePath $powerToysExe
        return
    }

    Write-Warning 'Could not find Command Palette. Install PowerToys or build CmdPal from A:\PowerToys.'
}

Push-Location $ProjectRoot
try {
    if (-not $SkipDeploy) {
        $deployArgs = @{
            Configuration       = $Configuration
            SkipElevation       = $SkipElevation
            RecreateCertificate = $RecreateCertificate
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
    }

    if ($DeployOnly) {
        Write-Host 'DeployOnly: skipping Command Palette restart.' -ForegroundColor DarkGray
        return
    }

    Write-Host '=== Command Palette: restart ===' -ForegroundColor Cyan
    Stop-CmdPalProcesses
    Start-DevCommandPalette -Configuration $Configuration -UseRetailCmdPal:$UseRetailCmdPal

    Write-Host ''
    Write-Host 'Dev loop ready.' -ForegroundColor Green
    Write-Host '  1. Open Command Palette (Win+Alt+Space by default)'
    Write-Host '  2. Run: Reload Command Palette Extension'
    Write-Host '  3. Search: Quick Shell'
    Write-Host ''
    Write-Host "Shortcuts file: $env:LOCALAPPDATA\QuickShell\shortcuts.json" -ForegroundColor DarkGray

    if ($UseLocalSdk) {
        Write-Host 'Built with local CmdPal SDK (hover actions). Use the PowerToys dev CmdPal build.' -ForegroundColor DarkGray
    }
}
finally {
    Pop-Location
}
