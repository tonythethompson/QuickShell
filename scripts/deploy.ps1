#Requires -Version 5.1
<#
.SYNOPSIS
    Build, sign, install the Quick Shell dev MSIX, and restart Command Palette.

.DESCRIPTION
    Dev deploy loop:
      1. Stop Command Palette / PowerToys (unless -NoRestartCmdPal)
      2. Build and install the signed MSIX
      3. Start Command Palette again

    Elevation runs only when the dev signing certificate is not yet trusted.
    After the first successful install, a normal terminal is enough — no UAC
    relaunch and no hidden second window.

.PARAMETER SkipElevation
    Never relaunch as administrator. Trusts the cert in CurrentUser\TrustedPeople
    when needed. Use this if you prefer to avoid UAC entirely.

.PARAMETER NoRestartCmdPal
    Do not stop or start Command Palette (build/install only).

.PARAMETER UseDevCmdPal
    After install, start a local PowerToys dev CmdPal build (sibling PowerToys checkout).
    Default is retail PowerToys.

.EXAMPLE
    .\scripts\deploy.ps1

.EXAMPLE
    .\scripts\deploy.ps1 -SkipElevation

.EXAMPLE
    .\scripts\run-cmdpal-dev.ps1
#>
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [switch]$SkipElevation,
    [switch]$RecreateCertificate,
    [switch]$UseLocalCmdPalSdk,
    [switch]$NoRestartCmdPal,
    [switch]$UseDevCmdPal
)

$ErrorActionPreference = 'Stop'
$ProjectRoot = Split-Path -Parent $PSScriptRoot
$ProjectDir = Join-Path $ProjectRoot 'QuickShell'
$PfxPath = Join-Path $ProjectDir 'QuickShell_Dev.pfx'
$CerPath = Join-Path $ProjectDir 'QuickShell_Dev.cer'
$CertSubject = 'CN=QuickShell Dev'
$CertPassword = 'QuickShell'
$CodeSigningEku = '1.3.6.1.5.5.7.3.3'

. (Join-Path $PSScriptRoot 'CmdPalLifecycle.ps1')

