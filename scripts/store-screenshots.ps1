#Requires -Version 5.1
<#
.SYNOPSIS
    Capture and prepare Microsoft Store desktop screenshots for Quick Shell.

.DESCRIPTION
    1. Capture  (-Mode Capture)  -  primary-monitor PNGs (Prepare trims/letterboxes).
    2. Prepare  (-Mode Prepare)  -  Store-safe 16:9 sizes + optional README assets.

.PARAMETER Mode
    Capture | Prepare | Measure | InstallDemo | RestoreShortcuts | Help

.PARAMETER CaptureTrigger
    Hotkey (default)  -  F9 capture, F8 skip, F7 measure, F12 quit; CmdPal can stay focused.
    Countdown  -  auto-capture after -CountdownSeconds (no terminal focus needed).
    Terminal  -  legacy Read-Host prompts in this window.

.PARAMETER CountdownSeconds
    Seconds before each auto-capture when -CaptureTrigger Countdown.
#>
[CmdletBinding()]
param(
    [ValidateSet('Capture', 'Prepare', 'Measure', 'InstallDemo', 'RestoreShortcuts', 'Help')]
    [string]$Mode = 'Help',

    [string]$RawDir,
    [string]$PreparedDir,
    [switch]$SkipAssetsCopy,
    [switch]$UseDemoShortcuts = $true,
    [switch]$KeepDemoShortcuts,

    [ValidateSet('Hotkey', 'Countdown', 'Terminal')]
    [string]$CaptureTrigger = 'Hotkey',

    [int]$CountdownSeconds = 5
)

$ErrorActionPreference = 'Stop'

$ProjectRoot = Split-Path -Parent $PSScriptRoot
$AssetsDir = Join-Path $ProjectRoot 'QuickShell\Assets'
$DefaultRawDir = Join-Path $AssetsDir 'StoreScreenshots\raw'
$DefaultPreparedDir = Join-Path $AssetsDir 'StoreScreenshots\prepared'
$DemoShortcutsPath = Join-Path $PSScriptRoot 'store-screenshot-shortcuts.json'
$ShortcutsBackupPath = Join-Path $AssetsDir 'StoreScreenshots\shortcuts-backup.json'
$LiveShortcutsPath = Join-Path $env:LOCALAPPDATA 'QuickShell\shortcuts.json'

$StoreSizes = @(
    @{ Width = 1366; Height = 768; Label = '1366x768' }
    @{ Width = 1920; Height = 1080; Label = '1920x1080' }
    @{ Width = 3840; Height = 2160; Label = '3840x2160' }
)

$BackgroundColorArgb = [System.Drawing.Color]::FromArgb(255, 28, 28, 28)

$ShotGuide = @(
    @{
        Id          = '01-list-context-menu'
        ReadmeAsset = 'Screenshot_1.png'
        Prompt      = 'Shortcut list + context menu open (My App selected, Ctrl+K).'
        ManualHint  = 'Quick Shell -> My App -> Ctrl+K'
    },
    @{
        Id          = '02-edit-shortcut'
        ReadmeAsset = 'Screenshot_2.png'
        Prompt      = 'Edit shortcut form (e.g. My App Admin).'
        ManualHint  = 'Esc -> My App Admin -> Ctrl+E'
    },
    @{
        Id          = '03-settings'
        ReadmeAsset = 'Screenshot_3.png'
        Prompt      = 'Quick Shell settings page.'
        ManualHint  = 'Esc -> search quick shell settings -> Enter'
    }
)

function Initialize-DrawingAssemblies {
    if (-not ('System.Drawing.Bitmap' -as [type])) {
        Add-Type -AssemblyName System.Drawing
    }

    if (-not ('System.Windows.Forms.Screen' -as [type])) {
        Add-Type -AssemblyName System.Windows.Forms
    }
}

function Get-QuickShellConfigDirectory {
    Join-Path $env:LOCALAPPDATA 'QuickShell'
}

function New-DirectoryIfMissing {
    param([string]$Path)

    if (-not (Test-Path $Path)) {
        New-Item -ItemType Directory -Force -Path $Path | Out-Null
    }
}

function Resolve-PathOrDefault {
    param(
        [string]$Path,
        [string]$Default
    )

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return $Default
    }

    return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
}

