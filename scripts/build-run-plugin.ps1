param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [ValidateSet('x64', 'ARM64')]
    [string]$Platform = 'x64',
    [switch]$Deploy
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'QuickShell.Run\QuickShell.Run.csproj'
$outputRoot = Join-Path $repoRoot "QuickShell.Run\bin\$Platform\$Configuration\net9.0-windows10.0.22621.0"
$pluginRoot = Join-Path $env:LOCALAPPDATA 'Microsoft\PowerToys\PowerToys Run\Plugins\QuickShell'
$stagingRoot = Join-Path $repoRoot "QuickShell.Run\bin\$Platform\$Configuration\package"
$zipPath = Join-Path $repoRoot "QuickShell.Run\bin\$Platform\$Configuration\QuickShell.Run-$Platform.zip"

Write-Host "Building Quick Shell Run plugin ($Configuration | $Platform)..."
dotnet build $project -c $Configuration -p:Platform=$Platform
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

if (-not (Test-Path (Join-Path $outputRoot 'plugin.json'))) {
    throw "Build output is missing plugin.json. qs will not register until it is deployed."
}

if (Test-Path $stagingRoot) {
    Remove-Item $stagingRoot -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $stagingRoot | Out-Null
Copy-Item (Join-Path $outputRoot 'QuickShell.Run.dll') $stagingRoot
Copy-Item (Join-Path $outputRoot 'QuickShell.Core.dll') $stagingRoot
Copy-Item (Join-Path $outputRoot 'plugin.json') $stagingRoot
Copy-Item (Join-Path $outputRoot 'Images') (Join-Path $stagingRoot 'Images') -Recurse

if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

Compress-Archive -Path (Join-Path $stagingRoot '*') -DestinationPath $zipPath
Write-Host "Created $zipPath"

function Test-PowerToysRunning {
    Get-Process -Name 'PowerToys', 'PowerToys.PowerLauncher' -ErrorAction SilentlyContinue |
        Select-Object -First 1
}

function Copy-PluginPayload {
    param(
        [string]$SourceRoot,
        [string]$DestinationRoot
    )

    New-Item -ItemType Directory -Force -Path $DestinationRoot | Out-Null

    $lockedFiles = @()
    Get-ChildItem $SourceRoot -Recurse -File | ForEach-Object {
        $relative = $_.FullName.Substring($SourceRoot.Length).TrimStart('\')
        $target = Join-Path $DestinationRoot $relative
        $targetDir = Split-Path $target -Parent
        if (-not (Test-Path $targetDir)) {
            New-Item -ItemType Directory -Force -Path $targetDir | Out-Null
        }

        try {
            Copy-Item $_.FullName -Destination $target -Force -ErrorAction Stop
        }
        catch [System.Management.Automation.RuntimeException] {
            $lockedFiles += $relative
        }
        catch [System.IO.IOException] {
            $lockedFiles += $relative
        }
    }

    return $lockedFiles
}

if ($Deploy) {
    $running = Test-PowerToysRunning
    if ($running) {
        Write-Host "PowerToys is running. DLLs may be locked; plugin.json and images will still be copied." -ForegroundColor Yellow
        Write-Host "Restart PowerToys after deploy so qs and updated DLLs load." -ForegroundColor Yellow
    }

    # Do not wipe the plugin folder first. A failed delete while PowerToys holds DLLs
    # removes plugin.json/Images and leaves only the locked DLLs — that breaks qs.
    $lockedFiles = Copy-PluginPayload -SourceRoot $stagingRoot -DestinationRoot $pluginRoot

    $pluginJson = Join-Path $pluginRoot 'plugin.json'
    if (-not (Test-Path $pluginJson)) {
        throw "Deploy failed: plugin.json is missing at $pluginRoot"
    }

    $keyword = (Get-Content $pluginJson -Raw | ConvertFrom-Json).ActionKeyword
    Write-Host "Deployed to $pluginRoot (action keyword: $keyword)"

    if ($lockedFiles.Count -gt 0) {
        Write-Host "These files were locked and may still be the previous build:" -ForegroundColor Yellow
        $lockedFiles | ForEach-Object { Write-Host "  $_" -ForegroundColor Yellow }
        Write-Host "Quit PowerToys, rerun with -Deploy, then start PowerToys again." -ForegroundColor Yellow
    }
    else {
        Write-Host "Restart PowerToys to load the plugin." -ForegroundColor Green
    }
}