function Test-IsAdmin {
    $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Test-DevCertificateTrusted {
    param([string]$Thumbprint)

    foreach ($store in @('Cert:\LocalMachine\TrustedPeople', 'Cert:\CurrentUser\TrustedPeople')) {
        $trusted = Get-ChildItem $store -ErrorAction SilentlyContinue |
            Where-Object { $_.Thumbprint -eq $Thumbprint } |
            Select-Object -First 1

        if ($trusted) {
            return $true
        }
    }

    return $false
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
    if (Test-DevCertificateTrusted -Thumbprint $Certificate.Thumbprint) {
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
    param([string]$Thumbprint)

    if (-not (Test-Path $CerPath)) {
        throw "Missing certificate file: $CerPath"
    }

    if (Test-DevCertificateTrusted -Thumbprint $Thumbprint) {
        Write-Host 'Dev certificate already trusted; skipping import.'
        return
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
    $msbuild = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1

    if (-not $msbuild -or -not (Test-Path $msbuild)) {
        throw 'MSBuild not found. Install Visual Studio with the Desktop development workload (or .NET desktop development).'
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

function Test-NeedsAdminElevation {
    if ($SkipElevation -or (Test-IsAdmin)) {
        return $false
    }

    if ($RecreateCertificate) {
        return $true
    }

    if (-not (Test-Path $PfxPath)) {
        return $true
    }

    try {
        $cert = Get-PfxCertificate -Path $PfxPath
        return -not (Test-DevCertificateTrusted -Thumbprint $cert.Thumbprint)
    }
    catch {
        return $true
    }
}

function Get-DeployArgumentList {
    $argList = @(
        '-NoProfile',
        '-ExecutionPolicy', 'Bypass',
        '-File', $PSCommandPath,
        '-Configuration', $Configuration,
        '-SkipElevation'
    )

    if ($RecreateCertificate) { $argList += '-RecreateCertificate' }
    if ($UseLocalCmdPalSdk) { $argList += '-UseLocalCmdPalSdk' }
    if ($NoRestartCmdPal) { $argList += '-NoRestartCmdPal' }
    if ($UseDevCmdPal) { $argList += '-UseDevCmdPal' }

    return $argList
}

if (Test-NeedsAdminElevation) {
    Write-Host 'Administrator approval required once to trust the dev signing certificate.'
    Write-Host 'Approve the UAC prompt. Build and install run in the elevated window that opens.' -ForegroundColor Yellow
    Start-Process -FilePath 'powershell.exe' -Verb RunAs -ArgumentList (Get-DeployArgumentList) -Wait
    exit $LASTEXITCODE
}

Push-Location $ProjectRoot
try {
    if (-not $NoRestartCmdPal) {
        Write-Host 'Stopping Command Palette before build/install...'
        Stop-CmdPalProcesses
    }

    Write-Host 'Generating MSIX logo assets...'
    & (Join-Path $PSScriptRoot 'generate-assets.ps1')

    $thumbprint = Ensure-DevCertificate
    Install-DevCertificateTrust -Thumbprint $thumbprint

    $msbuild = Get-MsBuildPath
    $powerToysRoot = Get-CmdPalPowerToysRoot -ProjectRoot $ProjectRoot
    $localToolkit = Join-Path $powerToysRoot 'src\modules\cmdpal\extensionsdk\Microsoft.CommandPalette.Extensions.Toolkit\Microsoft.CommandPalette.Extensions.Toolkit.csproj'
    $useLocalSdk = $UseLocalCmdPalSdk.IsPresent
    if ($useLocalSdk) {
        if (-not (Test-Path $localToolkit)) {
            throw "UseLocalCmdPalSdk requires a PowerToys checkout at $powerToysRoot."
        }

        Write-Host 'Building local Command Palette SDK...'
        & $msbuild $localToolkit /p:Configuration=$Configuration /p:Platform=x64 /t:Build /v:minimal | Out-Host
        if ($LASTEXITCODE -ne 0) {
            throw "Local CmdPal SDK build failed with exit code $LASTEXITCODE"
        }
    }
    elseif (Test-Path $localToolkit) {
        Write-Host 'Skipping local PowerToys SDK (NuGet CmdPal SDK). Pass -UseLocalCmdPalSdk to build against local PowerToys.' -ForegroundColor DarkGray
    }
    else {
        Write-Warning @"
Local PowerToys SDK not found at $localToolkit.
QuickShell will use NuGet Microsoft.CommandPalette.Extensions.
"@
    }

    $useLocalSdkMsbuild = if ($useLocalSdk) { 'true' } else { 'false' }

    Write-Host "Building signed MSIX ($Configuration|x64)..."
    & $msbuild (Join-Path $ProjectDir 'QuickShell.csproj') `
        /p:Configuration=$Configuration `
        /p:Platform=x64 `
        "/p:UseLocalCmdPalSdk=$useLocalSdkMsbuild" `
        /p:GenerateAppxPackageOnBuild=true `
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
    $installed = @(
        Get-AppxPackage -Name 'tonythethompson.536944BA0D095' -ErrorAction SilentlyContinue
        Get-AppxPackage -Name 'QuickShell' -ErrorAction SilentlyContinue
    )
    foreach ($package in $installed) {
        Write-Host "Removing previous install $($package.PackageFullName)..."
        Remove-AppxPackage -Package $package.PackageFullName
    }

    try {
        Add-AppxPackage -Path $msix.FullName
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
    Write-Host 'Quick Shell installed.' -ForegroundColor Green

    if (-not $NoRestartCmdPal) {
        Write-Host 'Starting Command Palette...'
        Start-CommandPalette -ProjectRoot $ProjectRoot -Configuration $Configuration -UseDevCmdPal:$UseDevCmdPal
    }

    Write-Host ''
    Write-Host "Next: open Command Palette and run 'Reload Command Palette Extension', then search 'Quick Shell'."
}
finally {
    Pop-Location
}
