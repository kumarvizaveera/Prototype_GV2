# GV2 — Push Server Build to GitHub (triggers auto-deploy)
#
# Usage:
#   .\push-build.ps1 -BuildPath "C:\path\to\your\ServerBuild"
#
# What happens:
#   1. Copies your Unity Linux build into the repo
#   2. Pushes to the 'server-build' branch
#   3. GitHub Actions automatically deploys to VPS
#   4. You switch back to your working branch
#
# The whole deploy takes ~2-3 minutes hands-free after you run this.

param(
    [string]$BuildPath = "",
    [string]$BuildFolder = "ServerBuild"  # Folder inside repo to store build files
)

# --- Find build path ---
if (-not $BuildPath) {
    $candidates = @(
        "$PSScriptRoot\ServerBuild",
        "$PSScriptRoot\Build\Server",
        "$PSScriptRoot\Builds\LinuxServer",
        "$env:USERPROFILE\Desktop\GV2ServerBuild"
    )
    foreach ($c in $candidates) {
        if (Test-Path $c) {
            $BuildPath = $c
            break
        }
    }
    if (-not $BuildPath) {
        Write-Host ""
        Write-Host "  Where is your server build folder?" -ForegroundColor Yellow
        $BuildPath = Read-Host "  Path"
        $BuildPath = $BuildPath.Trim('"').Trim("'")
    }
}

if (-not (Test-Path $BuildPath)) {
    Write-Host "ERROR: Build path not found: $BuildPath" -ForegroundColor Red
    exit 1
}

$repoRoot = $PSScriptRoot

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  GV2 — Push Server Build" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Build: $BuildPath"
Write-Host "  This will push to 'server-build' branch"
Write-Host "  GitHub Actions will auto-deploy to VPS"
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Remember current branch
$currentBranch = git -C $repoRoot rev-parse --abbrev-ref HEAD 2>$null
if (-not $currentBranch) {
    Write-Host "ERROR: Not a git repository" -ForegroundColor Red
    exit 1
}

Write-Host "[1/5] Saving current work..." -ForegroundColor Yellow
git -C $repoRoot stash push -m "auto-stash before server deploy" 2>$null

Write-Host "[2/5] Switching to server-build branch..." -ForegroundColor Yellow
$branchExists = git -C $repoRoot branch --list "server-build" 2>$null
if ($branchExists) {
    git -C $repoRoot checkout server-build
} else {
    git -C $repoRoot checkout --orphan server-build
    git -C $repoRoot rm -rf . 2>$null
}

Write-Host "[3/5] Copying build files..." -ForegroundColor Yellow
# Clean old build files (but keep .git and .github)
Get-ChildItem $repoRoot -Exclude @('.git', '.github', '.gitignore') |
    Where-Object { $_.Name -ne '.git' } |
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

# Copy new build
Copy-Item -Path "$BuildPath\*" -Destination $repoRoot -Recurse -Force

Write-Host "[4/5] Pushing to GitHub..." -ForegroundColor Yellow
git -C $repoRoot add -A
$timestamp = Get-Date -Format "yyyy-MM-dd HH:mm"
git -C $repoRoot commit -m "Server build $timestamp"
git -C $repoRoot push -u origin server-build --force

Write-Host "[5/5] Switching back to $currentBranch..." -ForegroundColor Yellow
git -C $repoRoot checkout $currentBranch
git -C $repoRoot stash pop 2>$null

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "  Build pushed! GitHub Actions is now" -ForegroundColor Green
Write-Host "  deploying to your VPS automatically." -ForegroundColor Green
Write-Host "" -ForegroundColor Green
Write-Host "  Monitor: https://github.com/YOUR_REPO/actions" -ForegroundColor DarkGray
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
