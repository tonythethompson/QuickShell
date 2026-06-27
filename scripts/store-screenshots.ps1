#Requires -Version 5.1
<#
.SYNOPSIS
    Capture and prepare Microsoft Store desktop screenshots for Quick Shell.

.DESCRIPTION
    Two-step workflow (or one command — see -Mode Easy):

      Easy / All  (-Mode Easy or -Mode All)
         Installs demo shortcuts, reloads extensions via keyboard, navigates CmdPal
         to each of the 3 screens, countdown-captures, Prepares Store PNGs, and
         opens the output folder. Minimal button pressing.

      1. Capture  (-Mode Capture)
         Manual capture — you bring each screen to the front and press Enter.

      2. Prepare  (-Mode Prepare)
         Letterboxes each raw PNG onto Store-safe 16:9 canvases at:
           1366 x 768   (minimum)
           1920 x 1080  (recommended — upload these to Partner Center)
           3840 x 2160  (optional 4K)

    You do NOT need to hit exact pixel sizes manually — Prepare handles that.

.PARAMETER Mode
    Easy | All | Capture | Prepare | Measure | InstallDemo | RestoreShortcuts | Help

.PARAMETER CountdownSeconds
    Seconds before each auto-capture in Easy mode (default: 6).

.PARAMETER SkipAutoNavigate
    Easy mode only: skip keyboard navigation; you position CmdPal yourself during the countdown.

.PARAMETER OpenOutputFolder
    Easy/All: open prepared\1920x1080 in Explorer when done (default: on).

.PARAMETER UseDemoShortcuts
    During Capture, swap in demo shortcuts before capture and restore after (default: on).

.PARAMETER KeepDemoShortcuts
    Leave demo shortcuts installed after Capture (skip restore).

.PARAMETER CopyToReadmeAssets
    Deprecated alias — asset copy is on by default. Use -SkipAssetsCopy to disable.

.PARAMETER SkipAssetsCopy
    After Prepare, do not overwrite QuickShell\Assets\Screenshot_1.png … Screenshot_3.png.

.EXAMPLE
    .\scripts\store-screenshots.ps1 -Mode Easy

.EXAMPLE
    .\scripts\store-screenshots.ps1 -Mode All

.EXAMPLE
    .\scripts\store-screenshots.ps1 -Mode Capture

.EXAMPLE
    .\scripts\store-screenshots.ps1 -Mode Prepare

.EXAMPLE
    .\scripts\store-screenshots.ps1 -Mode InstallDemo

.EXAMPLE
    .\scripts\store-screenshots.ps1 -Mode RestoreShortcuts
#>
[CmdletBinding()]
param(
    [ValidateSet('Easy', 'All', 'Capture', 'Prepare', 'Measure', 'InstallDemo', 'RestoreShortcuts', 'Help')]
    [string]$Mode = 'Help',

    [string]$RawDir,
    [string]$PreparedDir,
    [int]$CountdownSeconds = 6,
    [switch]$SkipAutoNavigate,
    [switch]$OpenOutputFolder = $true,
    [switch]$SkipAssetsCopy,
    [switch]$CopyToReadmeAssets,
    [switch]$UseDemoShortcuts = $true,
    [switch]$KeepDemoShortcuts
)

$ErrorActionPreference = 'Stop'

$ProjectRoot = Split-Path -Parent $PSScriptRoot
$AssetsDir = Join-Path $ProjectRoot 'QuickShell\Assets'
$DefaultRawDir = Join-Path $AssetsDir 'StoreScreenshots\raw'
$DefaultPreparedDir = Join-Path $AssetsDir 'StoreScreenshots\prepared'
$DemoShortcutsPath = Join-Path $AssetsDir 'StoreScreenshots\store-demo-shortcuts.json'
$ShortcutsBackupPath = Join-Path $AssetsDir 'StoreScreenshots\shortcuts-backup.json'
$LiveShortcutsPath = Join-Path $env:LOCALAPPDATA 'QuickShell\shortcuts.json'

$StoreSizes = @(
    @{ Width = 1366; Height = 768; Label = '1366x768' }
    @{ Width = 1920; Height = 1080; Label = '1920x1080' }
    @{ Width = 3840; Height = 2160; Label = '3840x2160' }
)

Add-Type -AssemblyName System.Drawing

