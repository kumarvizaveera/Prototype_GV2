# GV2 — Dedicated Server & Player Scaling Plan

> Master plan for migrating from Host Mode to Dedicated Server, then expanding from 4 to 20-30 players per match.
> Created: March 1, 2026

---

## Current State

- **Networking:** Photon Fusion 2, Host/Client mode, RPC-based input pipeline
- **Max Players:** 4 per match
- **Hosting:** One player's laptop acts as Host (server + player)
- **Web3:** Phases 1-3 complete (wallet, NFTs, rewards) — all client-side

---

## Part A — Dedicated Server Migration

> Goal: Move game logic off the player's laptop onto a separate headless server. All players connect as equals.

### A1. Add `GameMode.Server` to NetworkManager.cs
- Add `StartServer()` method (calls `StartGame(GameMode.Server)`)
- Add command-line argument check: `-server` flag auto-starts server mode
- Add `isDedicatedServer` property for other scripts to check
- Server has `Runner.IsServer = true`, `Runner.IsClient = false` — no local player

### A2. Fix `OnPlayerJoined` for Dedicated Server
- Guard camera setup: only run if `Runner.IsClient` (server has no camera)
- Guard UI notifications: server has no screen
- Spawning logic (`runner.Spawn(...)`) stays the same — already server-side

