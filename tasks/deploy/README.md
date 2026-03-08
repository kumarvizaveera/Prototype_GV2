# GV2 Server Deployment

VPS: `187.124.96.178` (Hostinger KVM 2, Ubuntu)
Build: `C:\Users\Veera\Desktop\Unity\Linux Tests\Tests_10\L_Tests_10`

## First-Time VPS Setup

Only needed once. SSH into the VPS and run the setup script:

```
ssh root@187.124.96.178
```

Upload the setup script and run it:
```
# From Windows PowerShell:
scp "C:\Users\Veera\Prototype_GV2\tasks\deploy\vps-setup.sh" root@187.124.96.178:/root/
ssh root@187.124.96.178 "bash /root/vps-setup.sh"
```

This installs dependencies, configures firewall, sets up systemd auto-restart, and log rotation.

## Deploy New Build

After building in Unity (File > Build > Linux Dedicated Server):

### Option A: PowerShell Script (automated)
```powershell
cd C:\Users\Veera\Prototype_GV2\tasks\deploy
.\deploy-to-vps.ps1
```

### Option B: Manual Commands
```powershell
# Stop server
ssh root@187.124.96.178 "systemctl stop gv2-server"

# Upload build
scp -r "C:\Users\Veera\Desktop\Unity\Linux Tests\Tests_10\*" root@187.124.96.178:/opt/gv2-server/

# Set permissions and restart
ssh root@187.124.96.178 "chmod +x /opt/gv2-server/L_Tests_10 && systemctl start gv2-server"
```

## Monitoring

```bash
# Server status
ssh root@187.124.96.178 "systemctl status gv2-server"

# Live logs
ssh root@187.124.96.178 "tail -f /var/log/gv2-server.log"

# Error logs
ssh root@187.124.96.178 "tail -f /var/log/gv2-server-error.log"

# Restart server
ssh root@187.124.96.178 "systemctl restart gv2-server"

# Stop server
ssh root@187.124.96.178 "systemctl stop gv2-server"
```

## Quick Reference

| Item | Value |
|------|-------|
| VPS IP | 187.124.96.178 |
| SSH | `ssh root@187.124.96.178` |
| Server dir | `/opt/gv2-server/` |
| Executable | `L_Tests_10` |
| Service | `gv2-server` |
| Server log | `/var/log/gv2-server.log` |
| Error log | `/var/log/gv2-server-error.log` |
| Photon ports | UDP 27000-27010 |
| Run flags | `-batchmode -nographics -server` |
