#Requires -Version 5.1
<#
.SYNOPSIS
  Opens Windows settings pages useful for Quick Shell accessibility testing.
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts/open-a11y-settings.ps1
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts/open-a11y-settings.ps1 -All
#>
param(
    [switch]$All,
    [switch]$Narrator,
    [switch]$Contrast,
    [switch]$Display,
    [switch]$Magnifier,
    [switch]$Keyboard
)

$ErrorActionPreference = 'Stop'

$pages = [ordered]@{
    Accessibility = 'ms-settings:easeofaccess'
    Narrator      = 'ms-settings:easeofaccess-narrator'
    Contrast      = 'ms-settings:easeofaccess-highcontrast'
    Magnifier     = 'ms-settings:easeofaccess-magnifier'
    Keyboard      = 'ms-settings:easeofaccess-keyboard'
    Display       = 'ms-settings:display'
}

function Open-SettingsPage {
    param([string]$Name, [string]$Uri)
    Write-Host "Opening $Name..."
    Start-Process $Uri
}

$selected = @()
if ($All -or (-not ($Narrator -or $Contrast -or $Display -or $Magnifier -or $Keyboard))) {
    $selected = $pages.Keys
}
else {
    if ($Narrator) { $selected += 'Narrator' }
    if ($Contrast) { $selected += 'Contrast' }
    if ($Display) { $selected += 'Display' }
    if ($Magnifier) { $selected += 'Magnifier' }
    if ($Keyboard) { $selected += 'Keyboard' }
}

foreach ($name in $selected) {
    Open-SettingsPage -Name $name -Uri $pages[$name]
    if ($selected.Count -gt 1) {
        Start-Sleep -Milliseconds 400
    }
}

Write-Host ''
Write-Host 'Quick Shell manual test reminder:'
Write-Host '  1. Reload Command Palette Extension in PowerToys'
Write-Host '  2. Open Quick Shell, arrow to a shortcut, Ctrl+K for More actions'
Write-Host 'Next: run Reload Command Palette Extension and test with Narrator (Win+Ctrl+Enter).'
