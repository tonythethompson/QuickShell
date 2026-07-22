Add-Type -AssemblyName System.Drawing

$srcDir = 'D:\Dev\QuickShell\QuickShell\Assets'
$dstDir = 'D:\Dev\QuickShell\QuickShell.Raycast\metadata'
New-Item -ItemType Directory -Force -Path $dstDir | Out-Null

$map = @(
  @{ Src = 'Screenshot_Raycast_3.png'; Dst = 'quickshell-1.png' },
  @{ Src = 'Screenshot_Raycast_1.png'; Dst = 'quickshell-2.png' },
  @{ Src = 'Screenshot_Raycast_2.png'; Dst = 'quickshell-3.png' }
)

$targetW = 2000
$targetH = 1250
$pad = 0.125
$contentW = [int]([Math]::Round($targetW * (1 - 2 * $pad)))
$contentH = [int]([Math]::Round($targetH * (1 - 2 * $pad)))
# Contrasting wallpaper-like fill (not Raycast dark chrome).
$bg = [System.Drawing.Color]::FromArgb(255, 58, 78, 168)
$border = [System.Drawing.Color]::FromArgb(255, 18, 18, 20)
$borderPx = 3

foreach ($item in $map) {
  $srcPath = Join-Path $srcDir $item.Src
  $dstPath = Join-Path $dstDir $item.Dst
  $src = [System.Drawing.Image]::FromFile($srcPath)

  $scale = [Math]::Min($contentW / $src.Width, $contentH / $src.Height)
  $w = [int]([Math]::Round($src.Width * $scale))
  $h = [int]([Math]::Round($src.Height * $scale))
  # Keep outer box (border included) centered at the target padding.
  $boxW = $w + 2 * $borderPx
  $boxH = $h + 2 * $borderPx
  $x = [int](($targetW - $boxW) / 2)
  $y = [int](($targetH - $boxH) / 2)

  $canvas = New-Object System.Drawing.Bitmap $targetW, $targetH
  $g = [System.Drawing.Graphics]::FromImage($canvas)
  $g.Clear($bg)
  # Hard edges help Raycast CI's gradient window detector.
  $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
  $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
  $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::None

  $g.FillRectangle((New-Object System.Drawing.SolidBrush $border), $x, $y, $boxW, $boxH)
  $g.DrawImage($src, $x + $borderPx, $y + $borderPx, $w, $h)

  $canvas.Save($dstPath, [System.Drawing.Imaging.ImageFormat]::Png)
  $g.Dispose(); $canvas.Dispose(); $src.Dispose()

  $padL = [math]::Round(100.0 * $x / $targetW, 1)
  $padR = [math]::Round(100.0 * ($targetW - $x - $boxW) / $targetW, 1)
  $padT = [math]::Round(100.0 * $y / $targetH, 1)
  $padB = [math]::Round(100.0 * ($targetH - $y - $boxH) / $targetH, 1)
  Write-Host ("{0}: box {1}x{2} pad L{3}% R{4}% T{5}% B{6}%" -f $item.Dst, $boxW, $boxH, $padL, $padR, $padT, $padB)
}
