# Submit Quick Shell to the Command Palette Extension Gallery.
# Requires: gh auth login, fork of microsoft/CmdPal-Extensions
param(
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$source = Join-Path $repoRoot 'cmdpal-gallery\extensions\tonythethompson\quickshell'
$workDir = Join-Path $env:TEMP 'CmdPal-Extensions-quickshell'
$upstream = 'microsoft/CmdPal-Extensions'
$branch = 'add-tonythethompson-quickshell'

if (-not (Test-Path $source)) {
    throw "Missing gallery source at $source"
}

if ($DryRun) {
    Write-Host "Would fork $upstream, copy $source, and open PR on branch $branch"
    exit 0
}

if (-not (gh auth status 2>$null)) {
    throw 'gh is not authenticated. Run: gh auth login'
}

if (Test-Path $workDir) { Remove-Item -Recurse -Force $workDir }
New-Item -ItemType Directory -Path $workDir | Out-Null

$login = gh api user --jq .login
$forkRepo = "$login/CmdPal-Extensions"
$forkExists = $false
try {
    gh repo view $forkRepo 1>$null 2>$null
    if ($LASTEXITCODE -eq 0) { $forkExists = $true }
} catch { }

if (-not $forkExists) {
    Write-Host "Forking $upstream..."
    gh repo fork $upstream --clone=false | Out-Null
}

Write-Host "Using fork: $forkRepo"
gh repo clone $forkRepo $workDir -- --depth=1
Push-Location $workDir
git checkout -b $branch
$dest = Join-Path $workDir 'extensions\tonythethompson\quickshell'
New-Item -ItemType Directory -Force -Path $dest | Out-Null
Copy-Item -Recurse -Force (Join-Path $source '*') $dest
git add extensions/tonythethompson/quickshell
git commit -m "Add tonythethompson.quickshell to gallery"
git push -u origin $branch
$prUrl = gh pr create --repo $upstream --head "${forkRepo.Split('/')[0]}:$branch" --title 'Add tonythethompson.quickshell to gallery' --body @"
## Summary
Adds **Quick Shell** to the Command Palette Extension Gallery.

- Microsoft Store: [9PC8S6LNRT3R](https://apps.microsoft.com/detail/9PC8S6LNRT3R)
- WinGet: \`tonythethompson.QuickShell\`
- Source: https://github.com/tonythethompson/QuickShell

## Test plan
- [ ] CI schema validation passes
- [ ] Store product ID resolves
- [ ] Icon under 100 KB
"@
Pop-Location
Write-Host "PR opened: $prUrl"
