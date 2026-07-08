# Quick Shell Run plugin icons (>_ + bolt, blue prompt, transparent bg).
# Deploys 50px dark/light PNGs to QuickShell.Run/Images/.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$runDir = $PSScriptRoot
$assetsDir = Split-Path $runDir -Parent
$repoRoot = Split-Path (Split-Path $assetsDir -Parent) -Parent
$runPluginImages = Join-Path $repoRoot 'QuickShell.Run\Images'

$themes = @(
  @{ Name = 'dark'; Master = Join-Path $runDir 'quickshell-run.dark.svg' },
  @{ Name = 'light'; Master = Join-Path $runDir 'quickshell-run.light.svg' }
)

function Export-Icon {
  param(
    [Parameter(Mandatory)] [string] $SvgPath,
    [Parameter(Mandatory)] [string] $OutPath,
    [Parameter(Mandatory)] [int] $Width,
    [ValidateSet(0, 1, 2)]
    [int] $ShapeRendering = 2,
    [ValidateSet(0, 1)]
    [int] $ImageRendering = 0
  )
  if (-not (Test-Path $SvgPath)) { throw "Missing source: $SvgPath" }
  npx --yes @resvg/resvg-js-cli $SvgPath $OutPath --fit-width $Width --shape-rendering $ShapeRendering --image-rendering $ImageRendering | Out-Null
  if ($LASTEXITCODE -ne 0) { throw "Export failed: $OutPath" }
}

function Downscale-Png {
  param(
    [Parameter(Mandatory)] [string] $SrcPath,
    [Parameter(Mandatory)] [string] $DstPath,
    [Parameter(Mandatory)] [int] $Size
  )
  $img = [System.Drawing.Image]::FromFile($SrcPath)
  try {
    $bmp = New-Object System.Drawing.Bitmap $Size, $Size
    try {
      $g = [System.Drawing.Graphics]::FromImage($bmp)
      try {
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $g.DrawImage($img, 0, 0, $Size, $Size)
      }
      finally { $g.Dispose() }
      $bmp.Save($DstPath, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally { $bmp.Dispose() }
  }
  finally { $img.Dispose() }
}

function Export-Supersampled {
  param(
    [Parameter(Mandatory)] [string] $SvgPath,
    [Parameter(Mandatory)] [string] $OutPath,
    [Parameter(Mandatory)] [int] $RenderWidth,
    [Parameter(Mandatory)] [int] $OutputSize
  )
  $temp = Join-Path $env:TEMP "quickshell-run-$([guid]::NewGuid().ToString('N')).png"
  try {
    Export-Icon -SvgPath $SvgPath -OutPath $temp -Width $RenderWidth -ShapeRendering 2 -ImageRendering 0
    Downscale-Png -SrcPath $temp -DstPath $OutPath -Size $OutputSize
  }
  finally {
    if (Test-Path $temp) { Remove-Item -Force $temp }
  }
}

New-Item -ItemType Directory -Force -Path $runPluginImages | Out-Null

foreach ($theme in $themes) {
  $outPath = Join-Path $runPluginImages "quickshell.$($theme.Name).png"
  Export-Supersampled -SvgPath $theme.Master -OutPath $outPath -RenderWidth 100 -OutputSize 50
}

Write-Host 'Quick Shell Run icon exports complete:'
Write-Host "  Plugin deploy: $runPluginImages (50 px, 100 px supersample)"
