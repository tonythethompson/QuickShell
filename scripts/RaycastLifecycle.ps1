#Requires -Version 5.1
# Shared helpers for stopping Raycast and deploying the QuickShell.Raycast extension.

function Get-RaycastRoot {
    param([string]$ProjectRoot)

    Join-Path $ProjectRoot 'QuickShell.Raycast'
}

function Get-RaycastExecutable {
    @(
        Join-Path $env:LOCALAPPDATA 'Programs\Raycast\Raycast.exe'
        Join-Path ${env:ProgramFiles} 'Raycast\Raycast.exe'
        Join-Path ${env:ProgramFiles(x86)} 'Raycast\Raycast.exe'
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1
}

function Stop-RaycastProcesses {
    $stopped = $false
    foreach ($name in @('Raycast')) {
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

function Start-RaycastApp {
    $raycastExe = Get-RaycastExecutable
    if (-not $raycastExe) {
        Write-Warning 'Raycast was not found. Install Raycast for Windows or pass -SkipRaycast.'
        return $false
    }

    if (Get-Process -Name 'Raycast' -ErrorAction SilentlyContinue) {
        Write-Host "Raycast is already running: $raycastExe"
        return $true
    }

    Write-Host "Starting Raycast: $raycastExe"
    Start-Process -FilePath $raycastExe
    Start-Sleep -Seconds 2
    return $true
}

function Deploy-RaycastExtension {
    param(
        [string]$ProjectRoot,
        [switch]$SkipTests,
        [switch]$BuildOnly,
        [switch]$StartDevServer
    )

    $raycastRoot = Get-RaycastRoot -ProjectRoot $ProjectRoot
    if (-not (Test-Path $raycastRoot)) {
        throw "QuickShell.Raycast project not found at $raycastRoot"
    }

    if (-not (Get-Command npm -ErrorAction SilentlyContinue)) {
        throw 'Node.js/npm is required to build QuickShell.Raycast.'
    }

    Push-Location $raycastRoot
    try {
        if (-not (Test-Path 'node_modules')) {
            Write-Host 'Installing Raycast extension dependencies...'
            npm install
            if ($LASTEXITCODE -ne 0) {
                throw "npm install failed with exit code $LASTEXITCODE"
            }
        }

        if (-not $SkipTests) {
            Write-Host 'Running Raycast extension tests...'
            npm test
            if ($LASTEXITCODE -ne 0) {
                throw "npm test failed with exit code $LASTEXITCODE"
            }
        }

        Write-Host 'Building Raycast extension...'
        npm run build
        if ($LASTEXITCODE -ne 0) {
            throw "npm run build failed with exit code $LASTEXITCODE"
        }

        if ($BuildOnly) {
            Write-Host 'Raycast build complete (-BuildOnly; skipping ray develop).'
            return
        }

        if ($StartDevServer) {
            Write-Host 'Starting Raycast develop server in a new terminal...'
            $devCommand = "Set-Location -LiteralPath '$raycastRoot'; npm run dev"
            Start-Process -FilePath 'powershell.exe' -ArgumentList @(
                '-NoExit',
                '-NoProfile',
                '-ExecutionPolicy', 'Bypass',
                '-Command', $devCommand
            ) | Out-Null
        }
    }
    finally {
        Pop-Location
    }
}
