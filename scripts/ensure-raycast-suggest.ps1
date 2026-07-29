<#
.SYNOPSIS
  Publish Suggest.exe into Raycast assets, then build and deploy the Raycast extension.

.DESCRIPTION
  One-shot Raycast test loop (does not touch CmdPal or PowerToys Run):
    1. Publish QuickShell.Suggest.exe into QuickShell.Raycast/assets/
    2. Smoke-test suggest against a directory
    3. npm test/build the Raycast extension and start `npm run dev`
    4. Restart Raycast so the develop extension is live

  Requires .NET 10 SDK + Desktop Runtime, Node.js 22.14+, and Raycast for Windows.

.EXAMPLE
  .\scripts\ensure-raycast-suggest.ps1

.EXAMPLE
  .\scripts\ensure-raycast-suggest.ps1 -Directory D:\Dev\some-project -SkipTests

.EXAMPLE
  .\scripts\ensure-raycast-suggest.ps1 -BuildOnly
#>
param(
    [string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot),

    [string]$Directory = $ProjectRoot,

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [ValidateSet('x64')]
    [string]$Platform = 'x64',

    [switch]$SkipSmokeTest,
    [switch]$SkipTests,
    [switch]$BuildOnly,
    [switch]$NoRestart,
    [switch]$SkipDeploy
)

$ErrorActionPreference = 'Stop'

if ($env:OS -ne 'Windows_NT') {
    throw 'ensure-raycast-suggest.ps1 is Windows-only (Suggest.exe is not used on macOS).'
}

$buildScript = Join-Path $PSScriptRoot 'build-raycast-suggest.ps1'
$lifecycleScript = Join-Path $PSScriptRoot 'RaycastLifecycle.ps1'
if (-not (Test-Path -LiteralPath $buildScript)) {
    throw "Build script not found at $buildScript"
}
if (-not (Test-Path -LiteralPath $lifecycleScript)) {
    throw "Raycast lifecycle helpers not found at $lifecycleScript"
}

. $lifecycleScript

Write-Host '1/3 Publishing QuickShell.Suggest.exe into Raycast assets...' -ForegroundColor Cyan
& $buildScript -ProjectRoot $ProjectRoot -Configuration $Configuration -Platform $Platform
if ($LASTEXITCODE -ne 0) {
    throw "build-raycast-suggest.ps1 failed with exit code $LASTEXITCODE"
}

$assetPath = Join-Path $ProjectRoot 'QuickShell.Raycast\assets\QuickShell.Suggest.exe'
if (-not (Test-Path -LiteralPath $assetPath)) {
    throw "Expected asset missing after publish: $assetPath"
}

$item = Get-Item -LiteralPath $assetPath
Write-Host ("Published: {0} ({1:N1} KB)" -f $item.FullName, ($item.Length / 1KB)) -ForegroundColor Green

if (-not $SkipSmokeTest) {
    if (-not (Test-Path -LiteralPath $Directory)) {
        throw "Smoke-test directory not found: $Directory"
    }

    Write-Host "Smoke-testing suggest against $Directory ..." -ForegroundColor Cyan
    $stdout = & $assetPath suggest --dir $Directory --generation 1
    if ($LASTEXITCODE -ne 0) {
        throw "Suggest.exe exited with code $LASTEXITCODE"
    }

    $parsed = $stdout | ConvertFrom-Json
    $pillCount = @($parsed.pills).Count
    Write-Host "Smoke test OK: generation=$($parsed.generation), pills=$pillCount" -ForegroundColor Green
}

if ($SkipDeploy) {
    Write-Host ''
    Write-Host 'Suggest ready (-SkipDeploy). Start Raycast yourself:' -ForegroundColor Yellow
    Write-Host '  cd QuickShell.Raycast'
    Write-Host '  npm run dev'
    Write-Host ("  `$env:QUICKSHELL_SUGGEST_EXE = '{0}'" -f $assetPath) -ForegroundColor DarkGray
    exit 0
}

Write-Host '2/3 Stopping Raycast (so the extension can reload)...' -ForegroundColor Cyan
$stoppedRaycast = $false
try {
    Stop-RaycastProcesses
    $stoppedRaycast = $true
}
catch {
    Write-Warning "Could not stop Raycast: $($_.Exception.Message)"
}

Write-Host '3/3 Building and deploying QuickShell.Raycast...' -ForegroundColor Cyan
Deploy-RaycastExtension `
    -ProjectRoot $ProjectRoot `
    -SkipTests:$SkipTests `
    -BuildOnly:$BuildOnly `
    -StartDevServer:(-not $BuildOnly)

if (-not $NoRestart -and $stoppedRaycast) {
    Write-Host 'Restarting Raycast...' -ForegroundColor Cyan
    if (-not (Start-RaycastApp)) {
        Write-Warning 'Raycast deploy finished but Raycast could not be restarted.'
    }
}

Write-Host ''
Write-Host 'Raycast Suggest + deploy complete.' -ForegroundColor Green
if (-not $BuildOnly) {
    Write-Host 'Use the new develop terminal (npm run dev) or search Quick Shell in Raycast.'
}
Write-Host 'In the workspace form, expect Suggest copy (not "Suggest.exe is unavailable").' -ForegroundColor DarkGray
Write-Host ("Asset: {0}" -f $assetPath) -ForegroundColor DarkGray