function Backup-LiveShortcuts {
    New-DirectoryIfMissing (Split-Path -Parent $ShortcutsBackupPath)
    New-DirectoryIfMissing (Get-QuickShellConfigDirectory)

    if (Test-Path $LiveShortcutsPath) {
        Copy-Item $LiveShortcutsPath $ShortcutsBackupPath -Force
        Write-Host "Backed up shortcuts -> $ShortcutsBackupPath" -ForegroundColor DarkGray
        return
    }

    '[]' | Set-Content -Path $ShortcutsBackupPath -Encoding UTF8
    Write-Host 'No live shortcuts.json yet; backup saved as empty list.' -ForegroundColor DarkGray
}

function Install-DemoShortcuts {
    if (-not (Test-Path $DemoShortcutsPath)) {
        throw "Demo preset not found: $DemoShortcutsPath"
    }

    New-DirectoryIfMissing (Get-QuickShellConfigDirectory)
    Copy-Item $DemoShortcutsPath $LiveShortcutsPath -Force
    Write-Host "Installed Store demo shortcuts -> $LiveShortcutsPath" -ForegroundColor Green
    Write-Host 'Run Reload Command Palette Extension in CmdPal.' -ForegroundColor Yellow
}

function Restore-LiveShortcuts {
    if (-not (Test-Path $ShortcutsBackupPath)) {
        throw "No backup found at $ShortcutsBackupPath."
    }

    New-DirectoryIfMissing (Get-QuickShellConfigDirectory)
    Copy-Item $ShortcutsBackupPath $LiveShortcutsPath -Force
    Write-Host "Restored your shortcuts -> $LiveShortcutsPath" -ForegroundColor Green
    Write-Host 'Run Reload Command Palette Extension again.' -ForegroundColor Yellow
}

function Ensure-CaptureHotKeyTypes {
    if ('StoreScreenshotHotKeyForm' -as [type]) {
        return
    }

    Initialize-DrawingAssemblies

    Add-Type -ReferencedAssemblies System.Drawing, System.Windows.Forms -TypeDefinition @'
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

public sealed class StoreScreenshotHotKeyForm : Form
{
    public const int WmHotKey = 0x0312;
    public const int IdCapture = 1;
    public const int IdSkip = 2;
    public const int IdQuit = 3;
    public const int IdMeasure = 4;

    private const uint ModNoRepeat = 0x4000;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public int LastHotKeyId { get; private set; }

    public StoreScreenshotHotKeyForm()
    {
        ShowInTaskbar = false;
        FormBorderStyle = FormBorderStyle.None;
        Opacity = 0;
        Width = 1;
        Height = 1;
        StartPosition = FormStartPosition.Manual;
        Location = new Point(-32000, -32000);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        RegisterHotKey(Handle, IdCapture, ModNoRepeat, (uint)Keys.F9);
        RegisterHotKey(Handle, IdSkip, ModNoRepeat, (uint)Keys.F8);
        RegisterHotKey(Handle, IdQuit, ModNoRepeat, (uint)Keys.F12);
        RegisterHotKey(Handle, IdMeasure, ModNoRepeat, (uint)Keys.F7);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        UnregisterHotKey(Handle, IdCapture);
        UnregisterHotKey(Handle, IdSkip);
        UnregisterHotKey(Handle, IdQuit);
        UnregisterHotKey(Handle, IdMeasure);
        base.OnFormClosed(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmHotKey)
        {
            LastHotKeyId = m.WParam.ToInt32();
            Close();
            return;
        }

        base.WndProc(ref m);
    }
}
'@
}

function Write-CaptureHotKeyLegend {
    Write-Host '  F9  = capture / continue     F8 = skip     F7 = measure monitor size' -ForegroundColor DarkGray
    Write-Host '  F12 = quit session           (CmdPal can stay focused  -  no need to click this terminal)' -ForegroundColor DarkGray
}

function Wait-CaptureHotKey {
    param([string]$Prompt)

    Ensure-CaptureHotKeyTypes

    Write-Host ''
    Write-Host $Prompt -ForegroundColor Cyan
    Write-CaptureHotKeyLegend

    $form = New-Object StoreScreenshotHotKeyForm
    [void]$form.ShowDialog()
    $form.Dispose()

    switch ($form.LastHotKeyId) {
        ([StoreScreenshotHotKeyForm]::IdCapture) { return 'Capture' }
        ([StoreScreenshotHotKeyForm]::IdSkip) { return 'Skip' }
        ([StoreScreenshotHotKeyForm]::IdMeasure) { return 'Measure' }
        default { return 'Quit' }
    }
}

