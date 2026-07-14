#Requires -Version 5.1
<#
.SYNOPSIS
    Shorthand entry point for deploy-all.ps1 (all QuickShell dev surfaces).

.DESCRIPTION
    Forwards to scripts/deploy-all.ps1 with the same parameters. Use from the repo
    root or from QuickShell workspace shortcuts (Abbreviation: ddeploy).

.EXAMPLE
    .\scripts\ddeploy.ps1

.EXAMPLE
    .\scripts\ddeploy.ps1 -SkipRaycast -SkipElevation
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [switch]$SkipCmdPal,
    [switch]$SkipRun,
    [switch]$SkipRaycast,
    [switch]$SkipTests,
    [switch]$RaycastBuildOnly,
    [switch]$NoRestart,
    [switch]$UseLocalCmdPalSdk,
    [switch]$UseDevCmdPal,
    [switch]$SkipElevation,
    [switch]$RecreateCertificate
)

$deployAll = Join-Path $PSScriptRoot 'deploy-all.ps1'
& $deployAll @PSBoundParameters
exit $LASTEXITCODE
