Add-Type -AssemblyName System.Drawing

$srcDir = 'D:\Dev\QuickShell\QuickShell\Assets'
$dstDir = 'D:\Dev\QuickShell\QuickShell.Raycast\metadata'
New-Item -ItemType Directory -Force -Path $dstDir | Out-Null

# Order for Store carousel: list first, then create, then edit/companion fields.
$map = @(
  @{ Src = 'Screenshot_Raycast_3.png'; Dst = 'quickshell-1.png' }, # Open Workspace list
  @{ Src = 'Screenshot_Raycast_1.png'; Dst = 'quickshell-2.png' }, # Create Workspace
  @{ Src = 'Screenshot_Raycast_2.png'; Dst = 'quickshell-3.png' }  # Edit / companion + URLs
)

$targetW = 2000
$targetH = 1250
$bg = [System.Drawing.Color]::FromArgb(255, 28, 28, 30)

foreach ($item in $map) {
  $srcPath = Join-Path $srcDir $item.Src
  $dstPath = Join-Path $dstDir $item.Dst
  $src = [System.Drawing.Image]::FromFile($srcPath)
  $canvas = New-Object System.Drawing.Bitmap $targetW, $targetH
  $g = [System.Drawing.Graphics]::FromImage($canvas)
  $g.Clear($bg)
  $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
  $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
  $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

  $scale = [Math]::Min($targetW / $src.Width, $targetH / $src.Height)
  $w = [int]([Math]::Round($src.Width * $scale))
  $h = [int]([Math]::Round($src.Height * $scale))
  $x = [int](($targetW - $w) / 2)
  $y = [int](($targetH - $h) / 2)
  $g.DrawImage($src, $x, $y, $w, $h)

  $canvas.Save($dstPath, [System.Drawing.Imaging.ImageFormat]::Png)
  $g.Dispose(); $canvas.Dispose(); $src.Dispose()
  $len = (Get-Item $dstPath).Length
  Write-Host ("{0} <- {1} ({2:N0} KB)" -f $item.Dst, $item.Src, ($len / 1KB))
}