### A3. Update NetworkedSpaceshipBridge.cs
- In `Spawned()`: server never has "its own ship" — all ships are remote
- The `HasStateAuthority && HasInputAuthority` path (host's own ship) never triggers on a dedicated server
- Add safety check: if dedicated server somehow gets InputAuthority, treat as remote ship
- Skip camera attachment on server
- `FixedUpdateNetwork()` for remote ships (`HasStateAuthority && !HasInputAuthority`) already works perfectly — uses RPC/raw input pipeline, no changes needed

### A4. Guard All UI/Camera/Local-Player References
- Pattern: wrap every UI reference in `if (Runner != null && Runner.IsClient)`
- Files to update:
  - `NetworkedGameManager.cs` — countdown UI
  - `PlayerLabelController.cs` — name labels
  - `NetworkedHealthSync.cs` — health bars
  - `BattleZoneController.cs` — zone visuals
  - `CheckpointNetwork.cs` — checkpoint UI
- `EliminationTracker.cs` — already server-side logic, no changes

### A5. Web3 — Verify Client-Only (Minimal Changes)
- All Web3 code already runs on clients only — no wallet or blockchain on the server
- `BattleRewardBridge.cs` triggers minting from host/server — works the same on dedicated server
- Reward wallet private key stays on the server (server knows final placements)
- Use `#if !UNITY_SERVER` to strip Thirdweb/Reown from server builds

### A6. Server Build Configuration
- Unity 6 has built-in "Dedicated Server" build target (File → Build Settings)
- Create server bootstrap script: skips menu, auto-starts `GameMode.Server`
- Strip UI, audio, ThirdwebManager, Reown from server build via `#if !UNITY_SERVER`
- Server launches with: `./GV2Server -batchmode -nographics -server`

### A7. VPS Setup (Self-Hosted)
- **Recommended:** Hostinger KVM 2 — 2 vCPU, 8GB RAM, 100GB NVMe — $6.99/month
- OS: Ubuntu 22.04 or 24.04
- Setup steps:
  1. Create VPS instance
  2. Upload headless server build
  3. `chmod +x` the server binary
  4. Open UDP port for Photon Fusion in firewall (`ufw allow <port>/udp`)
  5. Launch: `./GV2Server -batchmode -nographics -server`
  6. Optional: set up systemd service so server auto-restarts on crash
- Photon Cloud still handles matchmaking/relay — VPS runs game logic
- Free tier (100 CCU) covers up to 100 simultaneous connected players

### A8. Host Mode Fallback Toggle
- Add Inspector enum on NetworkManager: `ServerMode` (Host / DedicatedServer / Auto)
- `Auto` = check command-line args → if `-server` flag, use Server mode, else Host mode
- ParrelSync local testing stays unchanged (main = Host, clone = Client)
- Both code paths coexist — no features lost

### Part A — Cost Summary

| Item | Cost |
|------|------|
| Photon Fusion 2 | Free tier (100 CCU) — already active |
| Hostinger VPS (KVM 2) | $6.99/month |
| Code changes | Just our time |

---

## Part B — 20-30 Player Expansion

> Goal: Scale matches from 4 players to 20-30 players per battle. Depends on Part A being complete first (a laptop can't host 30 players, but a VPS can).

### B1. Increase Max Players in Fusion Session
- Change `maxPlayers` in NetworkManager from 4 to 30
- Update `StartGameArgs.PlayerCount` to match
- Photon session now accepts up to 30 connections

### B2. Expand Spawn System
- Current: spline-based spawn calculates 4 positions with `minSpawnSpacing`
- Problem: 30 ships need 30 non-overlapping spawn positions
- Solution: reduce `minSpawnSpacing` OR use multiple spline segments OR grid-based spawn around a larger starting area
- Update `CalculateSplineSpawnPoints()` and `GetSpawnPosition()` in NetworkManager

### B3. Optimize Input Pipeline for 30 Streams
- Current: every player sends RPC + raw reliable data every frame to the server
- With 30 players, that's 30 input streams per tick — may cause server lag
- Optimizations:
  - Reduce raw data send rate (every 2nd or 3rd frame instead of every frame)
  - Keep RPC as primary (more efficient for Fusion)
  - Profile server CPU under 30-player load — may need to batch input processing
  - Consider input compression (delta encoding — only send what changed)

### B4. Interest Management (Network Culling)
- Current: every player syncs position/rotation/health/weapons to every other player
- With 4 players: 12 sync relationships — trivial
- With 30 players: 870 sync relationships — heavy bandwidth
- Solution: **Fusion Interest Management** — only sync data for nearby players
  - Players far away get lower update rates or no updates
  - Photon Fusion 2 has built-in Area of Interest support
  - Configure interest radius based on combat range and visibility
  - Ships beyond render distance don't need per-tick position updates

### B5. Scale EliminationTracker
- Current: tracks 4 ship deaths, assigns placements 1st-4th
- Update: handle 1st through 30th placements
- Reward tiers: more granular (top 3, top 10, top 30 instead of just 1st-4th)
- Update `BattleRewardBridge.cs` reward amounts per placement tier

### B6. Rebalance Battle Zone
- Current: shrinking zone tuned for 4 ships in a small arena
- With 30 ships: arena needs to start much larger
- Shrink phases: more stages, slower initial shrink, faster final shrink
- Update `BattleZoneController.cs`:
  - Larger initial zone radius
  - More shrink phases (e.g., 5-6 instead of 2-3)
  - Dynamic shrink speed based on players remaining
  - Optional: multiple zone centers that merge (like PUBG's later zones)

### B7. Scale Power-Up and Weapon Spawning
- Current: gyros and weapon cubes placed for 4 players
- With 30 players: need more loot spread across a larger map
- Update `NetworkedSpawnerInitializer.cs` and power-up controllers
- Scale spawn counts proportionally to player count
- Spread loot more evenly to avoid 30 ships fighting over 4 power-ups

### B8. Performance Profiling & Optimization
- Test with 10, 20, 30 bot/dummy connections
- Profile server: CPU usage, memory, tick rate stability
- Profile client: FPS with 30 ships rendered, particle effects, UI labels
- Optimize:
  - LOD (Level of Detail) for distant ships
  - Reduce particle effects at distance
  - Cap UI elements (don't render 30 health bars — only nearby ones)
  - Consider server tick rate reduction (from 60 to 30 Hz) if CPU is bottleneck

### B9. Photon CCU Planning
- 30 players per match = 30 CCU per match
- Free tier: 100 CCU = max 3 simultaneous matches
- If you need more: Photon Plus bundle = 200 CCU at $95/year
- Or: 500 CCU plan at $125/month

### B10. VPS Scaling
- 30-player matches need more server power than 4-player
- Start with Hostinger KVM 2 (2 vCPU, 8GB RAM) — $6.99/month
- If running multiple 30-player matches simultaneously, upgrade to KVM 4 (4 vCPU, 16GB) — $12.99/month
- Monitor server CPU and memory after launch to decide

### Part B — Cost Summary

| Item | Cost |
|------|------|
| VPS (same or upgraded) | $6.99 - $12.99/month |
| Photon CCU upgrade (if needed) | $95/year (200 CCU) or $125/month (500 CCU) |
| Larger arena/map work | Design time |
| Performance profiling | Testing time |

---

## Execution Order

| Phase | Steps | Depends On | Effort |
|-------|-------|------------|--------|
| **Part A** (Dedicated Server) | A1-A8 | Nothing — can start now | 1-2 sessions |
| **Part B** (20-30 Players) | B1-B10 | Part A complete | 3-5 sessions |

Part A comes first because:
1. A laptop physically can't host 30 players — you need the VPS
2. The dedicated server code is the foundation for everything in Part B
3. Part A is lower risk — mostly guarding existing code for "no local player"
4. Part B is higher risk — touches gameplay balance, performance, and bandwidth

---

## What Stays the Same Across Both Parts

- RPC input pipeline (clients → server) — already the right architecture
- Position sync (server writes, clients interpolate)
- Game state machine (server-authoritative)
- All Web3 code (wallet, NFTs, rewards) — client-only, no changes
- Core gameplay mechanics (weapons, power-ups, flight model)
- Scene flow (Bootstrap → Menu → Gameplay)

---

## Open Questions

1. **Arena size for 30 players** — use the same map but bigger zone, or build a new larger map?
2. **Reward tiers** — how should token rewards scale? Top 3 get bonus? Top 10? Everyone gets something?
3. **Bot backfill** — if only 15 players join a 30-player match, add AI bots to fill slots?
4. **Region** — which region for the VPS? (US, EU, Asia?)
5. **Multiple match instances** — run several matches at once on one VPS, or one match per server?