$BackgroundColor = [System.Drawing.Color]::FromArgb(255, 28, 28, 28)

$ShotGuide = @(
    @{
        Id          = '01-list-context-menu'
        ReadmeAsset = 'Screenshot_1.png'
        Prompt      = 'Shortcut list: My App Admin + My App favorites. Select My App and open the context menu (Ctrl+K).'
        ManualHint  = 'Open Quick Shell → Down once to My App → Ctrl+K'
    }
    @{
        Id          = '02-edit-shortcut'
        ReadmeAsset = 'Screenshot_2.png'
        Prompt      = 'Edit a shortcut (e.g. My App Admin — admin keyword, claude command, run as admin checked).'
        ManualHint  = 'Esc (close menu) → Up to My App Admin → Ctrl+E'
    }
    @{
        Id          = '03-settings'
        ReadmeAsset = 'Screenshot_3.png'
        Prompt      = 'Quick Shell settings (Settings on any row, or from the home Quick Shell menu).'
        ManualHint  = 'Esc back to palette → type quick shell settings → Enter'
    }
)

Add-Type -AssemblyName System.Windows.Forms

Add-Type @"
using System;
using System.Runtime.InteropServices;
public struct NativeRect {
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
}
public static class StoreScreenshotNative {
    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out NativeRect lpRect);
    [DllImport("user32.dll")]
    public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")]
    public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, int dwExtraInfo);
    [DllImport("kernel32.dll")]
    public static extern IntPtr GetConsoleWindow();
    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private const int KeyeventfKeyup = 0x0002;

    public static void MinimizeConsole() {
        ShowWindow(GetConsoleWindow(), 6);
    }

    public static void RestoreConsole() {
        ShowWindow(GetConsoleWindow(), 9);
    }

    public static void OpenCommandPalette() {
        KeyDown(0x5B);
        KeyDown(0x12);
        KeyDown(0x20);
        KeyUp(0x20);
        KeyUp(0x12);
        KeyUp(0x5B);
    }

    public static void SendChord(bool ctrl, bool alt, bool shift, byte vk) {
        if (ctrl) { KeyDown(0x11); }
        if (alt) { KeyDown(0x12); }
        if (shift) { KeyDown(0x10); }
        KeyDown(vk);
        KeyUp(vk);
        if (shift) { KeyUp(0x10); }
        if (alt) { KeyUp(0x12); }
        if (ctrl) { KeyUp(0x11); }
    }

    private static void KeyDown(byte vk) {
        keybd_event(vk, 0, 0, 0);
    }

    private static void KeyUp(byte vk) {
        keybd_event(vk, 0, KeyeventfKeyup, 0);
    }
}
"@

[StoreScreenshotNative]::SetProcessDPIAware() | Out-Null

function Get-QuickShellConfigDirectory {
    Join-Path $env:LOCALAPPDATA 'QuickShell'
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
    Write-Host 'Run Reload Command Palette Extension in CmdPal so Quick Shell picks up the demo list.' -ForegroundColor Yellow
}

function Restore-LiveShortcuts {
    if (-not (Test-Path $ShortcutsBackupPath)) {
        throw "No backup found at $ShortcutsBackupPath. Nothing to restore."
    }

    New-DirectoryIfMissing (Get-QuickShellConfigDirectory)
    Copy-Item $ShortcutsBackupPath $LiveShortcutsPath -Force
    Write-Host "Restored your shortcuts -> $LiveShortcutsPath" -ForegroundColor Green
    Write-Host 'Run Reload Command Palette Extension again to pick up your real list.' -ForegroundColor Yellow
}

