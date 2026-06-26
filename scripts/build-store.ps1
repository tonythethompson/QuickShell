#Requires -Version 5.1
<#
.SYNOPSIS
    Build a Microsoft Store MSIX bundle for Quick Shell.

.DESCRIPTION
    Builds Release x64 + ARM64 packages using the public NuGet CmdPal SDK, then
    bundles them for Partner Center upload. Microsoft Store MSIX submissions do
    not require a local CA-trusted signing certificate; the Store re-signs the
    package after certification.

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

function Get-MakeAppxPath {
    $kitsRoot = "${env:ProgramFiles(x86)}\Windows Kits\10\bin"
    if (-not (Test-Path $kitsRoot)) {
        throw 'Windows 10 SDK not found. Install the Windows SDK (makeappx.exe).'
    }

    $makeappx = Get-ChildItem -Path $kitsRoot -Recurse -Filter 'makeappx.exe' -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '\\x64\\makeappx\.exe$' } |
        Sort-Object { [version]($_.Directory.Parent.Name) } -Descending |
        Select-Object -First 1

    if (-not $makeappx) {
        throw 'makeappx.exe not found in Windows SDK.'
    }

    return $makeappx.FullName
}

function Get-PackageVersion {
    [xml]$manifest = Get-Content (Join-Path $ProjectDir 'Package.appxmanifest')
    return [string]$manifest.Package.Identity.Version
}

function Build-PlatformPackage {
    param(
        [string]$MsBuild,
        [string]$Platform
    )

    Write-Host "Building unsigned Store MSIX (Release|$Platform)..."
    & $MsBuild $ProjectFile `
        /p:Configuration=Release `
        /p:Platform=$Platform `
        /p:UseLocalCmdPalSdk=false `
        /p:StoreBuild=true `
        /p:PackageCertificateKeyFile= `
        /p:PackageCertificateThumbprint= `
        /p:PackageCertificatePassword= `
        /p:AppxPackageSigningEnabled=false `
        /p:AppxPackageDir="AppPackages\$Platform\" `
        /t:Build `
        /v:minimal | Out-Host

    if ($LASTEXITCODE -ne 0) {
        throw "Build failed for $Platform with exit code $LASTEXITCODE"
    }

    $outputDir = Join-Path $ProjectDir "AppPackages\$Platform"
    $folder = Get-ChildItem -Path $outputDir -Directory -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1

    if (-not $folder) {
        throw "No package folder found under $outputDir"
    }

    $msix = Get-ChildItem -Path $folder.FullName -Filter '*.msix' | Select-Object -First 1
    if (-not $msix) {
        throw "No .msix found in $($folder.FullName)"
    }

    return $msix.FullName
}

Push-Location $ProjectRoot
try {
    Write-Host 'Generating MSIX logo assets...'
    & (Join-Path $PSScriptRoot 'generate-assets.ps1')

    $msbuild = Get-MsBuildPath
    $version = Get-PackageVersion
    $bundleDir = Join-Path $ProjectDir "AppPackages\Store_$version"
    New-Item -ItemType Directory -Path $bundleDir -Force | Out-Null

    $x64Msix = Build-PlatformPackage -MsBuild $msbuild -Platform x64
    $arm64Msix = Build-PlatformPackage -MsBuild $msbuild -Platform ARM64

    $x64Name = Split-Path $x64Msix -Leaf
    $arm64Name = Split-Path $arm64Msix -Leaf
    $mappingFile = Join-Path $bundleDir 'bundle_mapping.txt'
    @(
        '[Files]'
        "`"$x64Msix`" `"$x64Name`""
        "`"$arm64Msix`" `"$arm64Name`""
    ) | Set-Content -Path $mappingFile -Encoding ASCII

    $bundlePath = Join-Path $bundleDir "QuickShell_$version`_Store.msixbundle"
    $makeappx = Get-MakeAppxPath
    Write-Host "Creating bundle $bundlePath ..."
    & $makeappx bundle /f $mappingFile /p $bundlePath /o | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "makeappx bundle failed with exit code $LASTEXITCODE"
    }

    Write-Host ''
    Write-Host 'Store bundle ready:'
    Write-Host "  $bundlePath"
    Write-Host ''
    Write-Host 'Upload this file in Partner Center -> Submission -> Packages, then Save.'
    Write-Host 'Microsoft Store will re-sign the MSIX bundle after certification.'
}
finally {
    Pop-Location
}
