# Verifies that a release actually produced the exact artifacts users will
# download, with metadata matching the version being released. Run this in
# CI after building installers/plugin zips and before creating the GitHub
# Release / submitting the WinGet manifest update — catching a missing or
# mismatched artifact here is much cheaper than catching it after users hit
# a 404 or install a version-mismatched build.
param(
    [Parameter(Mandatory)]
    [string]$Version,

    [string]$InstallerDirectory = "QuickShell/bin/Release/installer",

    [string]$RunPluginX64Zip = "QuickShell.Run/bin/x64/Release/QuickShell.Run-x64.zip",

    [string]$RunPluginArm64Zip = "QuickShell.Run/bin/ARM64/Release/QuickShell.Run-ARM64.zip",

    # Below this, an installer is almost certainly truncated/empty rather
    # than a real self-contained .NET publish output.
    [long]$MinimumInstallerBytes = 500KB
)

$ErrorActionPreference = "Stop"
$failures = @()

function Test-InstallerArtifact(
    [string]$Platform,
    [string]$InstallerBaseName) {
    $path = Join-Path $InstallerDirectory "$InstallerBaseName-Setup-$Version-$Platform.exe"

    if (-not (Test-Path $path)) {
        $script:failures += "Missing installer for ${Platform} ($InstallerBaseName): $path"
        return
    }

    $file = Get-Item $path
    if ($file.Length -lt $MinimumInstallerBytes) {
        $script:failures += "Installer for $Platform ($InstallerBaseName) is suspiciously small ($($file.Length) bytes, expected at least $MinimumInstallerBytes): $path"
    }

    # Inno Setup stamps the compiled installer's own file version from
    # AppVersion (setup-template.iss does not override VersionInfoVersion),
    # so this should always equal the release version being cut.
    $fileVersion = $file.VersionInfo.FileVersion
    if ([string]::IsNullOrWhiteSpace($fileVersion)) {
        $script:failures += "Installer for $Platform ($InstallerBaseName) has no embedded file version metadata: $path"
    }
    elseif ($fileVersion -ne $Version) {
        $script:failures += "Installer for $Platform ($InstallerBaseName) has file version '$fileVersion', expected '$Version': $path"
    }
}

function Test-RunPluginZip([string]$Path, [string]$Label) {
    if (-not (Test-Path $Path)) {
        $script:failures += "Missing PowerToys Run plugin zip ($Label): $Path"
        return
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction SilentlyContinue
    $zip = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path $Path))
    try {
        $entryNames = $zip.Entries | ForEach-Object { $_.Name }
        foreach ($required in @('QuickShell.Run.dll', 'QuickShell.Core.dll')) {
            if ($entryNames -notcontains $required) {
                $script:failures += "$Label plugin zip is missing '$required': $Path"
            }
        }
    }
    finally {
        $zip.Dispose()
    }
}

Write-Host "Verifying release artifacts for version $Version..." -ForegroundColor Cyan

foreach ($installerBaseName in @("QuickShell", "QuickShellforCmdPal")) {
    Test-InstallerArtifact -Platform "x64" -InstallerBaseName $installerBaseName
    Test-InstallerArtifact -Platform "arm64" -InstallerBaseName $installerBaseName
}

Test-RunPluginZip -Path $RunPluginX64Zip -Label "x64"
Test-RunPluginZip -Path $RunPluginArm64Zip -Label "ARM64"

if ($failures.Count -gt 0) {
    Write-Host "`nRelease artifact verification FAILED:" -ForegroundColor Red
    foreach ($failure in $failures) {
        Write-Host " - $failure" -ForegroundColor Red
    }

    throw "Release artifact verification failed with $($failures.Count) issue(s). See above."
}

Write-Host "Release artifact verification passed: bundled + CmdPal installers, plugin zips present, correctly named, and version-stamped." -ForegroundColor Green
