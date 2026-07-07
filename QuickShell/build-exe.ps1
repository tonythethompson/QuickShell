# Builds self-contained EXE installers for x64 and ARM64 (WinGet / GitHub Releases).

param(

    [string]$ExtensionName = "QuickShell",

    [string]$Configuration = "Release",

    [string]$Version = "0.1.0.0",

    [string[]]$Platforms = @("x64", "arm64"),

    [ValidateSet("Bundled", "CmdPal", "Both")]

    [string]$Variant = "Both"

)



$ErrorActionPreference = "Stop"



$variantsToBuild = switch ($Variant) {

    "Both" { @("Bundled", "CmdPal") }

    default { @($Variant) }

}



Write-Host "Building $ExtensionName EXE installers..." -ForegroundColor Green

Write-Host "Version: $Version" -ForegroundColor Yellow

Write-Host "Platforms: $($Platforms -join ', ')" -ForegroundColor Yellow

Write-Host "Variants: $($variantsToBuild -join ', ')" -ForegroundColor Yellow



$ProjectDir = $PSScriptRoot

$ProjectFile = Join-Path $ProjectDir "$ExtensionName.csproj"

$RepoRoot = Split-Path -Parent $ProjectDir



if (-not (Test-Path $ProjectFile)) {

    throw "Project file not found: $ProjectFile"

}



function Get-VariantMetadata([string]$InstallerVariant) {

    switch ($InstallerVariant) {

        "CmdPal" {

            @{

                IncludeRunPlugin = "false"

                InstallerBaseName = "QuickShellforCmdPal"

                DisplayName = "Quick Shell for CmdPal"

            }

        }

        default {

            @{

                IncludeRunPlugin = "true"

                InstallerBaseName = "QuickShell"

                DisplayName = "Quick Shell for PowerToys"

            }

        }

    }

}



Write-Host "Restoring NuGet packages..." -ForegroundColor Yellow

dotnet restore $ProjectFile

if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed with exit code $LASTEXITCODE" }



foreach ($Platform in $Platforms) {

    Write-Host "`n=== Building $Platform publish output ===" -ForegroundColor Cyan



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

    $runPluginSource = $null

    if ($variantsToBuild -contains "Bundled") {

        $buildRunPlugin = Join-Path $RepoRoot "scripts\build-run-plugin.ps1"

        Write-Host "Building PowerToys Run plugin ($runPlatform)..." -ForegroundColor Yellow

        & $buildRunPlugin -Configuration $Configuration -Platform $runPlatform

        if ($LASTEXITCODE -ne 0) {

            throw "build-run-plugin.ps1 failed for $runPlatform with exit code $LASTEXITCODE"

        }



        $runPluginSource = Join-Path $RepoRoot "QuickShell.Run\bin\$runPlatform\$Configuration\package"

    }



    foreach ($installerVariant in $variantsToBuild) {

        $metadata = Get-VariantMetadata $installerVariant

        Write-Host "`n--- Creating $Platform / $installerVariant installer ---" -ForegroundColor Cyan



        $setupTemplate = Get-Content (Join-Path $ProjectDir "setup-template.iss") -Raw

        $setupScript = $setupTemplate -replace '#define AppVersion ".*"', "#define AppVersion `"$Version`""

        $setupScript = $setupScript -replace '#define DisplayName ".*"', "#define DisplayName `"$($metadata.DisplayName)`""

        $setupScript = $setupScript -replace '#define InstallerBaseName ".*"', "#define InstallerBaseName `"$($metadata.InstallerBaseName)`""

        $setupScript = $setupScript -replace '#define IncludeRunPlugin ".*"', "#define IncludeRunPlugin `"$($metadata.IncludeRunPlugin)`""



        if ($metadata.IncludeRunPlugin -eq "true") {

            if (-not $runPluginSource) {

                throw "Bundled installer requires Run plugin build output for $Platform."

            }



            $setupScript = $setupScript -replace '#define RunPluginSource ".*"', "#define RunPluginSource `"$($runPluginSource.Replace('\', '\\'))`""

        }



        $setupScript = $setupScript -replace 'OutputDir=bin\\[^\\]+\\installer', ("OutputDir=bin\{0}\installer" -f $Configuration)

        $setupScript = $setupScript -replace 'OutputBaseFilename=\{#InstallerBaseName\}-Setup-\{#AppVersion\}', "OutputBaseFilename={#InstallerBaseName}-Setup-{#AppVersion}-$Platform"

        $setupScript = $setupScript -replace 'Source: "bin\\Release\\win-x64\\publish', ("Source: `"bin\{0}\win-{1}\publish" -f $Configuration, $Platform)

        if ($Platform -eq "arm64") {

            $setupScript = $setupScript -replace '(\[Setup\][^\[]*)(MinVersion=)', "`$1ArchitecturesAllowed=arm64`r`nArchitecturesInstallIn64BitMode=arm64`r`n`$2"

        }

        else {

            $setupScript = $setupScript -replace '(\[Setup\][^\[]*)(MinVersion=)', "`$1ArchitecturesAllowed=x64compatible`r`nArchitecturesInstallIn64BitMode=x64compatible`r`n`$2"

        }



        $platformIss = Join-Path $ProjectDir "setup-$Platform-$installerVariant.iss"

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



        Write-Host "Creating $Platform $installerVariant installer with Inno Setup..." -ForegroundColor Yellow

        & $InnoSetupPath $platformIss

        if ($LASTEXITCODE -ne 0) {

            throw "Inno Setup failed for $Platform ($installerVariant) with exit code $LASTEXITCODE"

        }



        $installerPattern = Join-Path $ProjectDir "bin\$Configuration\installer\$($metadata.InstallerBaseName)-Setup-$Version-$Platform.exe"

        $installer = Get-Item $installerPattern -ErrorAction SilentlyContinue

        if ($installer) {

            $sizeMB = [math]::Round($installer.Length / 1MB, 2)

            Write-Host "Created installer: $($installer.Name) ($sizeMB MB)" -ForegroundColor Green

        }

        else {

            throw "Installer file not found for $Platform ($installerVariant): $installerPattern"

        }

    }

}



Write-Host "`nBuild completed successfully." -ForegroundColor Green

