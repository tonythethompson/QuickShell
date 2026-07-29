# wingetcreate update does not reliably preserve ReleaseDate from the prior
# published installer manifest. Metadata-consistency bots then fail the PR.
# Patch ReleaseDate into the installer YAML after update and before submit.
param(
    [Parameter(Mandatory)]
    [string]$ManifestDir,

    [Parameter(Mandatory)]
    [string]$ReleaseDate
)

$ErrorActionPreference = 'Stop'

if ($ReleaseDate -notmatch '^\d{4}-\d{2}-\d{2}$') {
    throw "ReleaseDate '$ReleaseDate' must be YYYY-MM-DD."
}

$installerFile = Get-ChildItem -Path $ManifestDir -Recurse -Filter '*.installer.yaml' | Select-Object -First 1
if (-not $installerFile) {
    throw "No *.installer.yaml found under $ManifestDir"
}

$content = Get-Content -Path $installerFile.FullName -Raw
if ($content -match '(?m)^ReleaseDate:\s*\S+') {
    $content = $content -replace '(?m)^ReleaseDate:\s*\S+', "ReleaseDate: $ReleaseDate"
} elseif ($content -match '(?m)^ManifestType:') {
    $content = $content -replace '(?m)^(ManifestType:)', "ReleaseDate: $ReleaseDate`r`n`$1"
} else {
    $content = $content.TrimEnd() + "`r`nReleaseDate: $ReleaseDate`r`n"
}

Set-Content -Path $installerFile.FullName -Value $content -NoNewline
Write-Host "Set ReleaseDate $ReleaseDate in $($installerFile.FullName)"
