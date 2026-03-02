# Session Summary — March 1, 2026: Dedicated Server Migration

## What Was Done

### Part A: Dedicated Server Migration (Code Complete)

We migrated the entire networking codebase from "one player hosts the match" to "a separate headless server runs the match and all players connect as equals." This is the foundation for scaling to 20-30 players later.

### Files Changed

**NetworkManager.cs** — The central networking hub
- Added `ServerMode` enum with three options: Host, DedicatedServer, Auto
- Added `IsDedicatedServer` property — single source of truth for the whole project
- Server detection works three ways: Inspector dropdown, `-server` command-line flag, or `#if UNITY_SERVER` build define
- Added `StartServer()` method that launches `GameMode.Server`
- When running as dedicated server: skips all UI, auto-starts server, disables local input
- Added ParrelSync clone detection — clones are never treated as dedicated servers (they share the same scene data as the main editor, so without this guard they'd inherit the DedicatedServer setting and crash)
- Added null-safety after `StartGame()` — if connection fails and Runner is destroyed, the code catches it instead of throwing NullReferenceException

**NetworkedSpaceshipBridge.cs** — Bridge between Fusion and the ship physics
- Rewrote `Spawned()` with 4-branch authority logic:
  1. Remote ship (not local player) — disable input, attach AimOverride on server
  2. Host's own ship (Host mode only) — full local control
  3. Client's own ship — client-side prediction
  4. Safety fallback — dedicated server edge case, treat as remote
- `SetupCamera()` bails early on dedicated server (no camera to attach)
- `FixedUpdateNetwork()` already handled dedicated server correctly — no changes needed

**NetworkedGameManager.cs** — Game state and countdown UI
- `OnGUI()` returns immediately on dedicated server

**PlayerLabelController.cs** — Name labels above ships
- `Spawned()` skips visual setup on server, still tracks player numbers
- `AssignPlayerNumber()` handles the no-local-player case
- `Render()` returns immediately on dedicated server

**BattleZoneController.cs** — Shrinking battle zone
- `Render()` skips sphere visuals and timer UI on dedicated server
- Shrinking/damage logic was already server-authoritative — no changes needed

**NetworkSuperWeaponHandler.cs** — Super weapon UI
- UI assignment in `Spawned()` wrapped in dedicated server check

**NetworkedPlayerInput.cs** — Input collection
- `Update()` returns immediately on dedicated server (no local input to collect)

**BattleRewardBridge.cs** — Web3 reward system bridge
- Self-disables in `OnEnable()` on dedicated server (blockchain stuff is client-only)

**ServerBootstrap.cs** — NEW file
- Optimizes server-side performance: sets target framerate to 60, disables audio, disables VSync, enables run-in-background, prevents sleep timeout

### Files Verified Safe (No Changes Needed)
- NetworkShieldHandler.cs — all UI gated on HasInputAuthority
- EliminationTracker.cs — server-side only logic
- NetworkedHealthSync.cs — host writes, clients read
- NetworkedCheckpointTracker.cs — HasStateAuthority gated
- NetworkedResourceSync.cs — host writes, clients read
- LevelSynchronizer.cs — HasStateAuthority gated
- RaceCheckpoint.cs — plain trigger, no authority issues

### VPS Setup Guide Created
- `tasks/VPS_Setup_Guide.md` — step-by-step guide for deploying on Hostinger KVM 2
- Covers everything from buying the VPS to auto-restart with systemd
- Includes troubleshooting section and quick reference card

### Bug Fix: ParrelSync + DedicatedServer Mode
- ParrelSync clones share the same scene data as the main editor
- When ServerMode was set to DedicatedServer in Inspector, the clone also tried to start as a server
- This caused "ServerAlreadyInRoom" error from Photon and a NullReferenceException crash
- Fix: Added `ParrelSync.ClonesManager.IsClone()` detection — clones always run as Client

---

## Key Insight

The existing RPC input pipeline (`HasStateAuthority && !HasInputAuthority` in FixedUpdateNetwork) already worked perfectly for dedicated server mode. Clients send input via RPC, server applies it — no game logic rewrites needed. The entire migration was about guarding UI/camera/input references, not changing how the game actually works.

---

---

## Session 2 — March 2, 2026: Testing, Bug Fixes & VPS Deployment

### Bug Fix: Proxy Position Sync Broken After Dedicated Server Migration

**Problem:** Two ParrelSync clones couldn't see each other's ships move — proxy positions were stuck at spawn.

**Root Cause:** `NetworkTransform` was only disabled on non-authority ships (inside `!HasStateAuthority` block in `Spawned()`). On the dedicated server, ALL ships are "state authority" but the physics Rigidbody lives on a CHILD object, not the root transform. NetworkTransform syncs the ROOT, which stays at spawn position. It was writing spawn-position into Fusion's internal state every tick, overriding our custom `SyncPosition` writes.

**Fix (NetworkedSpaceshipBridge.cs):**
- Moved NetworkTransform disable OUTSIDE the `!HasStateAuthority` block so it runs on ALL machines (server, host, and clients)
- Added one-time diagnostic log when proxy follow first activates

**Why it worked before:** In Host mode, the host IS the client with InputAuthority — NetworkTransform's root-position happened to match. In Dedicated Server mode, no one has InputAuthority on the server, so the conflict became visible.

### Bug Fix: Server Stuck at Menu Scene

**Problem:** VPS dedicated server loaded the Bootstrap scene which then loaded the menu scene (SCK_MainMenu_2). With no player to click "Enter Battle," the server sat there forever.

**Fix (Web3Bootstrap.cs):**
- Added `IsDedicatedServer()` method that checks `UNITY_SERVER` define and `-server` command-line flag
- Added `gameplaySceneName` field (Inspector, defaults to "VPS_Tests")
- In `Start()`, if dedicated server is detected, skip menu and load gameplay scene directly

### Bug Fix: ShutdownReason=GameNotFound on VPS

**Problem:** Server loaded the correct gameplay scene (VPS_Tests) but Fusion failed with `ShutdownReason=GameNotFound` — couldn't create or join a session.

**Root Cause:** NetworkManager's `ServerMode` was set to **Host** in the Inspector (not Auto or DedicatedServer). When set to Host, the `-server` command-line flag is completely ignored — the code only checks command-line args in the `Auto` case. So the VPS was trying to run as a regular game build with `autoHostInBuild`, not as a proper dedicated server.

**Fix:** Changed `ServerMode` to **DedicatedServer** directly in the Inspector on the VPS_Tests scene's NetworkManager. This forces dedicated server mode without needing command-line flags.

### Diagnostic Logging Added (NetworkManager.cs)

Added detailed logging to help debug future connection issues:
- Active scene name and build index (catches "scene not in Build Settings" errors)
- Whether `PhotonAppSettings.Global` loaded or fell back to hardcoded AppID
- Full StartGame parameters (mode, region, session name, AppID, AppVersion)
- Try-catch around `Runner.StartGame()` to capture exceptions
- Enhanced `OnShutdown` logging with Runner state details

### VPS Deployment — Successfully Tested

- Built Linux Standalone from Unity
- Uploaded to Hostinger VPS at `187.124.96.178` via SCP
- Server runs with: `/root/gv2-server/L_Tests_3.x86_64 -server -logFile /dev/stdout -batchmode -nographics`
- Two editor clients successfully connect and play
- Host mode (press H) still works in editor

### Files Changed This Session

| File | Changes |
|------|---------|
| NetworkedSpaceshipBridge.cs | Moved NetworkTransform disable to ALL machines; added proxy follow diagnostic log |
| Web3Bootstrap.cs | Added IsDedicatedServer() check, gameplaySceneName field, skip-to-gameplay for server |
| NetworkManager.cs | Added scene/AppID/StartGame diagnostics, try-catch, enhanced OnShutdown logging; ServerMode set to DedicatedServer in Inspector |
| tasks/lessons.md | Added "Proxy Position Sync Broken in Dedicated Server Mode" entry |

---

## What's Left

### Completed
- [x] Test Host mode still works — confirmed, press H in editor works fine
- [x] Deploy dedicated server build to VPS
- [x] Clients connect and play via VPS dedicated server

### Remaining
- [ ] Test position sync between two clients connected to VPS (proxy ships moving correctly)
- [ ] Test with more than 2 clients
- [ ] Set up systemd auto-restart on VPS (see VPS_Setup_Guide.md)
- [ ] Part B: Scale from 4 to 20-30 players per match

---

## Lessons Added to lessons.md
- ParrelSync clones inherit Inspector settings — must detect and override
- Runner can be null after failed StartGame — always null-check
- NetworkTransform must be disabled on ALL machines, not just non-authority — it syncs ROOT transform which conflicts with custom SyncPosition on CHILD Rigidbody
- ServerMode=Host in Inspector ignores the -server command-line flag — use Auto or DedicatedServer
- Dedicated server needs to skip menu scene (no player to click UI buttons)
- All previous dedicated server lessons (guard patterns, LocalPlayer safety, camera crashes, etc.)
