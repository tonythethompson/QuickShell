#Requires -Version 7.0
<#
.SYNOPSIS
    Patch Partner Center listing ReleaseNotes (Store "What's new") for a draft submission.

.DESCRIPTION
    Fetches the current MSIX submission metadata via msstore, sets only
    listings.en-us.baseListing.releaseNotes, and calls updateMetadata.
    Does not commit/publish the submission; the caller runs
    `msstore submission publish` after this succeeds.

.PARAMETER AppId
    Store product ID (e.g. 9PC8S6LNRT3R).

.PARAMETER ReleaseNotes
    Human-authored release notes text (from CHANGELOG.md).

.PARAMETER Locale
    Listing locale key. Default en-us.

.EXAMPLE
    .\scripts\set-store-release-notes.ps1 -AppId 9PC8S6LNRT3R -ReleaseNotes $notes
#>
param(
    [Parameter(Mandatory)]
    [string]$AppId,

    [Parameter(Mandatory)]
    [string]$ReleaseNotes,

    [string]$Locale = 'en-us'
)

$ErrorActionPreference = 'Stop'

function Get-HashtableEntry {
    param(
        [Parameter(Mandatory)][hashtable]$Table,
        [Parameter(Mandatory)][string[]]$Names
    )

    foreach ($name in $Names) {
        if ($Table.ContainsKey($name)) {
            return @{ Name = $name; Value = $Table[$name] }
        }
    }

    return $null
}

if (-not (Get-Command msstore -ErrorAction SilentlyContinue)) {
    throw 'msstore CLI was not found on PATH. Install Microsoft.Store.CLI first.'
}

$notes = $ReleaseNotes.Trim()
if ([string]::IsNullOrWhiteSpace($notes)) {
    throw 'ReleaseNotes is empty.'
}

# Partner Center "What's new" / ReleaseNotes is capped at 1,500 characters
# (Description is the separate ~10k field). Truncate to the listing limit.
$maxNotesLength = 1500
$truncationSuffix = "`n..."
if ($notes.Length -gt $maxNotesLength) {
    Write-Warning "Release notes exceed $maxNotesLength characters; truncating for Store listing."
    $truncateTo = [Math]::Max(0, $maxNotesLength - $truncationSuffix.Length)
    $notes = $notes.Substring(0, $truncateTo).TrimEnd() + $truncationSuffix
}

Write-Host "Fetching submission metadata for $AppId..."
# Capture stdout only so stderr chatter cannot corrupt JSON parsing.
$raw = & msstore submission get $AppId
if ($LASTEXITCODE -ne 0) {
    throw "msstore submission get failed (exit $LASTEXITCODE): $raw"
}

$jsonText = ($raw | Out-String).Trim()
if ([string]::IsNullOrWhiteSpace($jsonText)) {
    throw 'msstore submission get returned empty output.'
}

# Drop CLI chatter before/after the JSON object if present.
$jsonStart = $jsonText.IndexOf('{')
if ($jsonStart -lt 0) {
    throw "msstore submission get did not return JSON. Output:`n$jsonText"
}
if ($jsonStart -gt 0) {
    $jsonText = $jsonText.Substring($jsonStart)
}

$jsonEnd = $jsonText.LastIndexOf('}')
if ($jsonEnd -lt 0) {
    throw "msstore submission get JSON was missing a closing brace. Output:`n$jsonText"
}
if ($jsonEnd -lt $jsonText.Length - 1) {
    $jsonText = $jsonText.Substring(0, $jsonEnd + 1)
}

$submission = $jsonText | ConvertFrom-Json -AsHashtable
if (-not $submission) {
    throw 'Failed to parse submission JSON.'
}

$listingsEntry = Get-HashtableEntry -Table $submission -Names @('Listings', 'listings')
if (-not $listingsEntry -or $null -eq $listingsEntry.Value) {
    throw 'Submission JSON has no Listings object.'
}

$listings = $listingsEntry.Value
if ($listings -isnot [hashtable]) {
    throw 'Submission Listings is not an object map.'
}

$localeEntry = Get-HashtableEntry -Table $listings -Names @($Locale, $Locale.ToLowerInvariant(), $Locale.ToUpperInvariant())
if (-not $localeEntry) {
    throw "Submission has no listing for locale '$Locale'. Available: $($listings.Keys -join ', ')"
}

$listing = $localeEntry.Value
if ($listing -isnot [hashtable]) {
    throw "Listing '$Locale' is not an object."
}

$baseEntry = Get-HashtableEntry -Table $listing -Names @('BaseListing', 'baseListing')
if (-not $baseEntry -or $null -eq $baseEntry.Value) {
    throw "Listing '$Locale' has no BaseListing."
}

$baseListing = $baseEntry.Value
if ($baseListing -isnot [hashtable]) {
    throw "BaseListing for '$Locale' is not an object."
}

$releaseNotesKey = if ($baseListing.ContainsKey('ReleaseNotes')) {
    'ReleaseNotes'
} elseif ($baseListing.ContainsKey('releaseNotes')) {
    'releaseNotes'
} else {
    'ReleaseNotes'
}

$baseListing[$releaseNotesKey] = $notes
Write-Host "Patched Listings.$Locale.BaseListing.$releaseNotesKey ($($notes.Length) chars)."

$updated = $submission | ConvertTo-Json -Depth 100 -Compress
Write-Host 'Calling msstore submission updateMetadata...'
& msstore submission updateMetadata $AppId $updated
if ($LASTEXITCODE -ne 0) {
    throw "msstore submission updateMetadata failed with exit code $LASTEXITCODE."
}

Write-Host 'Store listing ReleaseNotes updated (submission still uncommitted until publish).'