function Wait-CaptureCountdown {
    param(
        [int]$Seconds,
        [string]$Prompt
    )

    Write-Host ''
    Write-Host $Prompt -ForegroundColor Cyan
    Write-Host "Auto-capture in $Seconds seconds  -  focus CmdPal on the primary monitor now." -ForegroundColor Yellow

    for ($remaining = $Seconds; $remaining -ge 1; $remaining--) {
        Write-Host "  $remaining..." -ForegroundColor DarkGray
        Start-Sleep -Seconds 1
    }

    return 'Capture'
}

function Wait-CaptureTrigger {
    param(
        [string]$Prompt,
        [ValidateSet('Hotkey', 'Countdown', 'Terminal')]
        [string]$Trigger,
        [int]$CountdownSeconds
    )

    switch ($Trigger) {
        'Hotkey' {
            while ($true) {
                $action = Wait-CaptureHotKey -Prompt $Prompt
                if ($action -eq 'Measure') {
                    $bounds = Get-PrimaryScreenBounds
                    Write-CaptureSizeHint -Width $bounds.Width -Height $bounds.Height
                    Write-Host ''
                    continue
                }

                return $action
            }
        }
        'Countdown' {
            return Wait-CaptureCountdown -Seconds $CountdownSeconds -Prompt $Prompt
        }
        default {
            Write-Host ''
            Write-Host $Prompt -ForegroundColor Cyan
            $response = Read-Host 'Enter=capture/continue, S=skip, Q=quit'
            if ($response -match '^[Qq]') { return 'Quit' }
            if ($response -match '^[Ss]') { return 'Skip' }
            return 'Capture'
        }
    }
}

function Wait-ForCmdPalReload {
    param(
        [ValidateSet('Hotkey', 'Countdown', 'Terminal')]
        [string]$Trigger,
        [int]$CountdownSeconds = 8
    )

    if ($Trigger -eq 'Terminal') {
        Write-Host ''
        Read-Host 'After Reload Command Palette Extension, press Enter to continue'
        return
    }

    if ($Trigger -eq 'Countdown') {
        [void](Wait-CaptureCountdown `
            -Seconds $CountdownSeconds `
            -Prompt 'Reload Command Palette Extension in CmdPal now.')
        return
    }

    [void](Wait-CaptureTrigger `
        -Prompt 'Reload Command Palette Extension in CmdPal, then press F9 when demo shortcuts appear.' `
        -Trigger $Trigger `
        -CountdownSeconds 0)
}

function Get-PrimaryScreenBounds {
    Initialize-DrawingAssemblies
    return [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
}

function Get-LetterboxUsageEstimate {
    param(
        [int]$Width,
        [int]$Height,
        [int]$TargetWidth = 1920,
        [int]$TargetHeight = 1080
    )

    $sourceAspect = [double]$Width / [double]$Height
    $targetAspect = [double]$TargetWidth / [double]$TargetHeight

    if ($sourceAspect -gt $targetAspect) {
        $drawWidth = $TargetWidth
        $drawHeight = [int][Math]::Round($TargetWidth / $sourceAspect)
    }
    else {
        $drawHeight = $TargetHeight
        $drawWidth = [int][Math]::Round($TargetHeight * $sourceAspect)
    }

    return @{
        DrawWidth     = $drawWidth
        DrawHeight    = $drawHeight
        WidthPercent  = [Math]::Round(100.0 * $drawWidth / $TargetWidth, 1)
        HeightPercent = [Math]::Round(100.0 * $drawHeight / $TargetHeight, 1)
    }
}

function Write-CaptureSizeHint {
    param(
        [int]$Width,
        [int]$Height
    )

    $usage = Get-LetterboxUsageEstimate -Width $Width -Height $Height
    Write-Host "  Capture: ${Width} x ${Height} px" -ForegroundColor DarkGray
    Write-Host "  On 1920x1080 after Prepare: ~$($usage.WidthPercent)% width, ~$($usage.HeightPercent)% height" -ForegroundColor DarkGray

    if ($usage.WidthPercent -lt 55) {
        Write-Host '  CmdPal looks narrow  -  drag its side edges wider before the next shot.' -ForegroundColor Yellow
    }
    elseif ($usage.HeightPercent -lt 55) {
        Write-Host '  CmdPal looks short  -  show more rows or resize taller.' -ForegroundColor Yellow
    }
    else {
        Write-Host '  Good size for Store listing captures.' -ForegroundColor Green
    }
}

function Invoke-MeasureCmdPalSize {
    param(
        [ValidateSet('Hotkey', 'Countdown', 'Terminal')]
        [string]$Trigger = 'Hotkey'
    )

    Write-Host ''
    Write-Host 'CmdPal size helper' -ForegroundColor Cyan
    Write-Host 'Drag CmdPal edges to resize. Use a dark desktop; capture uses the primary monitor.'
    Write-Host ''

    if ($Trigger -eq 'Terminal') {
        Write-Host 'Enter = read primary monitor size, Q = quit.'
        while ($true) {
            $response = Read-Host 'Enter=measure, Q=quit'
            if ($response -match '^[Qq]') { break }
            $bounds = Get-PrimaryScreenBounds
            Write-CaptureSizeHint -Width $bounds.Width -Height $bounds.Height
            Write-Host ''
        }

        return
    }

    Write-CaptureHotKeyLegend
    while ($true) {
        $action = Wait-CaptureHotKey -Prompt 'Open CmdPal on the primary monitor. F9 = done, F7 = read size.'
        if ($action -eq 'Capture' -or $action -eq 'Quit') { break }
        if ($action -eq 'Measure') {
            $bounds = Get-PrimaryScreenBounds
            Write-CaptureSizeHint -Width $bounds.Width -Height $bounds.Height
            Write-Host ''
        }
    }
}

function Wait-ForCmdPalSizeCalibration {
    param(
        [ValidateSet('Hotkey', 'Countdown', 'Terminal')]
        [string]$Trigger
    )

    if ($Trigger -eq 'Terminal') {
        Write-Host ''
        Write-Host 'Optional size check: open CmdPal on the primary monitor, drag edges, then continue.'
        Write-Host 'R = read primary monitor size, Enter = start captures.'
        Write-Host ''

        while ($true) {
            $response = Read-Host 'R=read size, Enter=start captures'
            if ([string]::IsNullOrWhiteSpace($response)) { break }
            if ($response -match '^[Rr]') {
                $bounds = Get-PrimaryScreenBounds
                Write-CaptureSizeHint -Width $bounds.Width -Height $bounds.Height
                Write-Host ''
                continue
            }

            Write-Host 'Use R or Enter.' -ForegroundColor DarkGray
        }

        return
    }

    Write-Host ''
    Write-Host 'Optional size check: open CmdPal, drag edges wider/taller if needed.' -ForegroundColor Cyan
    [void](Wait-CaptureTrigger `
        -Prompt 'Press F7 to read monitor size, or F9 when you are ready to start captures.' `
        -Trigger 'Hotkey' `
        -CountdownSeconds 0)
}

