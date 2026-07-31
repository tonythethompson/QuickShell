# Submit or update Quick Shell in the Command Palette Extension Gallery.
# Requires: gh auth login, fork of microsoft/CmdPal-Extensions
#
# Always creates a fresh branch from upstream main and a normal push (no force).
param(
    [switch]$DryRun,
    [string]$Branch = '',
    [string]$Title = 'Update tonythethompson.quickshell gallery listing',
    [string]$Body = ''
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$source = Join-Path $repoRoot 'cmdpal-gallery\extensions\tonythethompson\quickshell'
$workDir = Join-Path $env:TEMP 'CmdPal-Extensions-quickshell'
$upstream = 'microsoft/CmdPal-Extensions'

if (-not (Test-Path $source)) {
    throw "Missing gallery source at $source"
}

if ([string]::IsNullOrWhiteSpace($Branch)) {
    $Branch = 'update-tonythethompson-quickshell-' + (Get-Date -Format 'yyyyMMdd-HHmmss')
}

if ($DryRun) {
    Write-Host "Would sync fork $upstream, copy $source, push branch $Branch (no force), and open a PR"
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
# Sync the fork's default branch from upstream (not a branch force-push).
gh repo sync $forkRepo --source $upstream --force 2>$null
gh repo clone $forkRepo $workDir -- --depth=1
Push-Location $workDir
try {
    git checkout -b $Branch
    $dest = Join-Path $workDir 'extensions\tonythethompson\quickshell'
    New-Item -ItemType Directory -Force -Path $dest | Out-Null
    # Replace listing contents so renamed/removed screenshots do not linger.
    Get-ChildItem -Force $dest | Remove-Item -Recurse -Force
    Copy-Item -Recurse -Force (Join-Path $source '*') $dest
    git add -A extensions/tonythethompson/quickshell
    git status --short

    if (-not (git status --porcelain)) {
        throw 'No gallery listing changes to commit.'
    }

    git commit -m $Title
    git push -u origin $Branch
    if ($LASTEXITCODE -ne 0) {
        throw "git push failed for branch $Branch (refusing to force-push)."
    }

    if ([string]::IsNullOrWhiteSpace($Body)) {
        $Body = @"
## Summary
Updates the **Quick Shell** Command Palette Extension Gallery listing.

Describe only what this PR changes (for example logo, screenshots, copy, or install sources). Do not reuse stale bullets from a prior submission.

## Test plan
- [ ] CI schema validation passes
- [ ] Store / WinGet install source IDs resolve
- [ ] Icon under 100 KB; screenshots under 1 MB each (when media changed)
- [ ] Tags: at most 5
"@
    }

    $bodyFile = Join-Path $env:TEMP 'cmdpal-gallery-pr-body.md'
    Set-Content -Path $bodyFile -Value $Body -Encoding utf8
    $prUrl = gh pr create --repo $upstream --head "${login}:$Branch" --title $Title --body-file $bodyFile
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($prUrl)) {
        Remove-Item $bodyFile -Force -ErrorAction SilentlyContinue
        throw 'gh pr create failed; no PR was opened.'
    }
    Remove-Item $bodyFile -Force -ErrorAction SilentlyContinue
    Write-Host "PR opened: $prUrl"
}
finally {
    Pop-Location
}
