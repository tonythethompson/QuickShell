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

if (-not (Test-Path -LiteralPath $ProjectRoot)) {
    throw "ProjectRoot not found: $ProjectRoot"
}
# Absolute paths so printed SkipDeploy commands stay valid after Set-Location.
$ProjectRoot = (Resolve-Path -LiteralPath $ProjectRoot).ProviderPath

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
$assetPath = (Resolve-Path -LiteralPath $assetPath).ProviderPath

$item = Get-Item -LiteralPath $assetPath
Write-Host ("Published: {0} ({1:N1} KB)" -f $item.FullName, ($item.Length / 1KB)) -ForegroundColor Green

function Test-SuggestGenerationEqualsOne {
    param([object]$Generation)

    # Reject strings/bools so "1" is not coerced to 1.
    if ($null -eq $Generation -or $Generation -is [string] -or $Generation -is [bool] -or $Generation -is [char]) {
        return $false
    }
    if (-not (
        $Generation -is [byte] -or $Generation -is [sbyte] -or
        $Generation -is [int16] -or $Generation -is [uint16] -or
        $Generation -is [int] -or $Generation -is [uint32] -or
        $Generation -is [long] -or $Generation -is [uint64] -or
        $Generation -is [float] -or $Generation -is [double] -or
        $Generation -is [decimal]
    )) {
        return $false
    }
    if (($Generation -is [float] -or $Generation -is [double]) -and -not [double]::IsFinite([double]$Generation)) {
        return $false
    }
    return $Generation -eq 1
}

function Test-SuggestPillShape {
    param([object]$Pill)

    if ($null -eq $Pill -or $Pill -is [string] -or $Pill -is [ValueType] -or $Pill -is [System.Array]) {
        return $false
    }
    foreach ($field in @('command', 'taskType', 'typeTitle', 'displayTitle', 'tooltip')) {
        $prop = $Pill.PSObject.Properties[$field]
        if ($null -eq $prop -or $prop.Value -isnot [string]) {
            return $false
        }
    }
    return $true
}

if (-not $SkipSmokeTest) {
    if (-not (Test-Path -LiteralPath $Directory)) {
        throw "Smoke-test directory not found: $Directory"
    }

    Write-Host "Smoke-testing suggest against $Directory ..." -ForegroundColor Cyan
    $stdout = & $assetPath suggest --dir $Directory --generation 1
    if ($LASTEXITCODE -ne 0) {
        throw "Suggest.exe exited with code $LASTEXITCODE"
    }

    # Native stdout may be a string or a line array; always parse as one JSON document.
    $json = if ($null -eq $stdout) { '' } elseif ($stdout -is [string]) { $stdout } else { $stdout -join "`n" }
    $parsed = $json | ConvertFrom-Json
    if ($null -eq $parsed -or $parsed -is [System.Array] -or $parsed -is [string] -or $parsed -is [ValueType]) {
        throw 'Suggest.exe returned a non-object JSON payload.'
    }
    if ($null -eq $parsed.PSObject.Properties['generation'] -or $null -eq $parsed.PSObject.Properties['pills']) {
        throw 'Suggest.exe response missing generation or pills.'
    }
    if (-not (Test-SuggestGenerationEqualsOne -Generation $parsed.generation)) {
        throw "Suggest.exe generation mismatch (wanted 1, got $($parsed.generation))."
    }
    if ($null -eq $parsed.pills -or $parsed.pills -isnot [System.Array]) {
        throw 'Suggest.exe response pills must be an array.'
    }
    foreach ($pill in @($parsed.pills)) {
        if (-not (Test-SuggestPillShape -Pill $pill)) {
            throw 'Suggest.exe returned a malformed pill.'
        }
    }
    $pillCount = @($parsed.pills).Count
    Write-Host "Smoke test OK: generation=$($parsed.generation), pills=$pillCount" -ForegroundColor Green
}

if ($SkipDeploy) {
    $raycastRoot = (Resolve-Path -LiteralPath (Join-Path $ProjectRoot 'QuickShell.Raycast')).ProviderPath
    Write-Host ''
    Write-Host 'Suggest ready (-SkipDeploy). Start Raycast yourself:' -ForegroundColor Yellow
    Write-Host ("  `$env:QUICKSHELL_SUGGEST_EXE = '{0}'" -f $assetPath) -ForegroundColor DarkGray
    Write-Host ("  Set-Location -LiteralPath '{0}'" -f $raycastRoot)
    Write-Host '  npm run dev'
    return
}

# -BuildOnly should not stop or launch Raycast; only package/build.
if (-not $BuildOnly) {
    Write-Host '2/3 Stopping Raycast (so the extension can reload)...' -ForegroundColor Cyan
    # Stop-RaycastProcesses already uses -ErrorAction SilentlyContinue; let unexpected errors surface.
    Stop-RaycastProcesses
}

Write-Host '3/3 Building and deploying QuickShell.Raycast...' -ForegroundColor Cyan
Deploy-RaycastExtension `
    -ProjectRoot $ProjectRoot `
    -SkipTests:$SkipTests `
    -BuildOnly:$BuildOnly `
    -StartDevServer:(-not $BuildOnly)

if (-not $BuildOnly -and -not $NoRestart) {
    Write-Host 'Restarting Raycast...' -ForegroundColor Cyan
    if (-not (Start-RaycastApp)) {
        Write-Warning 'Raycast deploy finished but Raycast could not be restarted.'
    }
}

Write-Host ''
if ($BuildOnly) {
    Write-Host 'Raycast Suggest build complete (-BuildOnly).' -ForegroundColor Green
    Write-Host ("Asset: {0}" -f $assetPath) -ForegroundColor DarkGray
}
else {
    Write-Host 'Raycast Suggest + deploy complete.' -ForegroundColor Green
    Write-Host 'Use the new develop terminal (npm run dev) or search Quick Shell in Raycast.'
    Write-Host 'In the workspace form, expect Suggest copy (not "Suggest.exe is unavailable").' -ForegroundColor DarkGray
    Write-Host ("Asset: {0}" -f $assetPath) -ForegroundColor DarkGray
}
