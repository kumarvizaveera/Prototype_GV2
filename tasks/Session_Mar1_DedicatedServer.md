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

## What's Left for Part A

### Unity Tasks (Veera)
- [ ] Add ServerBootstrap.cs to the first scene that loads
- [ ] Test Host mode still works (it should — IsDedicatedServer defaults to false)
- [ ] Test with ParrelSync: main editor set to DedicatedServer, clone presses J
- [ ] When ready: build Dedicated Server target in Unity (File → Build Settings → Dedicated Server → Linux)
- [ ] Upload to VPS following VPS_Setup_Guide.md

### Part B (Future)
- Scale from 4 to 20-30 players per match
- Requires Part A to be tested and deployed first
- Full plan in `tasks/Dedicated_Server_Plan.md`

---

## Lessons Added to lessons.md
- ParrelSync clones inherit Inspector settings — must detect and override
- Runner can be null after failed StartGame — always null-check
- All previous dedicated server lessons (guard patterns, LocalPlayer safety, camera crashes, etc.)
