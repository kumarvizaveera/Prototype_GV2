# GV2 — VPS Setup Guide (Hostinger KVM 2)

> Step-by-step guide for deploying the GV2 dedicated server on a Hostinger KVM 2 VPS.
> Written for Veera — no Linux experience assumed.

---

## What You're Setting Up

Right now, when you play GV2, one player's laptop runs as the "Host" — it plays the game AND manages the match for everyone. That's fine for testing, but it's not ideal for real players because:

- The host has an unfair advantage (zero latency)
- If the host disconnects, the match dies
- A laptop can't handle 20-30 players

A VPS (Virtual Private Server) is basically a computer in a data center that runs 24/7. You'll upload a special "headless" version of GV2 to it — no graphics, no screen, just pure game logic. All players connect to it as equals.

---

## Step 1: Buy the Hostinger KVM 2 VPS

1. Go to [hostinger.com/vps](https://www.hostinger.com/vps)
2. Pick **KVM 2** plan ($6.99/month)
   - 2 vCPU, 8 GB RAM, 100 GB NVMe storage
   - This is enough for several 4-player matches or one 20-30 player match
3. During setup, choose these options:
   - **Operating System:** Ubuntu 24.04 LTS (64-bit) — recommended
     - LTS = Long Term Support (5 years of security updates, until 2029)
     - Avoid non-LTS versions like 25.10 — they only get 9 months of support, then you're forced to upgrade
   - **Server Location:** Pick the region closest to where your players will be
     - US East (Ashburn) for North American players
     - Europe (Netherlands/UK) for European players
     - Asia (Singapore) for Asian players
   - **Root Password:** Pick a strong password and **save it somewhere safe** — you'll need it to log in
4. Complete the purchase. It takes 1-2 minutes for the VPS to start up.
5. Once it's ready, Hostinger will show you an **IP address** (something like `192.168.1.100`). Write this down — it's your server's address.

---

## Step 2: Connect to Your VPS

You need to connect to the VPS from your Windows PC using a tool called SSH (Secure Shell). Think of it as a remote control for the server — you type commands, they run on the VPS.

### Option A: Use Windows Terminal (built into Windows 10/11)

1. Open **Windows Terminal** or **PowerShell** (search in Start menu)
2. Type:
   ```
   ssh root@YOUR_IP_ADDRESS
   ```
   Replace `YOUR_IP_ADDRESS` with the IP from Step 1 (e.g., `ssh root@192.168.1.100`)
3. It will ask "Are you sure you want to continue connecting?" — type `yes`
4. Enter your root password (the one from Step 1)
5. You're in! You'll see something like `root@vps:~#`

### Option B: Use PuTTY (alternative if Terminal doesn't work)

1. Download PuTTY from [putty.org](https://www.putty.org)
2. Open PuTTY
3. In "Host Name" — enter your IP address
4. Click "Open"
5. Login as `root` with your password

---

## Step 3: Set Up the Server Environment

Once you're connected via SSH, run these commands one at a time. Copy-paste each line and press Enter.

### 3a. Update the system (like Windows Update, but for Linux)

```bash
apt update && apt upgrade -y
```

This takes 1-2 minutes. It downloads the latest security patches.

### 3b. Install required libraries for Unity

Unity's Linux server build needs some system libraries to run:

```bash
apt install -y libglu1-mesa libxi6 libxcursor1 libxrandr2 libxxf86vm1 screen ufw
```

Here's what each thing is:
- `libglu1-mesa` through `libxxf86vm1` — graphics libraries Unity expects even in headless mode
- `screen` — lets the server keep running after you disconnect from SSH
- `ufw` — the firewall tool (to open the right ports)

### 3c. Create a folder for the game server

```bash
mkdir -p /opt/gv2-server
```

This creates a folder at `/opt/gv2-server` where your game will live.

---

## Step 4: Build the Dedicated Server in Unity

Back on your Windows PC, in Unity:

1. Open **File → Build Settings**
2. Under **Platform**, select **Dedicated Server** (this is a special build target in Unity 6)
   - If you don't see "Dedicated Server", click **Install with Unity Hub** to add it
3. Set **Target Platform** to **Linux**
4. Make sure your scenes are in the correct order:
   - Scene 0: Bootstrap (the first scene that loads — has NetworkManager + ServerBootstrap)
   - Scene 1: SCK_MainMenu
   - Scene 2: Your gameplay scene
5. Click **Build**
6. Choose a folder like `C:\Users\Veera\Desktop\GV2ServerBuild\`
7. Wait for the build to finish — it'll create a Linux executable

After building, you'll have a folder with files like:
```
GV2ServerBuild/
├── Prototype_GV2              ← The main executable (no .exe extension for Linux)
├── Prototype_GV2_Data/        ← Game data folder
├── UnityPlayer.so             ← Unity runtime library
└── ... other files
```

---

## Step 5: Upload the Build to the VPS

You need to copy the build files from your Windows PC to the VPS. There are two ways:

### Option A: Use SCP (from Windows Terminal)

```powershell
scp -r "C:\Users\Veera\Desktop\GV2ServerBuild\*" root@YOUR_IP_ADDRESS:/opt/gv2-server/
```

This copies everything in the build folder to the server. It might take a few minutes depending on your internet speed.

### Option B: Use WinSCP (visual file transfer)

1. Download [WinSCP](https://winscp.net/eng/download.php) — it's a free file transfer tool
2. Open WinSCP
3. Connection settings:
   - **File protocol:** SCP
   - **Host name:** Your VPS IP address
   - **User name:** root
   - **Password:** Your root password
4. Click **Login**
5. On the right side (the server), navigate to `/opt/gv2-server/`
6. On the left side (your PC), navigate to `C:\Users\Veera\Desktop\GV2ServerBuild\`
7. Select all files and drag them to the right side
8. Wait for the transfer to complete

---

## Step 6: Make the Server Executable

Back in your SSH terminal:

```bash
cd /opt/gv2-server
chmod +x Prototype_GV2
```

`chmod +x` tells Linux "this file is a program you can run" (like double-clicking an .exe on Windows).

> **Note:** The filename `Prototype_GV2` matches whatever you named your build in Unity. If you named it differently, use that name instead.

---

## Step 7: Open the Firewall Ports

Photon Fusion uses specific ports for game traffic. You need to tell the VPS firewall to allow that traffic through.

```bash
# Allow SSH (so you can still connect to manage the server)
ufw allow 22/tcp

# Allow Photon Fusion game traffic
# Fusion uses UDP ports 27000-27010 by default
ufw allow 27000:27010/udp

# Enable the firewall
ufw enable
```

When it asks "Command may disrupt existing SSH connections. Proceed?" — type `y`

Then verify:

```bash
ufw status
```

You should see the ports listed as ALLOW.

> **Important:** If Photon Fusion uses different ports in your setup, check your Fusion config and open those instead. The default range is 27000-27010/udp.

---

## Step 8: Test-Run the Server

Let's do a quick test to make sure everything works:

```bash
cd /opt/gv2-server
./Prototype_GV2 -batchmode -nographics -server
```

What these flags mean:
- `-batchmode` — run without any window or graphics (headless)
- `-nographics` — don't initialize any GPU stuff
- `-server` — this is the flag your `ServerBootstrap.cs` and `NetworkManager.cs` look for to activate dedicated server mode

You should see log output like:
```
=== GV2 DEDICATED SERVER STARTING ===
[ServerBootstrap] Unity version: ...
[ServerBootstrap] Target framerate set to 60
[ServerBootstrap] Audio disabled
=== GV2 DEDICATED SERVER READY ===
[NetworkManager] Detected dedicated server mode (command-line: -server)
[NetworkManager] Starting as DEDICATED SERVER...
```

If you see those messages, the server is running!

**To stop it:** Press `Ctrl + C`

---

## Step 9: Run the Server Permanently (Using Screen)

When you close your SSH window, normally any running program stops. `screen` keeps it running in the background.

```bash
# Create a named session called "gv2"
screen -S gv2

# Inside the screen session, start the server
cd /opt/gv2-server
./Prototype_GV2 -batchmode -nographics -server
```

Now the server is running inside a "screen" session. To leave it running and go back to normal:

- Press `Ctrl + A`, then press `D` (this "detaches" the screen — the server keeps running)

To come back later and check on it:

```bash
screen -r gv2
```

To see the list of running screens:

```bash
screen -ls
```

---

## Step 10: Auto-Restart on Crash (Systemd Service)

This is optional but recommended. It makes the server automatically restart if it crashes, and auto-start when the VPS reboots.

Create the service file:

```bash
nano /etc/systemd/system/gv2-server.service
```

Paste this content (right-click to paste in SSH):

```ini
[Unit]
Description=GV2 Dedicated Game Server
After=network.target

[Service]
Type=simple
User=root
WorkingDirectory=/opt/gv2-server
ExecStart=/opt/gv2-server/Prototype_GV2 -batchmode -nographics -server
Restart=always
RestartSec=10

# Logging
StandardOutput=append:/var/log/gv2-server.log
StandardError=append:/var/log/gv2-server-error.log

[Install]
WantedBy=multi-user.target
```

Save the file: Press `Ctrl + X`, then `Y`, then `Enter`.

Now enable and start it:

```bash
# Reload systemd to see the new service
systemctl daemon-reload

# Enable auto-start on boot
systemctl enable gv2-server

# Start the server now
systemctl start gv2-server

# Check if it's running
systemctl status gv2-server
```

You should see "Active: active (running)" in green.

### Useful commands for managing the service:

```bash
# Stop the server
systemctl stop gv2-server

# Restart the server (after uploading a new build)
systemctl restart gv2-server

# View live logs
tail -f /var/log/gv2-server.log

# View error logs
tail -f /var/log/gv2-server-error.log
```

---

## Step 11: Connect from Unity (Client Side)

Once the server is running, you need to tell your Unity clients to connect to it. In your Photon Fusion setup:

1. The server creates a Fusion session (room) on the Photon Cloud
2. Clients join that same session through Photon's matchmaking
3. Photon routes the traffic — clients don't need to know the VPS IP directly

**How it works:** Your dedicated server starts a Fusion session as `GameMode.Server`. When clients start Fusion with `GameMode.Client`, Photon matchmaking connects them to the server's session. The VPS IP doesn't need to be hardcoded — Photon handles the routing.

> If you later want direct connections (bypassing Photon relay), you'd use the VPS IP + port, but that's an advanced optimization for later.

---

## Step 12: Updating the Server (When You Build a New Version)

Every time you make changes and want to update the server:

1. Build a new Dedicated Server build in Unity (same as Step 4)
2. Stop the server:
   ```bash
   systemctl stop gv2-server
   ```
3. Upload the new build files (same as Step 5 — overwrite the old files)
4. Make it executable again:
   ```bash
   chmod +x /opt/gv2-server/Prototype_GV2
   ```
5. Start the server:
   ```bash
   systemctl restart gv2-server
   ```
6. Check it's running:
   ```bash
   systemctl status gv2-server
   ```

---

## Quick Reference Card

| Task | Command |
|------|---------|
| Connect to VPS | `ssh root@YOUR_IP` |
| Start server | `systemctl start gv2-server` |
| Stop server | `systemctl stop gv2-server` |
| Restart server | `systemctl restart gv2-server` |
| Check status | `systemctl status gv2-server` |
| View logs (live) | `tail -f /var/log/gv2-server.log` |
| View errors | `tail -f /var/log/gv2-server-error.log` |
| Upload files | `scp -r BuildFolder/* root@YOUR_IP:/opt/gv2-server/` |
| Check firewall | `ufw status` |
| Server disk usage | `df -h` |
| Server memory usage | `free -h` |
| Server CPU usage | `htop` (install with `apt install htop`) |

---

## Estimated Monthly Cost

| Item | Cost |
|------|------|
| Hostinger KVM 2 VPS | $6.99/month |
| Photon Fusion 2 | Free (100 CCU) |
| **Total** | **$6.99/month** |

---

## Troubleshooting

### "Permission denied" when running the server
```bash
chmod +x /opt/gv2-server/Prototype_GV2
```

### Server starts but no players can connect
- Check firewall: `ufw status` — make sure UDP ports are open
- Check Photon: make sure the Photon App ID is the same on server and clients
- Check logs: `tail -50 /var/log/gv2-server.log`

### Server crashes immediately
- Check error log: `cat /var/log/gv2-server-error.log`
- Common cause: missing libraries. Run: `apt install -y libglu1-mesa libxi6 libxcursor1 libxrandr2 libxxf86vm1`
- Another cause: wrong build target. Make sure you built for **Linux** Dedicated Server, not Windows

### "Address already in use" error
Another instance might already be running:
```bash
# Find and kill any running instances
pkill -f Prototype_GV2

# Then start normally
systemctl start gv2-server
```

### Out of disk space
```bash
# Check disk usage
df -h

# Clean up old logs if needed
truncate -s 0 /var/log/gv2-server.log
truncate -s 0 /var/log/gv2-server-error.log
```

### Server is slow / lagging
```bash
# Check CPU and memory
htop

# If CPU is maxed, consider upgrading to KVM 4 ($12.99/month)
# If memory is full, check for leaks in the Unity logs
```
