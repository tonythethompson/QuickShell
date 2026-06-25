#Requires -Version 5.1
# Generates MSIX logo assets for Quick Shell using the same Segoe Fluent glyph as CmdPal (E756).
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

$assetsDir = Join-Path (Split-Path -Parent $PSScriptRoot) 'QuickShell\Assets'
New-Item -ItemType Directory -Force -Path $assetsDir | Out-Null

# Same code point as IconInfo("\uE756") in QuickShellCommandsProvider.
$QuickShellGlyph = [char]0xE756
$IconFontFamilies = @('Segoe Fluent Icons', 'Segoe MDL2 Assets')

function Get-IconFont {
    param([single]$Size)

    foreach ($family in $IconFontFamilies) {
        $font = New-Object System.Drawing.Font $family, $Size
        if ($font.Name -eq $family) {
            return $font
        }

        $font.Dispose()
    }

    throw "Neither Segoe Fluent Icons nor Segoe MDL2 Assets is available."
}

function New-GlyphBitmap {
    param(
        [int]$Width,
        [int]$Height,
        [char]$Glyph = $QuickShellGlyph,
        [System.Drawing.Color]$Foreground = [System.Drawing.Color]::FromArgb(255, 235, 235, 235),
        [System.Drawing.Color]$Background = [System.Drawing.Color]::Transparent
    )

    $bitmap = New-Object System.Drawing.Bitmap $Width, $Height
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $graphics.Clear($Background)

    $fontSize = [Math]::Max(10, [single]([Math]::Min($Width, $Height) * 0.62))
    $font = Get-IconFont -Size $fontSize
    $brush = New-Object System.Drawing.SolidBrush $Foreground
    $format = New-Object System.Drawing.StringFormat
    $format.Alignment = [System.Drawing.StringAlignment]::Center
    $format.LineAlignment = [System.Drawing.StringAlignment]::Center

    $rect = New-Object System.Drawing.RectangleF 0, 0, $Width, $Height
    $graphics.DrawString([string]$Glyph, $font, $brush, $rect, $format)

    $brush.Dispose()
    $font.Dispose()
    $format.Dispose()
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
    @{ Path = 'Square44x44Logo.png'; Width = 44; Height = 44 },
    @{ Path = 'Square44x44Logo.targetsize-24_altform-unplated.png'; Width = 24; Height = 24 },
    @{ Path = 'Square44x44Logo.scale-200.png'; Width = 88; Height = 88 },
    @{ Path = 'Square150x150Logo.scale-200.png'; Width = 300; Height = 300 },
    @{ Path = 'Wide310x150Logo.scale-200.png'; Width = 620; Height = 300 },
    @{ Path = 'SplashScreen.scale-200.png'; Width = 620; Height = 300 },
    @{ Path = 'LockScreenLogo.scale-200.png'; Width = 48; Height = 48 }
)

foreach ($asset in $assets) {
    $outPath = Join-Path $assetsDir $asset.Path
    $bitmap = New-GlyphBitmap -Width $asset.Width -Height $asset.Height
    Save-Png -Bitmap $bitmap -Path $outPath
    Write-Host "Wrote $outPath"
}

# Unscaled aliases referenced by Package.appxmanifest.
Copy-Item (Join-Path $assetsDir 'Square150x150Logo.scale-200.png') (Join-Path $assetsDir 'Square150x150Logo.png') -Force
Copy-Item (Join-Path $assetsDir 'Wide310x150Logo.scale-200.png') (Join-Path $assetsDir 'Wide310x150Logo.png') -Force
Copy-Item (Join-Path $assetsDir 'SplashScreen.scale-200.png') (Join-Path $assetsDir 'SplashScreen.png') -Force

Write-Host "Quick Shell assets generated in $assetsDir"