function Wait-ForCmdPalReload {
    Write-Host ''
    Read-Host 'After Reload Command Palette Extension, press Enter to continue'
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

function New-DirectoryIfMissing {
    param([string]$Path)
    if (-not (Test-Path $Path)) {
        New-Item -ItemType Directory -Force -Path $Path | Out-Null
    }
}

function Get-ForegroundWindowSize {
    $hwnd = [StoreScreenshotNative]::GetForegroundWindow()
    if ($hwnd -eq [IntPtr]::Zero) {
        throw 'No foreground window found.'
    }

    $rect = New-Object NativeRect
    if (-not [StoreScreenshotNative]::GetWindowRect($hwnd, [ref]$rect)) {
        throw 'Could not read foreground window bounds.'
    }

    $width = $rect.Right - $rect.Left
    $height = $rect.Bottom - $rect.Top
    if ($width -le 0 -or $height -le 0) {
        throw "Foreground window has invalid size (${width}x${height})."
    }

    return @{
        Left   = $rect.Left
        Top    = $rect.Top
        Width  = $width
        Height = $height
    }
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
        DrawWidth      = $drawWidth
        DrawHeight     = $drawHeight
        WidthPercent   = [Math]::Round(100.0 * $drawWidth / $TargetWidth, 1)
        HeightPercent  = [Math]::Round(100.0 * $drawHeight / $TargetHeight, 1)
    }
}

function Write-CaptureSizeHint {
    param(
        [int]$Width,
        [int]$Height
    )

    $usage = Get-LetterboxUsageEstimate -Width $Width -Height $Height
    Write-Host "  Window: ${Width} x ${Height} px" -ForegroundColor DarkGray
    Write-Host "  On a 1920x1080 Store frame after Prepare: ~$($usage.WidthPercent)% width, ~$($usage.HeightPercent)% height" -ForegroundColor DarkGray

    if ($usage.WidthPercent -lt 55) {
        Write-Host '  CmdPal looks narrow in the final image — drag its side edges wider, then measure again.' -ForegroundColor Yellow
    }
    elseif ($usage.HeightPercent -lt 55) {
        Write-Host '  CmdPal looks short in the final image — show more rows or drag the bottom edge taller.' -ForegroundColor Yellow
    }
    else {
        Write-Host '  Good size for Store listing captures.' -ForegroundColor Green
    }
}

function Invoke-MeasureCmdPalSize {
    Write-Host ''
    Write-Host 'CmdPal size helper' -ForegroundColor Cyan
    Write-Host '------------------'
    Write-Host 'CmdPal width is set by dragging its window edges (PowerToys 0.98+ remembers it).'
    Write-Host 'There is no magic pixel width — widen until the readout below looks good, then capture.'
    Write-Host ''
    Write-Host '1. Open Command Palette and bring it to the front.'
    Write-Host '2. Drag edges to resize.'
    Write-Host '3. Press Enter here to read the foreground window size.'
    Write-Host '4. Press Q when done.'
    Write-Host ''

    while ($true) {
        $response = Read-Host 'Enter=measure, Q=quit'
        if ($response -match '^[Qq]') {
            break
        }

        try {
            $size = Get-ForegroundWindowSize
            Write-CaptureSizeHint -Width $size.Width -Height $size.Height
        }
        catch {
            Write-Warning $_.Exception.Message
        }

        Write-Host ''
    }
}

function Wait-ForCmdPalSizeCalibration {
    Write-Host ''
    Write-Host 'Size calibration (optional but recommended)' -ForegroundColor Cyan
    Write-Host 'Open CmdPal, drag its edges to a comfortable width, bring CmdPal to the front.'
    Write-Host 'Press R to read size while tweaking; Enter when ready to start captures.'
    Write-Host ''

    while ($true) {
        $response = Read-Host 'R=read size, Enter=start captures'
        if ([string]::IsNullOrWhiteSpace($response)) {
            break
        }

        if ($response -match '^[Rr]') {
            try {
                $size = Get-ForegroundWindowSize
                Write-CaptureSizeHint -Width $size.Width -Height $size.Height
            }
            catch {
                Write-Warning $_.Exception.Message
            }

            Write-Host ''
            continue
        }

        Write-Host 'Use R to read size or Enter to continue.' -ForegroundColor DarkGray
    }
}

function Capture-ForegroundWindow {
    param([string]$OutputPath)

    Start-Sleep -Milliseconds 250

    $size = Get-ForegroundWindowSize
    $width = $size.Width
    $height = $size.Height

    $bitmap = New-Object System.Drawing.Bitmap $width, $height
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen($size.Left, $size.Top, 0, 0, [System.Drawing.Size]::new($width, $height))
        New-DirectoryIfMissing (Split-Path -Parent $OutputPath)
        $bitmap.Save($OutputPath, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }

    return @{
        Width  = $width
        Height = $height
    }
}

