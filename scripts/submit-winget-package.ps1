#Requires -Version 5.1
<#
.SYNOPSIS
    Update and submit one WinGet package from GitHub Release installer URLs.

.DESCRIPTION
    Runs wingetcreate update/submit for a package that already exists in
    microsoft/winget-pkgs. If the package has never been published, wingetcreate
    cannot update it; this script warns and exits 0 so optional packages
    (CmdPal-only / Run-only) do not fail the whole release job.

.PARAMETER PackageId
    WinGet package identifier (e.g. tonythethompson.QuickShell).

.PARAMETER Version
    Package version (e.g. 0.2.4.0).

.PARAMETER Tag
    GitHub release tag (e.g. v0.2.4.0).

.PARAMETER X64Url
    x64 installer download URL.

.PARAMETER Arm64Url
    ARM64 installer download URL.

.PARAMETER ReleaseNotes
    Release notes text for the locale manifest.

.PARAMETER ReleaseDate
    Optional installer ReleaseDate (YYYY-MM-DD). Defaults to today (UTC).

.PARAMETER Required
    If set, missing packages and submit failures fail the step. Default: soft-skip
    when the package is not yet in winget-pkgs.
#>
param(
    [Parameter(Mandatory)]
    [string]$PackageId,

    [Parameter(Mandatory)]
    [string]$Version,

    [Parameter(Mandatory)]
    [string]$Tag,

    [Parameter(Mandatory)]
    [string]$X64Url,

    [Parameter(Mandatory)]
    [string]$Arm64Url,

    [Parameter(Mandatory)]
    [string]$ReleaseNotes,

    [string]$ReleaseDate = '',

    [switch]$Required
)

$ErrorActionPreference = 'Stop'

if (-not $env:WINGET_PAT) {
    if ($Required) {
        throw 'WINGET_PAT is not configured.'
    }
    Write-Warning "WINGET_PAT is not configured. Skipping $PackageId."
    exit 0
}

$env:Path = [System.Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + `
    [System.Environment]::GetEnvironmentVariable('Path', 'User')

if (-not (Get-Command wingetcreate -ErrorAction SilentlyContinue)) {
    throw 'wingetcreate was not found on PATH.'
}

$repo = if ($env:GITHUB_REPOSITORY) { $env:GITHUB_REPOSITORY } else { 'tonythethompson/QuickShell' }
$releaseNotesUrl = "https://github.com/$repo/releases/tag/$Tag"
$outDir = Join-Path 'winget-out' $PackageId

Write-Host "Updating $PackageId $Version..."
$updateOutput = & wingetcreate update $PackageId `
    --version $Version `
    --urls $X64Url $Arm64Url `
    --release-notes-url $releaseNotesUrl `
    --out $outDir `
    2>&1 | Out-String
Write-Host $updateOutput

if ($LASTEXITCODE -ne 0) {
    $missing = ($updateOutput -match 'was not found') -or
        ($updateOutput -match 'No manifests found') -or
        ($updateOutput -match 'does not exist')
    if ($missing -and -not $Required) {
        Write-Warning @"
$PackageId is not in microsoft/winget-pkgs yet, so wingetcreate cannot update it.
Create the initial package once (wingetcreate new / manual PR), then CI can submit version bumps.
Skipping $PackageId for this release.
"@
        exit 0
    }
    throw "wingetcreate update failed for $PackageId (exit $LASTEXITCODE)."
}

# wingetcreate 1.12 writes under out/manifests/.../<version>; submit that folder.
$manifestDir = Get-ChildItem -Path $outDir -Recurse -Filter '*.installer.yaml' -ErrorAction SilentlyContinue |
    Select-Object -First 1 |
    ForEach-Object { $_.Directory.FullName }
if (-not $manifestDir) {
    throw "No installer manifest found under $outDir after wingetcreate update."
}

& (Join-Path $PSScriptRoot 'set-winget-release-notes.ps1') -ManifestDir $manifestDir -ReleaseNotes $ReleaseNotes
if (-not $?) {
    throw "set-winget-release-notes.ps1 failed for $PackageId."
}

if ([string]::IsNullOrWhiteSpace($ReleaseDate)) {
    $ReleaseDate = [DateTime]::UtcNow.ToString('yyyy-MM-dd')
}
& (Join-Path $PSScriptRoot 'set-winget-release-date.ps1') -ManifestDir $manifestDir -ReleaseDate $ReleaseDate
if (-not $?) {
    throw "set-winget-release-date.ps1 failed for $PackageId."
}

Write-Host "Submitting $PackageId from $manifestDir..."
& wingetcreate submit `
    --prtitle "$PackageId version $Version" `
    --token $env:WINGET_PAT `
    $manifestDir
if ($LASTEXITCODE -ne 0) {
    throw "wingetcreate submit failed for $PackageId (exit $LASTEXITCODE)."
}

Write-Host "Submitted $PackageId $Version."