function Save-PrimaryScreenPng {
    param([string]$OutputPath)

    Initialize-DrawingAssemblies
    Start-Sleep -Milliseconds 250

    $bounds = Get-PrimaryScreenBounds
    $bitmap = New-Object System.Drawing.Bitmap $bounds.Width, $bounds.Height
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)

    try {
        $graphics.CopyFromScreen($bounds.Left, $bounds.Top, 0, 0, $bounds.Size)
        New-DirectoryIfMissing (Split-Path -Parent $OutputPath)
        $bitmap.Save($OutputPath, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }

    return @{
        Width  = $bounds.Width
        Height = $bounds.Height
    }
}

function Export-LetterboxedScreenshot {
    param(
        [string]$SourcePath,
        [string]$DestPath,
        [int]$TargetWidth,
        [int]$TargetHeight
    )

    Initialize-DrawingAssemblies

    $loaded = [System.Drawing.Image]::FromFile($SourcePath)
    $source = [System.Drawing.Bitmap]$loaded

    try {
        $targetAspect = [double]$TargetWidth / [double]$TargetHeight
        $sourceAspect = [double]$source.Width / [double]$source.Height

        if ($sourceAspect -gt $targetAspect) {
            $drawWidth = $TargetWidth
            $drawHeight = [int][Math]::Round($TargetWidth / $sourceAspect)
        }
        else {
            $drawHeight = $TargetHeight
            $drawWidth = [int][Math]::Round($TargetHeight * $sourceAspect)
        }

        $offsetX = [int][Math]::Round(($TargetWidth - $drawWidth) / 2.0)
        $offsetY = [int][Math]::Round(($TargetHeight - $drawHeight) / 2.0)

        $canvas = New-Object System.Drawing.Bitmap $TargetWidth, $TargetHeight
        $graphics = [System.Drawing.Graphics]::FromImage($canvas)

        try {
            $graphics.Clear($BackgroundColorArgb)
            $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
            $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.DrawImage($source, $offsetX, $offsetY, $drawWidth, $drawHeight)
            New-DirectoryIfMissing (Split-Path -Parent $DestPath)
            $canvas.Save($DestPath, [System.Drawing.Imaging.ImageFormat]::Png)
        }
        finally {
            $graphics.Dispose()
            $canvas.Dispose()
        }
    }
    finally {
        $source.Dispose()
    }
}