function Test-IsScreenshotBackgroundPixel {
    param([System.Drawing.Color]$Color)

    # CmdPal UI is dark; treat near-black pixels as empty margin.
    $luminance = (0.299 * $Color.R) + (0.587 * $Color.G) + (0.114 * $Color.B)
    return $luminance -lt 28
}

function Get-ScreenshotContentBounds {
    param(
        [System.Drawing.Bitmap]$Bitmap,
        [int]$Padding = 16
    )

    if ($Bitmap.Width -le 0 -or $Bitmap.Height -le 0) {
        return $null
    }

    $minX = $Bitmap.Width
    $minY = $Bitmap.Height
    $maxX = -1
    $maxY = -1

    for ($y = 0; $y -lt $Bitmap.Height; $y++) {
        for ($x = 0; $x -lt $Bitmap.Width; $x++) {
            if (Test-IsScreenshotBackgroundPixel -Color $Bitmap.GetPixel($x, $y)) {
                continue
            }

            if ($x -lt $minX) { $minX = $x }
            if ($y -lt $minY) { $minY = $y }
            if ($x -gt $maxX) { $maxX = $x }
            if ($y -gt $maxY) { $maxY = $y }
        }
    }

    if ($maxX -lt $minX -or $maxY -lt $minY) {
        return $null
    }

    $minX = [Math]::Max(0, $minX - $Padding)
    $minY = [Math]::Max(0, $minY - $Padding)
    $maxX = [Math]::Min($Bitmap.Width - 1, $maxX + $Padding)
    $maxY = [Math]::Min($Bitmap.Height - 1, $maxY + $Padding)

    return @{
        X      = $minX
        Y      = $minY
        Width  = ($maxX - $minX) + 1
        Height = ($maxY - $minY) + 1
    }
}

function Copy-BitmapRegion {
    param(
        [System.Drawing.Bitmap]$Source,
        [int]$X,
        [int]$Y,
        [int]$Width,
        [int]$Height
    )

    $cropped = New-Object System.Drawing.Bitmap $Width, $Height
    $graphics = [System.Drawing.Graphics]::FromImage($cropped)
    try {
        $graphics.DrawImage(
            $Source,
            (New-Object System.Drawing.Rectangle 0, 0, $Width, $Height),
            $X,
            $Y,
            $Width,
            $Height,
            [System.Drawing.GraphicsUnit]::Pixel)
    }
    finally {
        $graphics.Dispose()
    }

    return $cropped
}

