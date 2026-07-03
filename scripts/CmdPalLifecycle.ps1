#Requires -Version 5.1
# Shared helpers for stopping and starting Command Palette during deploy.

function Get-CmdPalPowerToysRoot {
    param([string]$ProjectRoot)

    Join-Path (Split-Path $ProjectRoot -Parent) 'PowerToys'
}

function Get-CmdPalDevExecutable {
    param(
        [string]$PowerToysRoot,
        [string]$Configuration
    )

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
    $stopped = $false
    foreach ($name in @('Microsoft.CmdPal.UI', 'PowerToys')) {
        Get-Process -Name $name -ErrorAction SilentlyContinue | ForEach-Object {
            Write-Host "Stopping $($_.ProcessName) (PID $($_.Id))..."
            Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
            $stopped = $true
        }
    }

    if ($stopped) {
        Start-Sleep -Milliseconds 750
    }
}

function Start-CommandPalette {
    param(
        [string]$ProjectRoot,
        [string]$Configuration,
        [switch]$UseDevCmdPal
    )

    if ($UseDevCmdPal) {
        $powerToysRoot = Get-CmdPalPowerToysRoot -ProjectRoot $ProjectRoot
        $powerToysDevScript = Join-Path $powerToysRoot 'tools\build\run-cmdpal-dev.ps1'

        if (Test-Path $powerToysDevScript) {
            Write-Host 'Starting dev Command Palette (PowerToys run-cmdpal-dev.ps1)...'
            & $powerToysDevScript -Configuration $Configuration -NoKill
            return
        }

        $devExe = Get-CmdPalDevExecutable -PowerToysRoot $powerToysRoot -Configuration $Configuration
        if ($devExe) {
            Write-Host "Starting dev CmdPal: $devExe"
            Start-Process -FilePath $devExe -WorkingDirectory (Split-Path $devExe -Parent)
            return
        }

        Write-Warning 'UseDevCmdPal was set but no local PowerToys CmdPal build was found. Falling back to retail PowerToys.'
    }

    $powerToysExe = @(
        "${env:ProgramFiles}\PowerToys\PowerToys.exe"
        "${env:LocalAppData}\Microsoft\PowerToys\PowerToys.exe"
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1

    if ($powerToysExe) {
        Write-Host "Starting retail PowerToys: $powerToysExe"
        Start-Process -FilePath $powerToysExe
        return
    }

    $powerToysRoot = Get-CmdPalPowerToysRoot -ProjectRoot $ProjectRoot
    $devExe = Get-CmdPalDevExecutable -PowerToysRoot $powerToysRoot -Configuration $Configuration
    if ($devExe) {
        Write-Host "Retail PowerToys not found; starting local CmdPal build: $devExe"
        Start-Process -FilePath $devExe -WorkingDirectory (Split-Path $devExe -Parent)
        return
    }

    Write-Warning 'Could not find Command Palette. Install PowerToys or build CmdPal from a sibling PowerToys checkout.'
}