function Update-QuickShellAssetScreenshots {
    param([string]$PreparedDir)

    $primaryDir = Join-Path $PreparedDir '1920x1080'
    if (-not (Test-Path $primaryDir)) {
        Write-Warning "Prepared 1920x1080 folder not found: $primaryDir"
        return
    }

    Write-Host ''
    Write-Host "Updating README assets in $AssetsDir ..." -ForegroundColor Cyan

    foreach ($shot in $ShotGuide) {
        if ([string]::IsNullOrWhiteSpace($shot.ReadmeAsset)) {
            continue
        }

        $source = Join-Path $primaryDir ($shot.Id + '.png')
        if (-not (Test-Path $source)) {
            Write-Warning "Skipping $($shot.ReadmeAsset): $source not found."
            continue
        }

        $dest = Join-Path $AssetsDir $shot.ReadmeAsset
        Copy-Item $source $dest -Force
        Write-Host "  Overwrote $($shot.ReadmeAsset)" -ForegroundColor Green
    }
}

function Invoke-PrepareStoreScreenshots {
    param(
        [string]$InputDir,
        [string]$OutputDir,
        [switch]$SkipAssetsCopy
    )

    New-DirectoryIfMissing $InputDir
    New-DirectoryIfMissing $OutputDir

    $sources = Get-ChildItem -Path $InputDir -Filter '*.png' -File | Sort-Object Name
    if ($sources.Count -eq 0) {
        throw "No PNG files found in $InputDir. Run -Mode Capture first."
    }

    foreach ($size in $StoreSizes) {
        New-DirectoryIfMissing (Join-Path $OutputDir $size.Label)
    }

    foreach ($source in $sources) {
        $baseName = [System.IO.Path]::GetFileNameWithoutExtension($source.Name)
        Write-Host "Preparing $baseName..." -ForegroundColor Cyan

        foreach ($size in $StoreSizes) {
            $dest = Join-Path (Join-Path $OutputDir $size.Label) ($baseName + '.png')
            Export-LetterboxedScreenshot `
                -SourcePath $source.FullName `
                -DestPath $dest `
                -TargetWidth $size.Width `
                -TargetHeight $size.Height
            Write-Host "  -> $($size.Label)\$baseName.png"
        }
    }

    if (-not $SkipAssetsCopy) {
        Update-QuickShellAssetScreenshots -PreparedDir $OutputDir
    }

    Write-Host ''
    Write-Host "Prepared screenshots in $OutputDir" -ForegroundColor Green
    if (-not $SkipAssetsCopy) {
        Write-Host "Repo assets updated: Screenshot_1.png … Screenshot_3.png" -ForegroundColor DarkGray
    }
}

