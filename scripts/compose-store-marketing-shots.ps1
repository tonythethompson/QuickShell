#Requires -Version 5.1
<#
.SYNOPSIS
    Build Store/README marketing screenshots from raw CmdPal captures.
#>
[CmdletBinding()]
param(
    [string]$RawDir = (Join-Path $PSScriptRoot '..\QuickShell\Assets\StoreScreenshots\figma\_raw_backup'),
    [string]$AssetsDir = (Join-Path $PSScriptRoot '..\QuickShell\Assets')
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

function Test-IsContentPixel {
    param(
        [System.Drawing.Color]$Color,
        [int]$Threshold = 52
    )

    $brightness = [Math]::Max($Color.R, [Math]::Max($Color.G, $Color.B))
    return ($brightness -ge $Threshold)
}

function Get-ContentTrimBounds {
    param([System.Drawing.Bitmap]$Bitmap)

    $left = $Bitmap.Width
    $right = 0
    $top = $Bitmap.Height
    $bottom = 0
    $sampleStep = 4
    $found = $false

    for ($y = 0; $y -lt $Bitmap.Height; $y += $sampleStep) {
        for ($x = 0; $x -lt $Bitmap.Width; $x += $sampleStep) {
            if (-not (Test-IsContentPixel -Color $Bitmap.GetPixel($x, $y))) {
                continue
            }

            $found = $true
            if ($x -lt $left) { $left = $x }
            if ($x -gt $right) { $right = $x }
            if ($y -lt $top) { $top = $y }
            if ($y -gt $bottom) { $bottom = $y }
        }
    }

    if (-not $found) {
        return @{
            Left   = 0
            Top    = 0
            Right  = $Bitmap.Width - 1
            Bottom = $Bitmap.Height - 1
            Width  = $Bitmap.Width
            Height = $Bitmap.Height
        }
    }

    $pad = 10
    $left = [Math]::Max(0, $left - $pad)
    $top = [Math]::Max(0, $top - $pad)
    $right = [Math]::Min($Bitmap.Width - 1, $right + $pad)
    $bottom = [Math]::Min($Bitmap.Height - 1, $bottom + $pad)

    return @{
        Left   = $left
        Top    = $top
        Right  = $right
        Bottom = $bottom
        Width  = ($right - $left + 1)
        Height = ($bottom - $top + 1)
    }
}

function New-RoundedRectPath {
    param(
        [single]$X,
        [single]$Y,
        [single]$Width,
        [single]$Height,
        [single]$Radius
    )

    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $diameter = $Radius * 2
    $path.AddArc($X, $Y, $diameter, $diameter, 180, 90)
    $path.AddArc($X + $Width - $diameter, $Y, $diameter, $diameter, 270, 90)
    $path.AddArc($X + $Width - $diameter, $Y + $Height - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($X, $Y + $Height - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function Export-MarketingScreenshot {
    param(
        [string]$SourcePath,
        [string]$DestPath,
        [string]$Title,
        [string]$Subtitle
    )

    $loaded = [System.Drawing.Image]::FromFile($SourcePath)
    $sourceBitmap = New-Object System.Drawing.Bitmap $loaded
    $loaded.Dispose()
    $cropped = $null

    try {
        $trim = Get-ContentTrimBounds -Bitmap $sourceBitmap
        $cropped = New-Object System.Drawing.Bitmap $trim.Width, $trim.Height
        $cropGraphics = [System.Drawing.Graphics]::FromImage($cropped)
        $sourceRect = New-Object System.Drawing.Rectangle $trim.Left, $trim.Top, $trim.Width, $trim.Height
        $cropGraphics.DrawImage($sourceBitmap, 0, 0, $sourceRect, [System.Drawing.GraphicsUnit]::Pixel)
        $cropGraphics.Dispose()

        $canvasWidth = 1920
        $canvasHeight = 1080
        $bitmap = New-Object System.Drawing.Bitmap $canvasWidth, $canvasHeight
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit

        $background = [System.Drawing.Color]::FromArgb(255, 20, 23, 31)
        $graphics.Clear($background)

        $accent = [System.Drawing.Color]::FromArgb(255, 0, 120, 214)
        $graphics.FillRectangle((New-Object System.Drawing.SolidBrush $accent), 0, 0, $canvasWidth, 4)

        $titleFont = [System.Drawing.Font]::new('Segoe UI', 48, [System.Drawing.FontStyle]::Bold)
        $subtitleFont = [System.Drawing.Font]::new('Segoe UI', 24, [System.Drawing.FontStyle]::Regular)
        $subtitleBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 191, 199, 209))

        $titleX = 80.0
        $titleY = 48.0
        $textWidth = $canvasWidth - 160
        $graphics.DrawString($Title, $titleFont, [System.Drawing.Brushes]::White, $titleX, $titleY)
        $subtitleY = $titleY + 88.0
        $graphics.DrawString($Subtitle, $subtitleFont, $subtitleBrush, $titleX, $subtitleY)

        $subtitleSize = $graphics.MeasureString($Subtitle, $subtitleFont, $textWidth)
        $headerBottom = $subtitleY + $subtitleSize.Height + 32.0

        $shotX = 120.0
        $shotWidth = $canvasWidth - 240.0
        $shotY = [Math]::Max(180.0, $headerBottom)
        $shotHeight = $canvasHeight - $shotY - 40.0

        $scale = [Math]::Min($shotWidth / $cropped.Width, $shotHeight / $cropped.Height)
        $drawWidth = [int]($cropped.Width * $scale)
        $drawHeight = [int]($cropped.Height * $scale)
        $drawX = $shotX + [int](($shotWidth - $drawWidth) / 2.0)
        $drawY = $shotY + [int](($shotHeight - $drawHeight) / 2.0)
        $radius = 12.0

        $clip = New-RoundedRectPath -X $drawX -Y $drawY -Width $drawWidth -Height $drawHeight -Radius $radius
        $graphics.SetClip($clip)
        $graphics.DrawImage($cropped, $drawX, $drawY, $drawWidth, $drawHeight)
        $graphics.ResetClip()

        $outline = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(40, 255, 255, 255)), 1
        $graphics.DrawPath($outline, $clip)
        $outline.Dispose()
        $clip.Dispose()

        $destDirectory = Split-Path -Parent $DestPath
        if (-not (Test-Path $destDirectory)) {
            New-Item -ItemType Directory -Force -Path $destDirectory | Out-Null
        }

        $bitmap.Save($DestPath, [System.Drawing.Imaging.ImageFormat]::Png)

        $titleFont.Dispose()
        $subtitleFont.Dispose()
        $subtitleBrush.Dispose()
        $graphics.Dispose()
        $bitmap.Dispose()

        return @{
            TrimLeft   = $trim.Left
            TrimTop    = $trim.Top
            TrimRight  = $trim.Right
            TrimBottom = $trim.Bottom
            DrawSize   = ('{0}x{1}' -f $drawWidth, $drawHeight)
        }
    }
    finally {
        $sourceBitmap.Dispose()
        if ($null -ne $cropped) {
            $cropped.Dispose()
        }
    }
}

$shots = @(
    @{
        Raw      = 'raw_1.png'
        Out      = 'Screenshot_1.png'
        Title    = 'Browse shortcuts'
        Subtitle = 'Search your shortcuts and use the menu to run, edit, or favorite them'
    },
    @{
        Raw      = 'raw_2.png'
        Out      = 'Screenshot_2.png'
        Title    = 'Edit in place'
        Subtitle = 'Update a shortcut''s folder, command, terminal profile, and admin option'
    },
    @{
        Raw      = 'raw_3.png'
        Out      = 'Screenshot_3.png'
        Title    = 'Settings'
        Subtitle = 'Choose your default terminal and export or import your shortcuts'
    }
)

foreach ($shot in $shots) {
    $sourcePath = Join-Path $RawDir $shot.Raw
    $destPath = Join-Path $AssetsDir $shot.Out
    if (-not (Test-Path $sourcePath)) {
        throw "Missing raw capture: $sourcePath"
    }

    $info = Export-MarketingScreenshot `
        -SourcePath $sourcePath `
        -DestPath $destPath `
        -Title $shot.Title `
        -Subtitle $shot.Subtitle

    Write-Host ("{0}: trimmed box ({1},{2})-({3},{4}), app draw {5}" -f $shot.Out, $info.TrimLeft, $info.TrimTop, $info.TrimRight, $info.TrimBottom, $info.DrawSize)
}

foreach ($name in @('Screenshot_1.png', 'Screenshot_2.png', 'Screenshot_3.png')) {
    $path = Join-Path $AssetsDir $name
    $image = [System.Drawing.Image]::FromFile($path)
    Write-Host ("{0}: {1}x{2}, {3} bytes" -f $name, $image.Width, $image.Height, (Get-Item $path).Length)
    $image.Dispose()
}
