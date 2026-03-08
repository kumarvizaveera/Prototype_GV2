#!/bin/bash
# GV2 Dedicated Server — VPS First-Time Setup
# Run this ON the VPS after SSH'ing in:
#   ssh root@187.124.96.178
#   bash vps-setup.sh
#
# Only needed once. After this, use deploy-to-vps.ps1 for updates.

set -e

VPS_DIR="/opt/gv2-server"
EXECUTABLE="L_Tests_10"

echo ""
echo "========================================"
echo "  GV2 Server — VPS Setup"
echo "========================================"
echo ""

# Step 1: System dependencies
echo "[1/6] Installing required libraries..."
apt-get update -qq
apt-get install -y -qq libglu1-mesa libxi6 libxcursor1 libxrandr2 libxinerama1 screen ufw
echo "Done."
echo ""

# Step 2: Create server directory
echo "[2/6] Creating server directory..."
mkdir -p $VPS_DIR
echo "Created $VPS_DIR"
echo ""

# Step 3: Firewall
echo "[3/6] Configuring firewall..."
ufw allow 22/tcp        # SSH
ufw allow 27000:27010/udp  # Photon Fusion
ufw --force enable
echo "Firewall configured: SSH (22/tcp) + Photon (27000-27010/udp)"
echo ""

# Step 4: Install systemd service
echo "[4/6] Installing systemd service..."
cat > /etc/systemd/system/gv2-server.service << 'EOF'
[Unit]
Description=GV2 Dedicated Game Server
After=network.target

[Service]
Type=simple
User=root
WorkingDirectory=/opt/gv2-server
ExecStart=/opt/gv2-server/L_Tests_10 -batchmode -nographics -server
Restart=always
RestartSec=10
StandardOutput=append:/var/log/gv2-server.log
StandardError=append:/var/log/gv2-server-error.log
LimitNOFILE=65536

[Install]
WantedBy=multi-user.target
EOF
systemctl daemon-reload
systemctl enable gv2-server
echo "Service installed and enabled (auto-start on boot)."
echo ""

# Step 5: Log rotation
echo "[5/6] Setting up log rotation..."
cat > /etc/logrotate.d/gv2-server << 'EOF'
/var/log/gv2-server.log /var/log/gv2-server-error.log {
    daily
    rotate 7
    compress
    missingok
    notifempty
    postrotate
        systemctl restart gv2-server
    endscript
}
EOF
echo "Logs will rotate daily, keeping 7 days."
echo ""

# Step 6: Verify
echo "[6/6] Checking setup..."
if [ -f "$VPS_DIR/$EXECUTABLE" ]; then
    chmod +x "$VPS_DIR/$EXECUTABLE"
    echo "Executable found and permissions set."
    echo ""
    echo "Ready to start! Run:"
    echo "  systemctl start gv2-server"
    echo ""
    echo "Check status:"
    echo "  systemctl status gv2-server"
    echo ""
    echo "View logs:"
    echo "  tail -f /var/log/gv2-server.log"
else
    echo "Executable not found at $VPS_DIR/$EXECUTABLE"
    echo ""
    echo "Upload your build first from Windows:"
    echo "  scp -r \"C:\\Users\\Veera\\Desktop\\Unity\\Linux Tests\\Tests_8\\*\" root@187.124.96.178:$VPS_DIR/"
    echo "  ssh root@187.124.96.178 \"chmod +x $VPS_DIR/$EXECUTABLE\""
    echo ""
    echo "Then start the server:"
    echo "  systemctl start gv2-server"
fi

echo ""
echo "========================================"
echo "  Setup complete!"
echo "========================================"
echo ""