function Trim-ScreenshotMargins {
    param([System.Drawing.Bitmap]$Bitmap)

    $bounds = Get-ScreenshotContentBounds -Bitmap $Bitmap
    if ($null -eq $bounds) {
        return $Bitmap
    }

    if ($bounds.Width -ge $Bitmap.Width -and $bounds.Height -ge $Bitmap.Height) {
        return $Bitmap
    }

    $cropped = Copy-BitmapRegion `
        -Source $Bitmap `
        -X $bounds.X `
        -Y $bounds.Y `
        -Width $bounds.Width `
        -Height $bounds.Height
    $Bitmap.Dispose()
    return $cropped
}

function Export-LetterboxedScreenshot {
    param(
        [string]$SourcePath,
        [string]$DestPath,
        [int]$TargetWidth,
        [int]$TargetHeight,
        [System.Drawing.Color]$Background = $BackgroundColor
    )

    $loaded = [System.Drawing.Image]::FromFile($SourcePath)
    $source = [System.Drawing.Bitmap]$loaded
    try {
        $source = Trim-ScreenshotMargins -Bitmap $source

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
            $graphics.Clear($Background)
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
    Write-Host "Updating README + repo assets in $AssetsDir ..." -ForegroundColor Cyan

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
        throw "No PNG files found in $InputDir. Run -Mode Capture first, or drop PNGs into that folder."
    }

    foreach ($size in $StoreSizes) {
        $sizeDir = Join-Path $OutputDir $size.Label
        New-DirectoryIfMissing $sizeDir
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
        Write-Host "Repo assets updated: $AssetsDir\Screenshot_1.png … Screenshot_3.png" -ForegroundColor DarkGray
    }
    Write-Host 'Upload the PNGs from prepared\1920x1080\ to Partner Center (Store listing).' -ForegroundColor DarkGray
}

function Invoke-CaptureStoreScreenshots {
    param(
        [string]$RawDir,
        [switch]$UseDemoShortcuts,
        [switch]$KeepDemoShortcuts
    )

    $installedDemo = $false
    try {
        if ($UseDemoShortcuts) {
            Backup-LiveShortcuts
            Install-DemoShortcuts
            $installedDemo = $true
            Wait-ForCmdPalReload
        }

        New-DirectoryIfMissing $RawDir

        Write-Host ''
        Write-Host 'Quick Shell Store screenshot capture' -ForegroundColor Cyan
        Write-Host '------------------------------------'
        Write-Host '1. Open Command Palette (Win+Alt+Space by default).'
        Write-Host '2. Drag CmdPal edges to set width once — use R during calibration or run -Mode Measure first.'
        Write-Host '3. For each step below, bring the CmdPal window to the front.'
        Write-Host '4. Press Enter here to capture the foreground window.'
        Write-Host '5. Press S to skip a step, Q to quit early.'
        if ($UseDemoShortcuts) {
            Write-Host '5. Demo shortcuts active: My App Admin + My App favorites.' -ForegroundColor DarkGray
        }
        Write-Host ''
        Write-Host "Raw captures -> $RawDir" -ForegroundColor DarkGray

        Wait-ForCmdPalSizeCalibration

        foreach ($shot in $ShotGuide) {
            Write-Host "[$($shot.Id)] $($shot.Prompt)" -ForegroundColor Yellow
            $response = Read-Host 'Enter=capture, S=skip, Q=quit'
            if ($response -match '^[Qq]') {
                break
            }

            if ($response -match '^[Ss]') {
                Write-Host 'Skipped.' -ForegroundColor DarkGray
                continue
            }

            $outPath = Join-Path $RawDir ($shot.Id + '.png')
            try {
                $captured = Capture-ForegroundWindow -OutputPath $outPath
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

Quick Shell — Store screenshots
================================

Microsoft Store desktop screenshots must be PNG, 16:9, at least 1366x768.
CmdPal width: drag its window edges (PowerToys remembers size). You do NOT guess
by eye — run Measure first, widen until ~65%+ width on the 1920x1080 readout,
then capture all shots at that same size. Prepare letterboxes to Store sizes.

Workflow
--------
  0. Optional — dial in CmdPal width with pixel readout:
       .\scripts\store-screenshots.ps1 -Mode Measure

  1. Capture (installs demo shortcuts by default, restores yours after):
       .\scripts\store-screenshots.ps1 -Mode Capture

  2. Prepare (also overwrites QuickShell\Assets\Screenshot_1.png … Screenshot_3.png):
       .\scripts\store-screenshots.ps1 -Mode Prepare

  3. Upload prepared\1920x1080\*.png to Partner Center (three shots: list+menu, edit, settings).

Demo shortcuts
--------------
  Preset: QuickShell\Assets\StoreScreenshots\store-demo-shortcuts.json
  (My App Admin + My App favorites — edit store-demo-shortcuts.json to tweak.)

  Install demo only:   -Mode InstallDemo
  Restore from backup: -Mode RestoreShortcuts
  Keep demo after capture: -Mode Capture -KeepDemoShortcuts

Already have PNGs?
------------------
Drop them in QuickShell\Assets\StoreScreenshots\raw\ and run Prepare only.

Optional: use the Microsoft Store Asset Guidance Kit in Figma for framed
marketing shots — export to raw\ then run Prepare.

'@ -ForegroundColor DarkGray
}

$resolvedRaw = Resolve-PathOrDefault -Path $RawDir -Default $DefaultRawDir
$resolvedPrepared = Resolve-PathOrDefault -Path $PreparedDir -Default $DefaultPreparedDir
$shouldUpdateAssets = -not $SkipAssetsCopy

switch ($Mode) {
    'Capture' {
        Invoke-CaptureStoreScreenshots `
            -RawDir $resolvedRaw `
            -UseDemoShortcuts:$UseDemoShortcuts `
            -KeepDemoShortcuts:$KeepDemoShortcuts
    }
    'Prepare' {
        Invoke-PrepareStoreScreenshots `
            -InputDir $resolvedRaw `
            -OutputDir $resolvedPrepared `
            -SkipAssetsCopy:(-not $shouldUpdateAssets)
    }
    'Measure' {
        Invoke-MeasureCmdPalSize
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
