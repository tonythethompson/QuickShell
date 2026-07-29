#Requires -Version 5.1
<#
.SYNOPSIS
    Extract the human-authored CHANGELOG.md section for a release version.

.DESCRIPTION
    Finds "## [X.Y.Z.W]" (optional date suffix) in the root CHANGELOG.md and
    prints the section body. Fails if the section is missing or empty so CI
    cannot ship commit dumps as release notes.

.PARAMETER Version
    Package version without a leading v (e.g. 0.2.3.0).

.PARAMETER Path
    Changelog file path. Defaults to repo-root CHANGELOG.md.

.EXAMPLE
    .\scripts\extract-changelog.ps1 -Version 0.2.3.0
#>
param(
    [Parameter(Mandatory)]
    [string]$Version,

    [string]$Path = ''
)

$ErrorActionPreference = 'Stop'

if (-not $Path) {
    $Path = Join-Path (Split-Path -Parent $PSScriptRoot) 'CHANGELOG.md'
}

if (-not (Test-Path -LiteralPath $Path)) {
    throw "CHANGELOG not found: $Path"
}

$normalized = $Version.Trim()
if ($normalized -match '^v') {
    $normalized = $normalized.Substring(1)
}

if ($normalized -notmatch '^\d+(\.\d+){1,3}$') {
    throw "Invalid version '$Version'. Expected a numeric package version such as 0.2.3.0."
}

$content = Get-Content -LiteralPath $Path -Raw
if ([string]::IsNullOrWhiteSpace($content)) {
    throw "CHANGELOG is empty: $Path"
}

# Keep a Changelog: ## [1.2.3] - 2026-07-22  or  ## [1.2.3]
$escaped = [regex]::Escape($normalized)
$headingPattern = "(?m)^## \[$escaped\](?:\s+-\s+[^\r\n]+)?\s*$"
$headingMatch = [regex]::Match($content, $headingPattern)
if (-not $headingMatch.Success) {
    throw "No CHANGELOG section for version $normalized. Add '## [$normalized] - YYYY-MM-DD' with user-facing notes before releasing."
}

$start = $headingMatch.Index + $headingMatch.Length
$rest = $content.Substring($start)
$nextHeading = [regex]::Match($rest, '(?m)^## \[')
$body = if ($nextHeading.Success) {
    $rest.Substring(0, $nextHeading.Index)
} else {
    $rest
}

$body = $body.Trim()
if ([string]::IsNullOrWhiteSpace($body)) {
    throw "CHANGELOG section for $normalized is empty. Add Added/Fixed/Changed bullets before releasing."
}

# Reject placeholder-only stubs.
if ($body -match '(?i)^\s*(tbd|todo|coming soon)\s*$') {
    throw "CHANGELOG section for $normalized looks like a placeholder. Write user-facing release notes."
}

Write-Output $body
