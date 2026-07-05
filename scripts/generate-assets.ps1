#Requires -Version 5.1

# Generates MSIX + store listing from logo.svg; CmdPal/small tiles from logo-micro.svg.

$ErrorActionPreference = 'Stop'



$repoRoot = Split-Path -Parent $PSScriptRoot

$assetsDir = Join-Path $repoRoot 'QuickShell\Assets'

$logoSvg = Join-Path $repoRoot 'logo.svg'

$logoMicroSvg = Join-Path $assetsDir 'logo-micro.svg'

$runImagesDir = Join-Path $repoRoot 'QuickShell.Run\Images'

$generatorProject = Join-Path $PSScriptRoot 'LogoAssetGenerator\LogoAssetGenerator.csproj'



if (-not (Test-Path $logoSvg)) {

    throw "Missing logo source: $logoSvg"

}



if (-not (Test-Path $logoMicroSvg)) {

    throw "Missing micro logo source: $logoMicroSvg"

}



New-Item -ItemType Directory -Force -Path $assetsDir | Out-Null

New-Item -ItemType Directory -Force -Path $runImagesDir | Out-Null



dotnet build $generatorProject --no-incremental | Out-Null

if ($LASTEXITCODE -ne 0) {

    throw "LogoAssetGenerator build failed with exit code $LASTEXITCODE"

}



dotnet run --project $generatorProject --no-build -- $logoMicroSvg $assetsDir

if ($LASTEXITCODE -ne 0) {

    throw "LogoAssetGenerator failed with exit code $LASTEXITCODE"

}



# PowerToys Run plugin icons: monochrome outlines (not the full-color MSIX logo).

$runDarkSvg = Join-Path $assetsDir 'logo-run.dark.svg'

$runLightSvg = Join-Path $assetsDir 'logo-run.light.svg'



if (-not (Test-Path $runDarkSvg)) {

    throw "Missing Run dark icon source: $runDarkSvg"

}



if (-not (Test-Path $runLightSvg)) {

    throw "Missing Run light icon source: $runLightSvg"

}



dotnet run --project $generatorProject --no-build -- --render $runDarkSvg (Join-Path $runImagesDir 'quickshell.dark.png') 50 50

if ($LASTEXITCODE -ne 0) {

    throw "LogoAssetGenerator Run dark icon failed with exit code $LASTEXITCODE"

}



dotnet run --project $generatorProject --no-build -- --render $runLightSvg (Join-Path $runImagesDir 'quickshell.light.png') 50 50

if ($LASTEXITCODE -ne 0) {

    throw "LogoAssetGenerator Run light icon failed with exit code $LASTEXITCODE"

}



$cmdPalIcon = Join-Path $repoRoot 'cmdpal-gallery\extensions\tonythethompson\quickshell\icon.png'

$appTile300 = Join-Path $assetsDir 'StoreListing\AppTile_300x300.png'

if (-not (Test-Path $appTile300)) {

    throw "Missing store listing icon source: $appTile300"

}



New-Item -ItemType Directory -Force -Path (Split-Path $cmdPalIcon -Parent) | Out-Null

Copy-Item -Force $appTile300 $cmdPalIcon



Write-Host "Quick Shell assets generated:"

Write-Host "  Full logo:  $logoSvg (150+, store listing)"

Write-Host "  Micro logo: $logoMicroSvg (StoreLogo, 44px, CmdPal)"

Write-Host "  MSIX:       $assetsDir"

Write-Host "  Run:        $runImagesDir"
