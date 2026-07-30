param(
    [string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot),

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [ValidateSet('x64')]
    [string]$Platform = 'x64'
)

$ErrorActionPreference = 'Stop'
$projectPath = Join-Path $ProjectRoot 'QuickShell.Suggest\QuickShell.Suggest.csproj'
$raycastRoot = Join-Path $ProjectRoot 'QuickShell.Raycast'
$publishRoot = Join-Path $raycastRoot 'bin\SuggestPublish'
$assetPath = Join-Path $raycastRoot 'assets\QuickShell.Suggest.exe'

if (-not (Test-Path -LiteralPath $projectPath)) {
    throw "QuickShell.Suggest project not found at $projectPath"
}

$dotnetCommand = Get-Command dotnet -ErrorAction Stop
$dotnetRoot = Split-Path -Parent $dotnetCommand.Source
$sdkVersion = (& $dotnetCommand.Source --version).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($sdkVersion)) {
    throw 'Unable to determine the installed .NET SDK version.'
}
$sdkPath = Join-Path $dotnetRoot "sdk\$sdkVersion\Sdks"
if (-not (Test-Path -LiteralPath $sdkPath)) {
    throw "The .NET SDK resolver path was not found at $sdkPath"
}

$previousDotnetRoot = $env:DOTNET_ROOT
$previousMsBuildSdksPath = $env:MSBuildSDKsPath
try {
    # Use the SDK belonging to the selected dotnet host even when the parent
    # shell contains stale Scoop/MSBuild environment variables.
    $env:DOTNET_ROOT = $dotnetRoot
    $env:MSBuildSDKsPath = $sdkPath

    & $dotnetCommand.Source publish $projectPath `
        -c $Configuration `
        -p:Platform=$Platform `
        -r win-x64 `
        --self-contained false `
        -p:PublishSingleFile=true `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -o $publishRoot
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish QuickShell.Suggest failed with exit code $LASTEXITCODE"
    }
}
finally {
    $env:DOTNET_ROOT = $previousDotnetRoot
    $env:MSBuildSDKsPath = $previousMsBuildSdksPath
}

$publishedExecutable = Join-Path $publishRoot 'QuickShell.Suggest.exe'
if (-not (Test-Path -LiteralPath $publishedExecutable)) {
    throw "Published QuickShell.Suggest executable not found at $publishedExecutable"
}

Copy-Item -LiteralPath $publishedExecutable -Destination $assetPath -Force
Write-Host "Published Raycast suggestion CLI: $assetPath" -ForegroundColor Green
