# Builds a local Raycast extension ZIP + optional Inno sideload installer for
# development / Store packaging. GitHub Releases and WinGet do NOT publish these
# artifacts; end users install from the Raycast Store.
param(
    [Parameter(Mandatory)]
    [string]$Version,

    [ValidateSet('Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$raycastRoot = Join-Path $repoRoot 'QuickShell.Raycast'
$outputRoot = Join-Path $raycastRoot "bin\$Configuration"
$stagingRoot = Join-Path $outputRoot 'package'
$zipPath = Join-Path $outputRoot 'QuickShell.Raycast.zip'
$installerDir = Join-Path $outputRoot 'installer'

if (-not (Test-Path $raycastRoot)) {
    throw "QuickShell.Raycast project not found at $raycastRoot"
}

Write-Host "Building Quick Shell for Raycast v$Version..." -ForegroundColor Cyan
& (Join-Path $PSScriptRoot 'build-raycast-suggest.ps1') `
    -ProjectRoot $repoRoot `
    -Configuration $Configuration `
    -Platform x64
if ($LASTEXITCODE -ne 0) {
    throw "QuickShell.Suggest publish failed with exit code $LASTEXITCODE"
}

Push-Location $raycastRoot
try {
    if (Get-Command npm -ErrorAction SilentlyContinue) {
        npm ci
        if ($LASTEXITCODE -ne 0) { throw "npm ci failed with exit code $LASTEXITCODE" }

        npm test
        if ($LASTEXITCODE -ne 0) { throw "npm test failed with exit code $LASTEXITCODE" }

        npm run build
        if ($LASTEXITCODE -ne 0) { throw "npm run build failed with exit code $LASTEXITCODE" }
    }
    else {
        throw "Node.js/npm is required to build QuickShell.Raycast"
    }
}
finally {
    Pop-Location
}

if (Test-Path $stagingRoot) {
    Remove-Item $stagingRoot -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $stagingRoot | Out-Null

$copyItems = @(
    'package.json',
    'package-lock.json',
    'raycast-env.d.ts',
    'tsconfig.json',
    'assets',
    'src'
)

foreach ($item in $copyItems) {
    $source = Join-Path $raycastRoot $item
    if (-not (Test-Path $source)) {
        throw "Missing Raycast build input: $source"
    }

    Copy-Item $source (Join-Path $stagingRoot $item) -Recurse -Force
}

$testsPath = Join-Path $stagingRoot 'src\__tests__'
if (Test-Path $testsPath) {
    Remove-Item $testsPath -Recurse -Force
}

Copy-Item (Join-Path $raycastRoot 'node_modules') (Join-Path $stagingRoot 'node_modules') -Recurse -Force

if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

New-Item -ItemType Directory -Force -Path $installerDir | Out-Null
Compress-Archive -Path (Join-Path $stagingRoot '*') -DestinationPath $zipPath
Write-Host "Created $zipPath" -ForegroundColor Green

$templatePath = Join-Path $raycastRoot 'setup-template.iss'
if (-not (Test-Path $templatePath)) {
    throw "Missing Raycast installer template: $templatePath"
}

$setupTemplate = Get-Content $templatePath -Raw
$setupScript = $setupTemplate -replace '#define AppVersion ".*"', "#define AppVersion `"$Version`""
$setupScript = $setupScript -replace '#define ExtensionSource ".*"', "#define ExtensionSource `"$($stagingRoot.Replace('\', '\\'))`""

$platformIss = Join-Path $raycastRoot 'setup-release.iss'
$setupScript | Out-File -FilePath $platformIss -Encoding UTF8

$innoSetupPath = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
if (-not (Test-Path $innoSetupPath)) {
    $innoSetupPath = "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
}
if (-not (Test-Path $innoSetupPath)) {
    $innoSetupPath = "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
}
if (-not (Test-Path $innoSetupPath)) {
    throw "Inno Setup 6 not found. Install Inno Setup or run the GitHub release workflow."
}

& $innoSetupPath $platformIss
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup failed for QuickShellforRaycast with exit code $LASTEXITCODE"
}

$installerPath = Join-Path $installerDir "QuickShellforRaycast-Setup-$Version-x64.exe"
if (-not (Test-Path $installerPath)) {
    throw "Raycast installer not found: $installerPath"
}

Write-Host "Created $installerPath" -ForegroundColor Green
