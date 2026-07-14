# Quick Shell MSIX icon exports (sources under QuickShell/Assets/icon):
#   micro-smooth @ 32px -> 16px
#   sharp SVG + crispEdges -> 24-48, 64px
#   flat SVG -> 50px StoreLogo + 128-1024px MSIX ladder + scale-200 variants
#   soft SVG -> marketing 128px
#   filtered SVG -> marketing 256px+
# MSIX packaging PNGs are written to QuickShell/Assets/ (manifest paths).
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$iconDir = $PSScriptRoot
$assetsDir = Split-Path $iconDir -Parent
$svgDir = Join-Path $iconDir 'svg'
$exportDir = Join-Path $iconDir 'export'
$marketingDir = Join-Path $exportDir 'marketing'

$sharpSrc = Join-Path $svgDir 'quickshell-icon-sharp.svg'
$microSmoothSrc = Join-Path $svgDir 'quickshell-icon-micro-smooth.svg'
$flatSrc = Join-Path $svgDir 'quickshell-icon-flat.svg'
$softSrc = Join-Path $svgDir 'quickshell-icon-soft.svg'
$filteredSrc = Join-Path $iconDir 'quickshell-icon.svg'
$wideSrc = Join-Path $svgDir 'quickshell-icon-wide-310x150.svg'

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

function Export-Micro16 {
  param(
    [Parameter(Mandatory)] [string] $SvgPath,
    [Parameter(Mandatory)] [string] $OutPath
  )
  $temp32 = Join-Path $exportDir 'quickshell-icon-16-temp-32.png'
  try {
    Export-Icon -SvgPath $SvgPath -OutPath $temp32 -Width 32 -ShapeRendering 2 -ImageRendering 0
    Downscale-Png -SrcPath $temp32 -DstPath $OutPath -Size 16
  }
  finally {
    if (Test-Path $temp32) { Remove-Item -Force $temp32 }
  }
}

New-Item -ItemType Directory -Force -Path $exportDir, $marketingDir | Out-Null

$sharpSizes = @(24, 32, 44, 48, 64)
$flatSizes = @(128, 150, 256, 512, 620, 1024)

foreach ($s in $sharpSizes) {
  Export-Icon -SvgPath $sharpSrc -OutPath (Join-Path $exportDir "quickshell-icon-$s.png") -Width $s -ShapeRendering 1
}

Export-Micro16 -SvgPath $microSmoothSrc -OutPath (Join-Path $exportDir 'quickshell-icon-16.png')
Export-Icon -SvgPath $flatSrc -OutPath (Join-Path $exportDir 'quickshell-icon-50.png') -Width 50 -ShapeRendering 2

foreach ($s in $flatSizes) {
  Export-Icon -SvgPath $flatSrc -OutPath (Join-Path $exportDir "quickshell-icon-$s.png") -Width $s -ShapeRendering 2
}

Export-Icon -SvgPath $wideSrc -OutPath (Join-Path $exportDir 'quickshell-icon-310x150.png') -Width 310 -ShapeRendering 2

Export-Icon -SvgPath $softSrc -OutPath (Join-Path $marketingDir 'quickshell-icon-128.png') -Width 128 -ShapeRendering 2

$marketingSizes = @(256, 512, 1024)
foreach ($s in $marketingSizes) {
  Export-Icon -SvgPath $filteredSrc -OutPath (Join-Path $marketingDir "quickshell-icon-$s.png") -Width $s -ShapeRendering 2
}

# MSIX manifest assets (Assets root)
Copy-Item -Force (Join-Path $exportDir 'quickshell-icon-50.png') (Join-Path $assetsDir 'StoreLogo.png')
Copy-Item -Force (Join-Path $exportDir 'quickshell-icon-44.png') (Join-Path $assetsDir 'Square44x44Logo.png')
Copy-Item -Force (Join-Path $exportDir 'quickshell-icon-150.png') (Join-Path $assetsDir 'Square150x150Logo.png')
Copy-Item -Force (Join-Path $exportDir 'quickshell-icon-310x150.png') (Join-Path $assetsDir 'Wide310x150Logo.png')
Copy-Item -Force (Join-Path $exportDir 'quickshell-icon-620.png') (Join-Path $assetsDir 'SplashScreen.png')