function Invoke-CaptureStoreScreenshots {
    param(
        [string]$RawDir,
        [switch]$UseDemoShortcuts,
        [switch]$KeepDemoShortcuts,
        [ValidateSet('Hotkey', 'Countdown', 'Terminal')]
        [string]$CaptureTrigger = 'Hotkey',
        [int]$CountdownSeconds = 5
    )

    $installedDemo = $false

    try {
        if ($UseDemoShortcuts) {
            Backup-LiveShortcuts
            Install-DemoShortcuts
            $installedDemo = $true
            Wait-ForCmdPalReload -Trigger $CaptureTrigger -CountdownSeconds $CountdownSeconds
        }

        New-DirectoryIfMissing $RawDir

        Write-Host ''
        Write-Host 'Quick Shell Store screenshot capture' -ForegroundColor Cyan
        Write-Host 'Use a dark desktop on your primary monitor. Each shot captures the full primary screen.'
        if ($CaptureTrigger -eq 'Hotkey') {
            Write-Host 'Keep CmdPal focused  -  use F9/F8/F12 (see legend below). Do not click this terminal between shots.'
        }
        elseif ($CaptureTrigger -eq 'Countdown') {
            Write-Host "Each shot auto-captures after $CountdownSeconds seconds once you start it."
        }
        else {
            Write-Host 'Bring CmdPal to the front, then press Enter here to capture.'
        }

        Write-Host "Raw captures -> $RawDir" -ForegroundColor DarkGray
        Write-Host ''

        Wait-ForCmdPalSizeCalibration -Trigger $CaptureTrigger

        $shotNumber = 0
        foreach ($shot in $ShotGuide) {
            $shotNumber++
            Write-Host "$shotNumber. $($shot.Prompt)" -ForegroundColor Yellow
            Write-Host "   $($shot.ManualHint)" -ForegroundColor DarkGray

            $action = Wait-CaptureTrigger `
                -Prompt 'Position CmdPal, then trigger capture.' `
                -Trigger $CaptureTrigger `
                -CountdownSeconds $CountdownSeconds

            if ($action -eq 'Quit') {
                break
            }

            if ($action -eq 'Skip') {
                Write-Host 'Skipped.' -ForegroundColor DarkGray
                Write-Host ''
                continue
            }

            $outPath = Join-Path $RawDir ($shot.Id + '.png')

            try {
                [console]::Beep(880, 120)
                Start-Sleep -Milliseconds 150
                $captured = Save-PrimaryScreenPng -OutputPath $outPath
                Write-Host "Saved $outPath" -ForegroundColor Green
                Write-CaptureSizeHint -Width $captured.Width -Height $captured.Height
            }
            catch {
                Write-Warning $_.Exception.Message
            }

            Write-Host ''
        }

        Write-Host 'Capture done. Run:' -ForegroundColor Cyan
        Write-Host '  .\scripts\store-screenshots.ps1 -Mode Prepare'
    }
    finally {
        if ($installedDemo -and -not $KeepDemoShortcuts) {
            Restore-LiveShortcuts
        }
    }
}

function Show-StoreScreenshotHelp {
    Write-Host @'

Quick Shell  -  Store screenshots
================================

  1. (Optional) Measure CmdPal width on your primary monitor:
       .\scripts\store-screenshots.ps1 -Mode Measure

  2. Capture three screenshots (CmdPal stays focused  -  global hotkeys):
       .\scripts\store-screenshots.ps1 -Mode Capture
         F9  = capture / continue
         F8  = skip shot
         F7  = read monitor size (during setup)
         F12 = quit
         1. Shortcut list + context menu (My App, Ctrl+K)
         2. Edit shortcut form (My App Admin, Ctrl+E)
         3. Quick Shell settings

     Alternative triggers:
       -CaptureTrigger Countdown     # auto-capture after 5s (no hotkeys)
       -CaptureTrigger Terminal       # legacy Enter-in-terminal prompts

  3. Prepare Store PNGs (+ README Screenshot_1-3.png):
       .\scripts\store-screenshots.ps1 -Mode Prepare

  4. Upload prepared\1920x1080\*.png to Partner Center.

Dev shortcuts: import dev-shortcuts.json (pwsh) from repo root.

If Windows Defender blocks this script, allow scripts\store-screenshots.ps1
under Virus & threat protection -> Protection history.

'@ -ForegroundColor DarkGray
}

$resolvedRaw = Resolve-PathOrDefault -Path $RawDir -Default $DefaultRawDir
$resolvedPrepared = Resolve-PathOrDefault -Path $PreparedDir -Default $DefaultPreparedDir

switch ($Mode) {
    'Capture' {
        Invoke-CaptureStoreScreenshots `
            -RawDir $resolvedRaw `
            -UseDemoShortcuts:$UseDemoShortcuts `
            -KeepDemoShortcuts:$KeepDemoShortcuts `
            -CaptureTrigger $CaptureTrigger `
            -CountdownSeconds $CountdownSeconds
    }
    'Prepare' {
        Invoke-PrepareStoreScreenshots `
            -InputDir $resolvedRaw `
            -OutputDir $resolvedPrepared `
            -SkipAssetsCopy:$SkipAssetsCopy
    }
    'Measure' {
        Invoke-MeasureCmdPalSize -Trigger $CaptureTrigger
    }
    'InstallDemo' {
        Backup-LiveShortcuts
        Install-DemoShortcuts
    }
    'RestoreShortcuts' {
        Restore-LiveShortcuts
    }
    default {
        Show-StoreScreenshotHelp
    }
}
