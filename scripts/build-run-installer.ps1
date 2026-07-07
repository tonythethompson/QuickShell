param(
    [Parameter(Mandatory)]
    [string]$Version,

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [ValidateSet('x64', 'ARM64')]
    [string[]]$Platforms = @('x64', 'ARM64')
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$runRoot = Join-Path $repoRoot 'QuickShell.Run'
$buildScript = Join-Path $repoRoot 'scripts\build-run-plugin.ps1'
$templatePath = Join-Path $runRoot 'setup-template.iss'
$installerDir = Join-Path $runRoot "bin\$Configuration\installer"

if (-not (Test-Path $templatePath)) {
    throw "Missing Run installer template: $templatePath"
}

New-Item -ItemType Directory -Force -Path $installerDir | Out-Null

foreach ($platform in $Platforms) {
    Write-Host "Building Quick Shell for Run installer ($Configuration | $platform)..." -ForegroundColor Cyan
    & $buildScript -Configuration $Configuration -Platform $platform
    if ($LASTEXITCODE -ne 0) {
        throw "build-run-plugin.ps1 failed for $platform with exit code $LASTEXITCODE"
    }

    $pluginSource = Join-Path $runRoot "bin\$platform\$Configuration\package"
    if (-not (Test-Path $pluginSource)) {
        throw "Run plugin package folder not found: $pluginSource"
    }

    $platformSlug = if ($platform -eq 'ARM64') { 'arm64' } else { 'x64' }
    $setupTemplate = Get-Content $templatePath -Raw
    $setupScript = $setupTemplate -replace '#define AppVersion ".*"', "#define AppVersion `"$Version`""
    $setupScript = $setupScript -replace '#define PluginSource ".*"', "#define PluginSource `"$($pluginSource.Replace('\', '\\'))`""
    $setupScript = $setupScript -replace 'OutputBaseFilename=\{#InstallerBaseName\}-Setup-\{#AppVersion\}-PLATFORM', "OutputBaseFilename={#InstallerBaseName}-Setup-{#AppVersion}-$platformSlug"
    $setupScript = $setupScript -replace 'OutputDir=bin\\Release\\installer', "OutputDir=bin\$Configuration\installer"

    if ($platformSlug -eq 'arm64') {
        $setupScript = $setupScript -replace '(\[Setup\][^\[]*)(MinVersion=)', "`$1ArchitecturesAllowed=arm64`r`nArchitecturesInstallIn64BitMode=arm64`r`n`$2"
    }
    else {
        $setupScript = $setupScript -replace '(\[Setup\][^\[]*)(MinVersion=)', "`$1ArchitecturesAllowed=x64compatible`r`nArchitecturesInstallIn64BitMode=x64compatible`r`n`$2"
    }

    $platformIss = Join-Path $runRoot "setup-$platformSlug.iss"
    $setupScript | Out-File -FilePath $platformIss -Encoding UTF8

    $innoSetupPath = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
    if (-not (Test-Path $innoSetupPath)) {
        $innoSetupPath = "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
    }
    if (-not (Test-Path $innoSetupPath)) {
        $innoSetupPath = "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    }
    if (-not (Test-Path $innoSetupPath)) {
        throw "Inno Setup 6 not found."
    }

    & $innoSetupPath $platformIss
    if ($LASTEXITCODE -ne 0) {
        throw "Inno Setup failed for Run ($platform) with exit code $LASTEXITCODE"
    }

    $installerPath = Join-Path $installerDir "QuickShellforRun-Setup-$Version-$platformSlug.exe"
    if (-not (Test-Path $installerPath)) {
        throw "Run installer not found: $installerPath"
    }

    Write-Host "Created $installerPath" -ForegroundColor Green
}
