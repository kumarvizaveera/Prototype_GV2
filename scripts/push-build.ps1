# GV2 - Push Server Build to GitHub (triggers auto-deploy to VPS)
#
# Usage: .\push-build.ps1
#   or:  .\push-build.ps1 -BuildPath "C:\path\to\ServerBuild"
#
# Requirements: git, SSH key set up for GitHub

param(
    [string]$BuildPath = ""
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
        Write-Host "  Where is your server build folder?" -ForegroundColor Yellow
        $BuildPath = Read-Host "  Path"
        $BuildPath = $BuildPath.Trim('"').Trim("'")
    }
}

if (-not (Test-Path $BuildPath)) {
    Write-Host "ERROR: Build path not found: $BuildPath" -ForegroundColor Red
    exit 1
}

# Verify there are actual build files
$fileCount = (Get-ChildItem $BuildPath -File).Count
if ($fileCount -eq 0) {
    Write-Host "ERROR: ServerBuild folder is empty. Build in Unity first." -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  GV2 - Push Server Build" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Build: $BuildPath ($fileCount files)"
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# --- Use a temporary clone to avoid messing with working repo ---
$tempDir = "$env:TEMP\gv2-server-deploy-$(Get-Random)"
$repoUrl = git -C $PSScriptRoot remote get-url origin 2>$null

if (-not $repoUrl) {
    Write-Host "ERROR: Could not get git remote URL" -ForegroundColor Red
    exit 1
}

Write-Host "[1/4] Preparing deploy branch..." -ForegroundColor Yellow
# Shallow clone just enough to push
git clone --depth 1 --no-checkout $repoUrl $tempDir 2>$null
if ($LASTEXITCODE -ne 0) {
    # If clone fails, try init + remote approach
    New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
    Push-Location $tempDir
    git init
    git remote add origin $repoUrl
    Pop-Location
}

Push-Location $tempDir

# Create orphan branch (no history = small pushes)
git checkout --orphan server-build 2>$null
git rm -rf . 2>$null

Write-Host "[2/4] Copying build files..." -ForegroundColor Yellow
# Copy build files
Copy-Item -Path "$BuildPath\*" -Destination $tempDir -Recurse -Force

# Copy GitHub Actions workflow
$workflowSrc = "$PSScriptRoot\.github"
if (Test-Path $workflowSrc) {
    Copy-Item -Path $workflowSrc -Destination "$tempDir\.github" -Recurse -Force
}

Write-Host "[3/4] Pushing to GitHub..." -ForegroundColor Yellow
git add -A
$timestamp = Get-Date -Format "yyyy-MM-dd HH:mm"
git commit -m "Server build $timestamp"
git push origin server-build --force

$pushResult = $LASTEXITCODE

Pop-Location

Write-Host "[4/4] Cleaning up..." -ForegroundColor Yellow
Remove-Item -Path $tempDir -Recurse -Force -ErrorAction SilentlyContinue

if ($pushResult -eq 0) {
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Green
    Write-Host "  Build pushed! GitHub Actions is now" -ForegroundColor Green
    Write-Host "  deploying to your VPS automatically." -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Green
    Write-Host ""
} else {
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Red
    Write-Host "  Push failed. Check your git credentials." -ForegroundColor Red
    Write-Host "========================================" -ForegroundColor Red
    Write-Host ""
    exit 1
}
