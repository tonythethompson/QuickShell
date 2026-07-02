# Builds self-contained EXE installers for x64 and ARM64 (WinGet distribution).
param(
    [string]$ExtensionName = "QuickShell",
    [string]$Configuration = "Release",
    [string]$Version = "0.1.0.0",
    [string[]]$Platforms = @("x64", "arm64")
)

$ErrorActionPreference = "Stop"

Write-Host "Building $ExtensionName EXE installers..." -ForegroundColor Green
Write-Host "Version: $Version" -ForegroundColor Yellow
Write-Host "Platforms: $($Platforms -join ', ')" -ForegroundColor Yellow

$ProjectDir = $PSScriptRoot
$ProjectFile = Join-Path $ProjectDir "$ExtensionName.csproj"

if (-not (Test-Path $ProjectFile)) {
    throw "Project file not found: $ProjectFile"
}

Write-Host "Restoring NuGet packages..." -ForegroundColor Yellow
dotnet restore $ProjectFile
if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed with exit code $LASTEXITCODE" }

foreach ($Platform in $Platforms) {
    Write-Host "`n=== Building $Platform ===" -ForegroundColor Cyan

    $publishDir = Join-Path $ProjectDir "bin\$Configuration\win-$Platform\publish"
    if (Test-Path $publishDir) {
        Remove-Item -Path $publishDir -Recurse -Force
    }

    Write-Host "Publishing $Platform application..." -ForegroundColor Yellow
    dotnet publish $ProjectFile `
        --configuration $Configuration `
        --runtime "win-$Platform" `
        --self-contained true `
        -p:WinGetBuild=true `
        --output $publishDir

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for $Platform with exit code $LASTEXITCODE"
    }

    $fileCount = (Get-ChildItem -Path $publishDir -Recurse -File).Count
    Write-Host "Published $fileCount files to $publishDir" -ForegroundColor Green

    $runPlatform = if ($Platform -eq "arm64") { "ARM64" } else { "x64" }
    $buildRunPlugin = Join-Path (Split-Path $ProjectDir -Parent) "scripts\build-run-plugin.ps1"
    Write-Host "Building PowerToys Run plugin ($runPlatform)..." -ForegroundColor Yellow
    & $buildRunPlugin -Configuration $Configuration -Platform $runPlatform
    if ($LASTEXITCODE -ne 0) {
        throw "build-run-plugin.ps1 failed for $runPlatform with exit code $LASTEXITCODE"
    }

    $runPluginSource = "..\QuickShell.Run\bin\$runPlatform\$Configuration\package"
    $setupTemplate = Get-Content (Join-Path $ProjectDir "setup-template.iss") -Raw
    $setupScript = $setupTemplate -replace '#define AppVersion ".*"', "#define AppVersion `"$Version`""
    $setupScript = $setupScript -replace '#define RunPluginSource ".*"', "#define RunPluginSource `"$runPluginSource`""
    $setupScript = $setupScript -replace 'OutputBaseFilename=(.*?)\{#AppVersion\}', "OutputBaseFilename=`$1{#AppVersion}-$Platform"
    $setupScript = $setupScript -replace 'Source: "bin\\Release\\win-x64\\publish', "Source: `"bin\Release\win-$Platform\publish"

    if ($Platform -eq "arm64") {
        $setupScript = $setupScript -replace '(\[Setup\][^\[]*)(MinVersion=)', "`$1ArchitecturesAllowed=arm64`r`nArchitecturesInstallIn64BitMode=arm64`r`n`$2"
    }
    else {
        $setupScript = $setupScript -replace '(\[Setup\][^\[]*)(MinVersion=)', "`$1ArchitecturesAllowed=x64compatible`r`nArchitecturesInstallIn64BitMode=x64compatible`r`n`$2"
    }

    $platformIss = Join-Path $ProjectDir "setup-$Platform.iss"
    $setupScript | Out-File -FilePath $platformIss -Encoding UTF8

    $InnoSetupPath = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
    if (-not (Test-Path $InnoSetupPath)) {
        $InnoSetupPath = "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
    }
    if (-not (Test-Path $InnoSetupPath)) {
        $InnoSetupPath = "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    }

    if (-not (Test-Path $InnoSetupPath)) {
        throw "Inno Setup 6 not found. Install from https://jrsoftware.org/isinfo.php or use the GitHub Actions workflow."
    }

    Write-Host "Creating $Platform installer with Inno Setup..." -ForegroundColor Yellow
    & $InnoSetupPath $platformIss
    if ($LASTEXITCODE -ne 0) {
        throw "Inno Setup failed for $Platform with exit code $LASTEXITCODE"
    }

    $installer = Get-ChildItem (Join-Path $ProjectDir "bin\$Configuration\installer\*-$Platform.exe") -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($installer) {
        $sizeMB = [math]::Round($installer.Length / 1MB, 2)
        Write-Host "Created installer: $($installer.Name) ($sizeMB MB)" -ForegroundColor Green
    }
    else {
        throw "Installer file not found for $Platform"
    }
}

Write-Host "`nBuild completed successfully." -ForegroundColor Green
