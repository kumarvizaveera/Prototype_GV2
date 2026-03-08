# GV2 Dedicated Server — Deploy to VPS
# Run from PowerShell on Windows
#
# Usage:
#   .\deploy-to-vps.ps1
#   .\deploy-to-vps.ps1 -SkipUpload    # only generate commands, don't SCP
#
# Prerequisites:
#   - OpenSSH client installed (comes with Windows 10/11)
#   - SSH access to VPS (password or key-based)

param(
    [switch]$SkipUpload
)

# ── Configuration ──────────────────────────────────────────────
$VPS_IP       = "187.124.96.178"
$VPS_USER     = "root"
$VPS_DIR      = "/opt/gv2-server"
$BUILD_DIR    = "C:\Users\Veera\Desktop\Unity\Linux Tests\Tests_10"
$EXECUTABLE   = "L_Tests_10"
# ───────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  GV2 Server Deploy to VPS" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Verify build exists
if (-not (Test-Path "$BUILD_DIR\$EXECUTABLE")) {
    Write-Host "ERROR: Build not found at $BUILD_DIR\$EXECUTABLE" -ForegroundColor Red
    Write-Host "Make sure you've built the Linux Dedicated Server in Unity first." -ForegroundColor Yellow
    exit 1
}

# List build files
Write-Host "Build directory: $BUILD_DIR" -ForegroundColor Green
Write-Host "Executable: $EXECUTABLE" -ForegroundColor Green
$buildSize = (Get-ChildItem -Path $BUILD_DIR -Recurse | Measure-Object -Property Length -Sum).Sum / 1MB
Write-Host "Total size: $([math]::Round($buildSize, 1)) MB" -ForegroundColor Green
Write-Host ""

if ($SkipUpload) {
    Write-Host "SkipUpload flag set — printing commands only." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Run these manually:" -ForegroundColor Cyan
    Write-Host "  scp -r `"$BUILD_DIR\*`" ${VPS_USER}@${VPS_IP}:${VPS_DIR}/" -ForegroundColor White
    Write-Host "  ssh ${VPS_USER}@${VPS_IP} `"chmod +x ${VPS_DIR}/${EXECUTABLE}`"" -ForegroundColor White
    Write-Host "  ssh ${VPS_USER}@${VPS_IP} `"systemctl restart gv2-server`"" -ForegroundColor White
    exit 0
}

# Step 1: Stop existing server
Write-Host "[1/4] Stopping existing server..." -ForegroundColor Yellow
ssh ${VPS_USER}@${VPS_IP} "systemctl stop gv2-server 2>/dev/null; screen -S gv2 -X quit 2>/dev/null; echo 'Server stopped'"
Write-Host ""

# Step 2: Clean old build and upload new one
Write-Host "[2/4] Uploading build to VPS (this may take a few minutes)..." -ForegroundColor Yellow
ssh ${VPS_USER}@${VPS_IP} "rm -rf ${VPS_DIR}/* && mkdir -p ${VPS_DIR}"
scp -r "$BUILD_DIR\*" "${VPS_USER}@${VPS_IP}:${VPS_DIR}/"
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: SCP upload failed." -ForegroundColor Red
    exit 1
}
Write-Host "Upload complete." -ForegroundColor Green
Write-Host ""

# Step 3: Set permissions
Write-Host "[3/4] Setting executable permissions..." -ForegroundColor Yellow
ssh ${VPS_USER}@${VPS_IP} "chmod +x ${VPS_DIR}/${EXECUTABLE}"
Write-Host ""

# Step 4: Restart server
Write-Host "[4/4] Starting server..." -ForegroundColor Yellow
ssh ${VPS_USER}@${VPS_IP} "systemctl restart gv2-server 2>/dev/null || ${VPS_DIR}/${EXECUTABLE} -batchmode -nographics -server -logFile /dev/stdout &"
Write-Host ""

Write-Host "========================================" -ForegroundColor Green
Write-Host "  Deploy complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "To check server status:" -ForegroundColor Cyan
Write-Host "  ssh ${VPS_USER}@${VPS_IP} `"systemctl status gv2-server`"" -ForegroundColor White
Write-Host ""
Write-Host "To view live logs:" -ForegroundColor Cyan
Write-Host "  ssh ${VPS_USER}@${VPS_IP} `"tail -f /var/log/gv2-server.log`"" -ForegroundColor White
Write-Host ""