# scale-200 / altform MSIX assets
Export-Icon -SvgPath $flatSrc -OutPath (Join-Path $assetsDir 'Square44x44Logo.targetsize-24_altform-unplated.png') -Width 24 -ShapeRendering 2
Export-Icon -SvgPath $flatSrc -OutPath (Join-Path $assetsDir 'Square44x44Logo.scale-200.png') -Width 88 -ShapeRendering 2
Export-Icon -SvgPath $flatSrc -OutPath (Join-Path $assetsDir 'LockScreenLogo.scale-200.png') -Width 48 -ShapeRendering 2
Export-Icon -SvgPath $flatSrc -OutPath (Join-Path $assetsDir 'Square150x150Logo.scale-200.png') -Width 300 -ShapeRendering 2
Export-Icon -SvgPath $wideSrc -OutPath (Join-Path $assetsDir 'Wide310x150Logo.scale-200.png') -Width 620 -ShapeRendering 2
Export-Icon -SvgPath $flatSrc -OutPath (Join-Path $assetsDir 'SplashScreen.scale-200.png') -Width 620 -ShapeRendering 2

# Store listing square tiles (Partner Center)
$storeDir = Join-Path $assetsDir 'StoreListing'
New-Item -ItemType Directory -Force -Path $storeDir | Out-Null
Export-Icon -SvgPath $filteredSrc -OutPath (Join-Path $storeDir 'AppTile_300x300.png') -Width 300 -ShapeRendering 2
Export-Icon -SvgPath $filteredSrc -OutPath (Join-Path $storeDir 'AppTile_150x150.png') -Width 150 -ShapeRendering 2
Export-Icon -SvgPath $filteredSrc -OutPath (Join-Path $storeDir 'AppTile_71x71.png') -Width 71 -ShapeRendering 2
Export-Icon -SvgPath $filteredSrc -OutPath (Join-Path $storeDir 'BoxArt_1080x1080.png') -Width 1080 -ShapeRendering 2
Export-Icon -SvgPath $filteredSrc -OutPath (Join-Path $storeDir 'BoxArt_2160x2160.png') -Width 2160 -ShapeRendering 2

# Store listing poster art (Svg.Skia cannot render filtered SVG; prerender with resvg first)
$posterIconPng = Join-Path $storeDir '_poster-icon-source.png'
Export-Icon -SvgPath $filteredSrc -OutPath $posterIconPng -Width 1024 -ShapeRendering 2

$repoRoot = Split-Path (Split-Path $assetsDir -Parent) -Parent
$generatorProject = Join-Path $repoRoot 'scripts\LogoAssetGenerator\LogoAssetGenerator.csproj'
dotnet build $generatorProject -v q | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'LogoAssetGenerator build failed' }
dotnet run --project $generatorProject --no-build -- --posters $posterIconPng $storeDir
if ($LASTEXITCODE -ne 0) { throw 'LogoAssetGenerator poster export failed' }
Remove-Item -Force $posterIconPng -ErrorAction SilentlyContinue

Write-Host 'Quick Shell icon exports complete:'
Write-Host "  Preview:         $exportDir"
Write-Host "  MSIX packaging:  $assetsDir"
Write-Host '  Micro smooth:    16 px (32px AA render, downscaled)'
Write-Host "  Sharp (solid):   24, 32, 44, 48, 64 px"
Write-Host '  Store logo:      50 px (flat resvg, same pipeline as other MSIX sizes)'
Write-Host "  Flat (MSIX):     $($flatSizes -join ', ') px + wide tile"
Write-Host '  Store posters:   PosterArt_720x1080, PosterArt_1440x2160'
