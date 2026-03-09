# GV2 Dedicated Server — One-Command Deploy Script
# Usage: Right-click → Run with PowerShell, or from terminal: .\deploy.ps1
#        .\deploy.ps1 -Logs          — deploy and then tail server logs (Ctrl+C to stop)
#
# FIRST-TIME SETUP (run once to eliminate password prompts forever):
#   1. Open PowerShell and run:  ssh-keygen -t ed25519
#      (press Enter for all defaults — no passphrase needed)
#   2. Then run:  type $env:USERPROFILE\.ssh\id_ed25519.pub | ssh root@187.124.96.178 "mkdir -p ~/.ssh && cat >> ~/.ssh/authorized_keys"
#      (enter your password one last time)
#   3. After that, this script runs with ZERO password prompts.

param(
    [string]$BuildPath = "",
    [string]$VPS = "187.124.96.178",
    [string]$User = "root",
    [string]$RemotePath = "/opt/gv2-server",
    [string]$ServiceName = "gv2-server",
    [switch]$Logs
)

# --- Auto-detect build path if not specified ---
if (-not $BuildPath) {
    # Look for common build locations
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
        Write-Host "  Could not auto-detect build folder." -ForegroundColor Yellow
        Write-Host "  Please drag your build folder here, or type the path:" -ForegroundColor Yellow
        Write-Host ""
        $BuildPath = Read-Host "  Build folder path"
        $BuildPath = $BuildPath.Trim('"').Trim("'")
    }
}

if (-not (Test-Path $BuildPath)) {
    Write-Host "ERROR: Build path not found: $BuildPath" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  GV2 Server Deploy" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Build:  $BuildPath"
Write-Host "  VPS:    $User@$VPS"
Write-Host "  Remote: $RemotePath"
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# --- Step 1: Compress build into a tarball (much faster than SCP of many files) ---
Write-Host "[1/4] Compressing build..." -ForegroundColor Yellow
$tarFile = "$env:TEMP\gv2-server-build.tar.gz"
if (Test-Path $tarFile) { Remove-Item $tarFile -Force }

# Use tar (built into Windows 10+)
Push-Location $BuildPath
tar -czf $tarFile -C $BuildPath .
Pop-Location

if (-not (Test-Path $tarFile)) {
    Write-Host "ERROR: Failed to create tarball" -ForegroundColor Red
    exit 1
}

$sizeMB = [math]::Round((Get-Item $tarFile).Length / 1MB, 1)
Write-Host "  Done! ($sizeMB MB)" -ForegroundColor Green

# --- Step 2: Upload tarball (single file = single SCP connection) ---
Write-Host "[2/4] Uploading to VPS..." -ForegroundColor Yellow
scp -o ConnectTimeout=10 $tarFile "${User}@${VPS}:/tmp/gv2-server-build.tar.gz"
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Upload failed. Check VPS connection." -ForegroundColor Red
    exit 1
}
Write-Host "  Done!" -ForegroundColor Green

# --- Step 3: Deploy on VPS (single SSH connection does everything) ---
Write-Host "[3/4] Deploying on VPS..." -ForegroundColor Yellow
$deployScript = @"
set -e
echo '  Stopping server...'
systemctl stop $ServiceName 2>/dev/null || true
echo '  Cleaning old files...'
rm -rf $RemotePath/*
echo '  Extracting new build...'
tar -xzf /tmp/gv2-server-build.tar.gz -C $RemotePath
echo '  Setting permissions...'
chmod +x $RemotePath/Prototype_GV2 2>/dev/null || chmod +x $RemotePath/*.x86_64 2>/dev/null || true
echo '  Starting server...'
systemctl start $ServiceName
sleep 2
echo '  Checking status...'
systemctl is-active $ServiceName
rm -f /tmp/gv2-server-build.tar.gz
echo '  DEPLOY COMPLETE'
"@

ssh -o ConnectTimeout=10 "${User}@${VPS}" $deployScript
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Deploy commands failed on VPS." -ForegroundColor Red
    exit 1
}
Write-Host "  Done!" -ForegroundColor Green

# --- Step 4: Quick health check ---
Write-Host "[4/4] Verifying server..." -ForegroundColor Yellow
$status = ssh -o ConnectTimeout=10 "${User}@${VPS}" "systemctl is-active $ServiceName"
if ($status -eq "active") {
    Write-Host "  Server is RUNNING!" -ForegroundColor Green
} else {
    Write-Host "  WARNING: Server status is '$status'" -ForegroundColor Yellow
    Write-Host "  Check logs with: ssh ${User}@${VPS} 'tail -50 /var/log/gv2-server.log'" -ForegroundColor Yellow
}

# Cleanup
Remove-Item $tarFile -Force -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "  Deploy complete!" -ForegroundColor Green
if (-not $Logs) {
    Write-Host "  View logs: .\deploy.ps1 -Logs  (or ssh ${User}@${VPS} 'tail -f /var/log/gv2-server.log')" -ForegroundColor DarkGray
}
Write-Host "========================================" -ForegroundColor Green
Write-Host ""

if ($Logs) {
    Write-Host "  Tailing server logs (Ctrl+C to stop)..." -ForegroundColor Cyan
    Write-Host ""
    ssh -o ConnectTimeout=10 "${User}@${VPS}" "tail -f /var/log/gv2-server.log"
}
