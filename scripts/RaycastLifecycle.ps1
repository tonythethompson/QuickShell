#Requires -Version 5.1
# Shared helpers for stopping Raycast and deploying the QuickShell.Raycast extension.

function Get-RaycastRoot {
    param([string]$ProjectRoot)

    Join-Path $ProjectRoot 'QuickShell.Raycast'
}

function Get-RaycastExecutable {
    $candidates = @(
        Join-Path $env:LOCALAPPDATA 'Programs\Raycast\Raycast.exe'
        Join-Path ${env:ProgramFiles} 'Raycast\Raycast.exe'
        Join-Path ${env:ProgramFiles(x86)} 'Raycast\Raycast.exe'
        Join-Path $env:LOCALAPPDATA 'Microsoft\WindowsApps\Raycast.exe'
    )

    $found = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    if ($found) {
        return $found
    }

    $package = Get-AppxPackage -Name 'Raycast.Raycast' -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($package) {
        $packageExe = Join-Path $package.InstallLocation 'Raycast\Raycast.exe'
        if (Test-Path -LiteralPath $packageExe) {
            return $packageExe
        }
    }

    return $null
}

function Get-RaycastAppUserModelId {
    $package = Get-AppxPackage -Name 'Raycast.Raycast' -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($package -and $package.PackageFamilyName) {
        return "$($package.PackageFamilyName)!Raycast"
    }

    $startApp = Get-StartApps -ErrorAction SilentlyContinue |
        Where-Object {
            $_.Name -eq 'Raycast' -or
            $_.AppID -like 'Raycast.Raycast_*'
        } |
        Select-Object -First 1

    if ($startApp) {
        return $startApp.AppID
    }

    return $null
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
    if (Get-Process -Name 'Raycast' -ErrorAction SilentlyContinue) {
        Write-Host 'Raycast is already running.'
        return $true
    }

    # Prefer classic / alias exe paths (desktop installs and app execution aliases).
    $raycastExe = @(
        Join-Path $env:LOCALAPPDATA 'Programs\Raycast\Raycast.exe'
        Join-Path ${env:ProgramFiles} 'Raycast\Raycast.exe'
        Join-Path ${env:ProgramFiles(x86)} 'Raycast\Raycast.exe'
        Join-Path $env:LOCALAPPDATA 'Microsoft\WindowsApps\Raycast.exe'
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1

    if ($raycastExe) {
        Write-Host "Starting Raycast: $raycastExe"
        Start-Process -FilePath $raycastExe
        Start-Sleep -Seconds 2
        return $true
    }

    # Store / WinGet AppX packages usually need AUMID activation, not a raw Start-Process on the package exe.
    $aumid = Get-RaycastAppUserModelId
    if ($aumid) {
        Write-Host "Starting Raycast (Store/AppX): $aumid"
        Start-Process -FilePath 'explorer.exe' -ArgumentList "shell:AppsFolder\$aumid"
        Start-Sleep -Seconds 2
        return $true
    }

    # Last resort: AppX install-location Raycast.exe if resolvable.
    $packageExe = Get-RaycastExecutable
    if ($packageExe) {
        Write-Host "Starting Raycast: $packageExe"
        Start-Process -FilePath $packageExe
        Start-Sleep -Seconds 2
        return $true
    }

    Write-Warning 'Raycast was not found. Install Raycast for Windows or pass -SkipRaycast.'
    return $false
}

function Invoke-NpmCommand {
    param(
        [Parameter(Mandatory)]
        [string[]]$Arguments,
        [Parameter(Mandatory)]
        [string]$FailureMessage
    )

    # npm writes progress/notices to stderr. With $ErrorActionPreference Stop that can
    # become a terminating error even when the exit code is 0 (especially under 2>&1).
    $previousEap = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & npm @Arguments
        if ($LASTEXITCODE -ne 0) {
            throw "$FailureMessage (exit code $LASTEXITCODE)"
        }
    }
    finally {
        $ErrorActionPreference = $previousEap
    }
}

function Deploy-RaycastExtension {
    param(
        [string]$ProjectRoot,
        [ValidateSet('Debug', 'Release')]
        [string]$Configuration = 'Release',
        [switch]$SkipTests,
        [switch]$BuildOnly,
        [switch]$StartDevServer
    )

    if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
        throw 'Deploy-RaycastExtension requires -ProjectRoot (got empty). Check deploy-all.ps1 line endings if backtick argument continuations are failing.'
    }

    $raycastRoot = Get-RaycastRoot -ProjectRoot $ProjectRoot
    if (-not (Test-Path $raycastRoot)) {
        throw "QuickShell.Raycast project not found at $raycastRoot"
    }

    if (-not (Get-Command npm -ErrorAction SilentlyContinue)) {
        throw 'Node.js/npm is required to build QuickShell.Raycast.'
    }

    if ($env:OS -eq 'Windows_NT') {
        $suggestBuildScript = Join-Path $ProjectRoot 'scripts\build-raycast-suggest.ps1'
        if (-not (Test-Path -LiteralPath $suggestBuildScript)) {
            throw "QuickShell.Suggest build script not found at $suggestBuildScript"
        }
        & $suggestBuildScript -ProjectRoot $ProjectRoot -Configuration $Configuration -Platform x64
        if ($LASTEXITCODE -ne 0) {
            throw "QuickShell.Suggest publish failed with exit code $LASTEXITCODE"
        }
    }

    Push-Location $raycastRoot
    try {
        if (-not (Test-Path 'node_modules/@raycast/api')) {
            Write-Host 'Installing Raycast extension dependencies...'
            Invoke-NpmCommand -Arguments @('install') -FailureMessage 'npm install failed'
        }

        if (-not $SkipTests) {
            Write-Host 'Running Raycast extension tests...'
            Invoke-NpmCommand -Arguments @('test') -FailureMessage 'npm test failed'
        }

        Write-Host 'Building Raycast extension...'
        Invoke-NpmCommand -Arguments @('run', 'build') -FailureMessage 'npm run build failed'

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
