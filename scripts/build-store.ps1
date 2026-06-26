#Requires -Version 5.1
<#
.SYNOPSIS
    Build a Microsoft Store MSIX bundle for Quick Shell.

.DESCRIPTION
    Builds Release x64 + ARM64 packages using the public NuGet CmdPal SDK, then
    bundles them for Partner Center upload. Requires a Store signing certificate
    matching Partner Center Product identity (see -CertificatePath or -CertificateThumbprint).

.PARAMETER CertificatePath
    Path to the .pfx from Visual Studio "Associate App with the Store" or Partner Center.

.PARAMETER CertificatePassword
    Password for the Store .pfx (optional if the cert is in CurrentUser\My).

.PARAMETER CertificateThumbprint
    Thumbprint of an already-imported Store certificate in CurrentUser\My.

.EXAMPLE
    .\scripts\build-store.ps1 -CertificatePath "$env:USERPROFILE\QuickShell.Store.pfx"
#>
param(
    [string]$CertificatePath,
    [string]$CertificatePassword,
    [string]$CertificateThumbprint
)

$ErrorActionPreference = 'Stop'
$ProjectRoot = Split-Path -Parent $PSScriptRoot
$ProjectDir = Join-Path $ProjectRoot 'QuickShell'
$ProjectFile = Join-Path $ProjectDir 'QuickShell.csproj'
$StorePublisherCn = '31D3C278-3D55-4A42-8A6A-24E16D158B63'

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

function Resolve-StoreCertificateThumbprint {
    if ($CertificateThumbprint) {
        return $CertificateThumbprint
    }

    if ($CertificatePath) {
        if (-not (Test-Path $CertificatePath)) {
            throw "Certificate not found: $CertificatePath"
        }

        $securePassword = $null
        if ($CertificatePassword) {
            $securePassword = ConvertTo-SecureString -String $CertificatePassword -AsPlainText -Force
        }

        $importArgs = @{
            FilePath         = $CertificatePath
            CertStoreLocation = 'Cert:\CurrentUser\My'
            Exportable       = $true
        }
        if ($securePassword) {
            $importArgs.Password = $securePassword
        }

        $imported = Import-PfxCertificate @importArgs
        Write-Host "Imported Store certificate ($($imported.Thumbprint))."
        return $imported.Thumbprint
    }

    $match = Get-ChildItem Cert:\CurrentUser\My -ErrorAction SilentlyContinue |
        Where-Object { $_.Subject -like "*$StorePublisherCn*" -and $_.HasPrivateKey } |
        Sort-Object NotAfter -Descending |
        Select-Object -First 1

    if ($match) {
        Write-Host "Using Store certificate from CurrentUser\My ($($match.Thumbprint))."
        return $match.Thumbprint
    }

    throw @"
No Store signing certificate found.

Associate Quick Shell with the Store in Visual Studio once:
  QuickShell.sln -> right-click QuickShell -> Publish -> Associate App with the Store

Then either:
  - rerun this script (cert is imported automatically), or
  - pass -CertificatePath to the .pfx Partner Center / Visual Studio created.

Dev certificate QuickShell_Dev.pfx cannot sign Store packages.
"@
}

function Get-PackageVersion {
    [xml]$manifest = Get-Content (Join-Path $ProjectDir 'Package.appxmanifest')
    return [string]$manifest.Package.Identity.Version
}

function Build-PlatformPackage {
    param(
        [string]$MsBuild,
        [string]$Platform,
        [string]$Thumbprint
    )

    Write-Host "Building Store MSIX (Release|$Platform)..."
    & $MsBuild $ProjectFile `
        /p:Configuration=Release `
        /p:Platform=$Platform `
        /p:UseLocalCmdPalSdk=false `
        /p:StoreBuild=true `
        /p:PackageCertificateThumbprint=$Thumbprint `
        /p:PackageCertificateKeyFile= `
        /p:PackageCertificatePassword= `
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
    $thumbprint = Resolve-StoreCertificateThumbprint
    $version = Get-PackageVersion
    $bundleDir = Join-Path $ProjectDir "AppPackages\Store_$version"
    New-Item -ItemType Directory -Path $bundleDir -Force | Out-Null

    $x64Msix = Build-PlatformPackage -MsBuild $msbuild -Platform x64 -Thumbprint $thumbprint
    $arm64Msix = Build-PlatformPackage -MsBuild $msbuild -Platform ARM64 -Thumbprint $thumbprint

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
    & $makeappx bundle /f $mappingFile /p $bundlePath | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "makeappx bundle failed with exit code $LASTEXITCODE"
    }

    Write-Host ''
    Write-Host 'Store bundle ready:'
    Write-Host "  $bundlePath"
    Write-Host ''
    Write-Host 'Upload this file in Partner Center -> Submission -> Packages, then Save.'
}
finally {
    Pop-Location
}
