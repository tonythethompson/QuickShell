param(
    [Parameter(Mandatory = $true)]
    [string]$Directory,

    [string[]]$Used = @(),

    [int]$Generation = 0,

    [string]$Configuration = "Release",

    [string]$Platform = "x64"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "QuickShell.Suggest\QuickShell.Suggest.csproj"

$dotnetArgs = @(
    "run",
    "--project", $project,
    "-c", $Configuration,
    "-p:Platform=$Platform",
    "--",
    "suggest",
    "--dir", $Directory,
    "--generation", $Generation
)

foreach ($item in $Used) {
    if (-not [string]::IsNullOrWhiteSpace($item)) {
        $dotnetArgs += @("--used", $item)
    }
}

dotnet @dotnetArgs
