# Submit or update Quick Shell in the Command Palette Extension Gallery.
# Requires: gh auth login, fork of microsoft/CmdPal-Extensions
param(
    [switch]$DryRun,
    [string]$Branch = 'update-tonythethompson-quickshell'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$source = Join-Path $repoRoot 'cmdpal-gallery\extensions\tonythethompson\quickshell'
$workDir = Join-Path $env:TEMP 'CmdPal-Extensions-quickshell'
$upstream = 'microsoft/CmdPal-Extensions'

if (-not (Test-Path $source)) {
    throw "Missing gallery source at $source"
}

if ($DryRun) {
    Write-Host "Would sync fork $upstream, copy $source, and open PR on branch $Branch"
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
gh repo sync $forkRepo --source $upstream --force 2>$null
gh repo clone $forkRepo $workDir -- --depth=1
Push-Location $workDir
git checkout -b $Branch
$dest = Join-Path $workDir 'extensions\tonythethompson\quickshell'
New-Item -ItemType Directory -Force -Path $dest | Out-Null
# Replace listing contents so renamed/removed screenshots do not linger.
Get-ChildItem -Force $dest | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
Copy-Item -Recurse -Force (Join-Path $source '*') $dest
git add -A extensions/tonythethompson/quickshell
git status --short
git commit -m "Update tonythethompson.quickshell gallery listing"
git push -u origin $Branch --force-with-lease
$bodyFile = Join-Path $env:TEMP 'cmdpal-gallery-pr-body.md'
@'
## Summary
Updates the **Quick Shell** Command Palette Extension Gallery listing.

- New product logo (`icon.png`, Store AppTile)
- Screenshots refreshed for current workspace UI (list, create, settings, detail)
- Description/tags aligned with current product language
- Install sources unchanged: Microsoft Store `9PC8S6LNRT3R`, WinGet `tonythethompson.QuickShell`

## Test plan
- [ ] CI schema validation passes
- [ ] Store product ID resolves
- [ ] Icon under 100 KB; screenshots under 1 MB each
- [ ] Tags ≤ 5
'@ | Set-Content -Path $bodyFile -Encoding utf8
$prUrl = gh pr create --repo $upstream --head "${login}:$Branch" --title 'Update tonythethompson.quickshell gallery listing' --body-file $bodyFile
Remove-Item $bodyFile -Force -ErrorAction SilentlyContinue
Pop-Location
Write-Host "PR opened: $prUrl"
