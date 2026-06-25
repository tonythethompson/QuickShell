#Requires -Version 5.1
# Generates MSIX logo assets for Quick Shell (terminal-style icon).
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

$assetsDir = Join-Path (Split-Path -Parent $PSScriptRoot) 'QuickShell\Assets'
New-Item -ItemType Directory -Force -Path $assetsDir | Out-Null

function New-TerminalBitmap {
    param(
        [int]$Width,
        [int]$Height
    )

    $bitmap = New-Object System.Drawing.Bitmap $Width, $Height
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([System.Drawing.Color]::FromArgb(255, 30, 30, 30))

    $margin = [Math]::Max(2, [int]([Math]::Min($Width, $Height) * 0.12))
    $frameWidth = $Width - (2 * $margin)
    $frameHeight = $Height - (2 * $margin)
    $frame = New-Object System.Drawing.Rectangle $margin, $margin, $frameWidth, $frameHeight

    $framePen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 230, 230, 230)), ([Math]::Max(1, [int]($frame.Width / 22)))
    $graphics.DrawRectangle($framePen, $frame)

  $titleBarHeight = [Math]::Max(4, [int]($frame.Height * 0.18))
    $graphics.FillRectangle(
        (New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 55, 55, 55))),
        $frame.X,
        $frame.Y,
        $frame.Width,
        $titleBarHeight)

    if ($frame.Width -ge 32) {
        $fontSize = [Math]::Max(6, [int]($frame.Width / 7))
        $font = New-Object System.Drawing.Font ('Consolas', [single]$fontSize, [System.Drawing.FontStyle]::Bold)
        $textY = $frame.Y + $titleBarHeight + ([Math]::Max(2, [int](($frame.Height - $titleBarHeight) / 6)))
        $graphics.DrawString('C:\', $font, [System.Drawing.Brushes]::White, ($frame.X + [int]($frame.Width / 10)), $textY)
        $font.Dispose()
    }

    $graphics.Dispose()
    return $bitmap
}

function Save-Png {
    param(
        [System.Drawing.Bitmap]$Bitmap,
        [string]$Path
    )

    $Bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $Bitmap.Dispose()
}

$assets = @(
    @{ Path = 'StoreLogo.png'; Width = 50; Height = 50 },
    @{ Path = 'Square44x44Logo.targetsize-24_altform-unplated.png'; Width = 24; Height = 24 },
    @{ Path = 'Square44x44Logo.scale-200.png'; Width = 88; Height = 88 },
    @{ Path = 'Square150x150Logo.scale-200.png'; Width = 300; Height = 300 },
    @{ Path = 'Wide310x150Logo.scale-200.png'; Width = 620; Height = 300 },
    @{ Path = 'SplashScreen.scale-200.png'; Width = 620; Height = 300 },
    @{ Path = 'LockScreenLogo.scale-200.png'; Width = 48; Height = 48 }
)

foreach ($asset in $assets) {
    $outPath = Join-Path $assetsDir $asset.Path
    $bitmap = New-TerminalBitmap -Width $asset.Width -Height $asset.Height
    Save-Png -Bitmap $bitmap -Path $outPath
    Write-Host "Wrote $outPath"
}

# Unscaled aliases referenced by Package.appxmanifest.
Copy-Item (Join-Path $assetsDir 'Square44x44Logo.scale-200.png') (Join-Path $assetsDir 'Square44x44Logo.png') -Force
Copy-Item (Join-Path $assetsDir 'Square150x150Logo.scale-200.png') (Join-Path $assetsDir 'Square150x150Logo.png') -Force
Copy-Item (Join-Path $assetsDir 'Wide310x150Logo.scale-200.png') (Join-Path $assetsDir 'Wide310x150Logo.png') -Force
Copy-Item (Join-Path $assetsDir 'SplashScreen.scale-200.png') (Join-Path $assetsDir 'SplashScreen.png') -Force

Write-Host "Quick Shell assets generated in $assetsDir"
