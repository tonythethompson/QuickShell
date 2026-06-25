#Requires -Version 5.1
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [switch]$SkipElevation,
    [switch]$RecreateCertificate
)

$ErrorActionPreference = 'Stop'
$ProjectRoot = Split-Path -Parent $PSScriptRoot
$ProjectDir = Join-Path $ProjectRoot 'QuickShell'
$PfxPath = Join-Path $ProjectDir 'QuickShell_Dev.pfx'
$CerPath = Join-Path $ProjectDir 'QuickShell_Dev.cer'
$CertSubject = 'CN=QuickShell Dev'
$CertPassword = 'QuickShell'
$CodeSigningEku = '1.3.6.1.5.5.7.3.3'

function Test-IsAdmin {
    $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Test-CertificateCanSign {
    param([System.Security.Cryptography.X509Certificates.X509Certificate2]$Certificate)

    if (-not $Certificate.HasPrivateKey) {
        return $false
    }

    if ($Certificate.NotAfter -lt (Get-Date)) {
        return $false
    }

    # MSIX packaging can sign via thumbprint even when the PFX lacks the
    # code-signing EKU (common for older dev self-signed certs).
    $inTrustedPeople = Get-ChildItem Cert:\LocalMachine\TrustedPeople -ErrorAction SilentlyContinue |
        Where-Object { $_.Thumbprint -eq $Certificate.Thumbprint }

    if ($inTrustedPeople) {
        return $true
    }

    foreach ($eku in $Certificate.EnhancedKeyUsageList) {
        if ($eku.Value -eq $CodeSigningEku) {
            return $true
        }
    }

    return $false
}

function Get-PfxCertificate {
    param([string]$Path)

    $securePassword = ConvertTo-SecureString -String $CertPassword -AsPlainText -Force
    return New-Object System.Security.Cryptography.X509Certificates.X509Certificate2(
        $Path,
        $securePassword,
        [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::Exportable)
}

function Remove-QuickShellDevCertificates {
    Get-ChildItem Cert:\CurrentUser\My -ErrorAction SilentlyContinue |
        Where-Object { $_.Subject -like '*QuickShell*' } |
        ForEach-Object {
            Write-Host "Removing old certificate: $($_.Subject) ($($_.Thumbprint))"
            Remove-Item -Path $_.PSPath -Force
        }
}

function New-DevCertificate {
    Write-Host "Creating dev certificate ($CertSubject)..."
    $securePassword = ConvertTo-SecureString -String $CertPassword -AsPlainText -Force

    $cert = New-SelfSignedCertificate `
        -Type CodeSigningCert `
        -Subject $CertSubject `
        -KeyAlgorithm RSA `
        -KeyLength 2048 `
        -KeyExportPolicy Exportable `
        -Provider 'Microsoft Enhanced RSA and AES Cryptographic Provider' `
        -KeyUsage DigitalSignature `
        -FriendlyName 'QuickShell Dev' `
        -CertStoreLocation 'Cert:\CurrentUser\My' `
        -TextExtension @("2.5.29.37={text}$CodeSigningEku")

    Export-PfxCertificate -Cert $cert -FilePath $PfxPath -Password $securePassword | Out-Null
    Export-Certificate -Cert $cert -FilePath $CerPath | Out-Null
    Write-Host "Created $PfxPath"

    return $cert.Thumbprint
}

function Ensure-DevCertificate {
    if ($RecreateCertificate) {
        if (Test-Path $PfxPath) { Remove-Item $PfxPath -Force }
        if (Test-Path $CerPath) { Remove-Item $CerPath -Force }
        Remove-QuickShellDevCertificates
        return New-DevCertificate
    }

    if (Test-Path $PfxPath) {
        try {
            $cert = Get-PfxCertificate -Path $PfxPath
            if (Test-CertificateCanSign -Certificate $cert) {
                Import-PfxCertificate `
                    -FilePath $PfxPath `
                    -CertStoreLocation 'Cert:\CurrentUser\My' `
                    -Password (ConvertTo-SecureString -String $CertPassword -AsPlainText -Force) `
                    -Exportable | Out-Null

                if (-not (Test-Path $CerPath)) {
                    Export-Certificate -Cert "Cert:\CurrentUser\My\$($cert.Thumbprint)" -FilePath $CerPath | Out-Null
                }

                Write-Host "Using existing signing certificate ($($cert.Thumbprint))."
                return $cert.Thumbprint
            }

            Write-Host "Existing PFX is missing code-signing EKU. Recreating certificate..."
        }
        catch {
            Write-Host "Existing PFX is invalid ($($_.Exception.Message)). Recreating certificate..."
        }

        Remove-Item $PfxPath -Force -ErrorAction SilentlyContinue
        Remove-Item $CerPath -Force -ErrorAction SilentlyContinue
    }

    return New-DevCertificate
}

function Install-DevCertificateTrust {
    if (-not (Test-Path $CerPath)) {
        throw "Missing certificate file: $CerPath"
    }

    if (Test-IsAdmin) {
        Write-Host 'Trusting dev certificate in LocalMachine\TrustedPeople...'
        Import-Certificate -FilePath $CerPath -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople' | Out-Null
        return
    }

    Write-Host 'Trusting dev certificate in CurrentUser\TrustedPeople (no admin)...'
    Import-Certificate -FilePath $CerPath -CertStoreLocation 'Cert:\CurrentUser\TrustedPeople' | Out-Null
    Write-Warning 'Not running as administrator. If install fails, rerun deploy without -SkipElevation.'
}

function Get-MsBuildPath {
    $msbuild = Join-Path ${env:ProgramFiles} 'Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe'
    if (-not (Test-Path $msbuild)) {
        $msbuild = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
    }

    if (-not $msbuild -or -not (Test-Path $msbuild)) {
        throw 'MSBuild not found. Install Visual Studio with Desktop development to build QuickShell against the local CmdPal SDK.'
    }

    return $msbuild
}

function Get-PackageFolder {
    param([string]$Root)

    $folders = Get-ChildItem -Path (Join-Path $Root 'AppPackages') -Directory -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending

    if (-not $folders) {
        throw 'No AppPackages output found. Build may have failed.'
    }

    return $folders[0].FullName
}

if (-not $SkipElevation -and -not (Test-IsAdmin)) {
    Write-Host 'Re-launching deploy script as administrator to trust the dev certificate...'
    $argList = @(
        '-NoProfile',
        '-ExecutionPolicy', 'Bypass',
        '-File', $PSCommandPath,
        '-Configuration', $Configuration,
        '-SkipElevation'
    )
    if ($RecreateCertificate) {
        $argList += '-RecreateCertificate'
    }
    Start-Process -FilePath 'powershell.exe' -Verb RunAs -ArgumentList $argList -Wait
    exit $LASTEXITCODE
}

Push-Location $ProjectRoot
try {
    Write-Host 'Generating MSIX logo assets...'
    & (Join-Path $PSScriptRoot 'generate-assets.ps1')

    $thumbprint = Ensure-DevCertificate
    Install-DevCertificateTrust

    $localToolkit = Join-Path (Split-Path $ProjectRoot -Parent) 'PowerToys\src\modules\cmdpal\extensionsdk\Microsoft.CommandPalette.Extensions.Toolkit\Microsoft.CommandPalette.Extensions.Toolkit.csproj'
    if (Test-Path $localToolkit) {
        Write-Host 'Building local Command Palette SDK (hover APIs)...'
        $msbuild = Get-MsBuildPath
        & $msbuild $localToolkit /p:Configuration=$Configuration /p:Platform=x64 /t:Build /v:minimal | Out-Host
        if ($LASTEXITCODE -ne 0) {
            throw "Local CmdPal SDK build failed with exit code $LASTEXITCODE"
        }
    }
    else {
        Write-Warning @"
Local PowerToys SDK not found at $localToolkit.
QuickShell will use NuGet Microsoft.CommandPalette.Extensions, which does not include hover action APIs.
Clone/build PowerToys at A:\PowerToys or set UseLocalCmdPalSdk=false only for legacy builds.
"@
    }

    Write-Host "Building signed MSIX ($Configuration|x64)..."
    $msbuild = Get-MsBuildPath
    & $msbuild (Join-Path $ProjectDir 'QuickShell.csproj') `
        /p:Configuration=$Configuration `
        /p:Platform=x64 `
        /p:PackageCertificateThumbprint=$thumbprint `
        /p:PackageCertificateKeyFile= `
        /p:PackageCertificatePassword= `
        /p:AppxPackageSigningEnabled=true `
        /t:Build `
        /v:minimal | Out-Host

    if ($LASTEXITCODE -ne 0) {
        throw "Build failed with exit code $LASTEXITCODE"
    }

    $packageFolder = Get-PackageFolder -Root $ProjectDir
    $msix = Get-ChildItem -Path $packageFolder -Filter '*.msix' | Select-Object -First 1
    if (-not $msix) {
        throw "No .msix file found in $packageFolder"
    }

    Write-Host "Installing $($msix.FullName)..."
    $installed = Get-AppxPackage -Name 'QuickShell' -ErrorAction SilentlyContinue
    if ($installed) {
        Write-Host 'Removing previous QuickShell install...'
        Remove-AppxPackage -Package $installed.PackageFullName
    }

    try {
        Add-AppxPackage -Path $msix.FullName -ForceApplicationShutdown
    }
    catch {
        throw @"
Package install failed because the dev certificate is not trusted.

Run deploy as administrator (omit -SkipElevation) so the cert is trusted machine-wide:
  powershell -NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`" -RecreateCertificate

Original error: $($_.Exception.Message)
"@
    }

    Write-Host ''
    Write-Host 'Quick Shell installed.'
    Write-Host "Next: open Command Palette and run 'Reload Command Palette Extension', then search 'Quick Shell'."
}
finally {
    Pop-Location
}
