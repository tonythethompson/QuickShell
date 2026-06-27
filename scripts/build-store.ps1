#Requires -Version 5.1
<#
.SYNOPSIS
    Build a Microsoft Store upload package for Quick Shell.

.DESCRIPTION
    Builds an unsigned Release x64 + ARM64 .msixupload file using MSBuild
    StoreUpload mode. Individual developer accounts do not sign locally;
    Microsoft re-signs the package after certification.

.EXAMPLE
    .\scripts\build-store.ps1
#>

$ErrorActionPreference = 'Stop'
$ProjectRoot = Split-Path -Parent $PSScriptRoot
$ProjectDir = Join-Path $ProjectRoot 'QuickShell'
$ProjectFile = Join-Path $ProjectDir 'QuickShell.csproj'

function Get-MsBuildPath {
    $msbuild = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" `
        -latest -requires Microsoft.Component.MSBuild `
        -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1

    if (-not $msbuild -or -not (Test-Path $msbuild)) {
        throw 'MSBuild not found. Install Visual Studio with the Desktop development workload.'
    }

    return $msbuild
}

function Get-PackageVersion {
    [xml]$manifest = Get-Content (Join-Path $ProjectDir 'Package.appxmanifest')
    return [string]$manifest.Package.Identity.Version
}

Push-Location $ProjectRoot
try {
    Write-Host 'Generating MSIX logo assets...'
    & (Join-Path $PSScriptRoot 'generate-assets.ps1')

    $msbuild = Get-MsBuildPath
    $version = Get-PackageVersion
    $outputDir = Join-Path $ProjectDir 'AppPackages\Store'

    Write-Host "Building unsigned Store upload package (Release|x64 + ARM64 bundle, v$version)..."
    & $msbuild $ProjectFile `
        /p:Configuration=Release `
        /p:Platform=x64 `
        /p:UseLocalCmdPalSdk=false `
        /p:StoreBuild=true `
        /p:PackageCertificateKeyFile= `
        /p:PackageCertificateThumbprint= `
        /p:PackageCertificatePassword= `
        /p:AppxPackageSigningEnabled=false `
        /p:AppxPackageDir="$outputDir\" `
        /t:Build `
        /v:minimal | Out-Host

    if ($LASTEXITCODE -ne 0) {
        throw "Build failed with exit code $LASTEXITCODE"
    }

    $upload = Get-ChildItem -Path $outputDir -Filter '*.msixupload' -Recurse -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1

    if (-not $upload) {
        throw "No .msixupload found under $outputDir"
    }

    $bundle = Get-ChildItem -Path $outputDir -Filter '*.msixbundle' -Recurse -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1

    if ($bundle) {
        $makeappx = Get-ChildItem -Path "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Recurse -Filter 'makeappx.exe' -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match '\\x64\\makeappx\.exe$' } |
            Sort-Object { [version]($_.Directory.Parent.Name) } -Descending |
            Select-Object -First 1

        if ($makeappx) {
            $inspectDir = Join-Path $env:TEMP "QuickShell_StoreBundle_$version"
            if (Test-Path $inspectDir) {
                Remove-Item $inspectDir -Recurse -Force
            }

            & $makeappx.FullName unbundle /p $bundle.FullName /d $inspectDir /o | Out-Null
            if ($LASTEXITCODE -eq 0) {
                [xml]$bundleManifest = Get-Content (Join-Path $inspectDir 'AppxMetadata\AppxBundleManifest.xml')
                $bundleVersion = [string]$bundleManifest.Bundle.Identity.Version
                if ($bundleVersion -ne $version) {
                    throw "Bundle identity version ($bundleVersion) does not match app version ($version)."
                }

                Write-Host "Verified bundle identity version: $bundleVersion"
            }

            Remove-Item $inspectDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    Write-Host ''
    Write-Host 'Store upload package ready:'
    Write-Host "  $($upload.FullName)"
    Write-Host ''
    Write-Host 'Upload the .msixupload file in Partner Center -> Submission -> Packages, then Save.'
    Write-Host 'Microsoft Store will re-sign the package after certification.'
}
finally {
    Pop-Location
}
