# wingetcreate's `update` command has no flag to set the ReleaseNotes text
# field (only --release-notes-url via -o/--out), so an automated
# `wingetcreate update ... --submit` run always drops ReleaseNotes even when
# the previously published manifest had one. This patches the field into the
# locale manifest that `wingetcreate update -o <dir>` generated locally, so it
# can be submitted with `wingetcreate submit <dir>` afterwards.
param(
    [Parameter(Mandatory)]
    [string]$ManifestDir,

    [Parameter(Mandatory)]
    [string]$ReleaseNotes
)

$ErrorActionPreference = "Stop"

$localeFile = Get-ChildItem -Path $ManifestDir -Recurse -Filter "*.locale.en-US.yaml" | Select-Object -First 1
if (-not $localeFile) {
    throw "No *.locale.en-US.yaml manifest found under $ManifestDir"
}

$content = Get-Content -Path $localeFile.FullName -Raw

# Drop any ReleaseNotes block wingetcreate may have carried forward, so we
# don't end up with a duplicate key.
$content = $content -replace '(?ms)^ReleaseNotes:\s*\|.*?(?=^\S)', ''

$notesBlock = "ReleaseNotes: |`n  $ReleaseNotes`n"

if ($content -match '(?m)^ReleaseNotesUrl:') {
    $content = $content -replace '(?m)^(ReleaseNotesUrl:)', "$notesBlock`$1"
} else {
    $content = $content -replace '(?m)^(ManifestType:)', "$notesBlock`$1"
}

Set-Content -Path $localeFile.FullName -Value $content -NoNewline
Write-Host "Set ReleaseNotes in $($localeFile.FullName)"
