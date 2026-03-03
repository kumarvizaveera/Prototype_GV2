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

## Session 3 — March 3, 2026: Room Code UI, Scene Flow & Enter Battle

### Overview

Replaced the old keyboard-driven H/J host/join system with a proper room code UI. Players now create or join rooms using 4-character codes. Also fixed the full scene flow so the game goes: Bootstrap → Menu (wallet + ship + room UI) → Gameplay.

### Major Changes

**Removed Host ServerMode option**
- `ServerMode` enum reduced from 3 options (Host, DedicatedServer, Auto) to 2 (Auto, DedicatedServer)
- No more pressing H or J — everything goes through the room lobby UI

**Room Code System (NetworkManager.cs)**
- Create Room generates a random 4-character code (letters/numbers, excluding O/0/I/1/L to avoid confusion)
- Join Room accepts a typed code
- The room code is used as Fusion's `SessionName` — same code = same room
- Creator starts as `GameMode.Host`, joiner starts as `GameMode.Client`
- Added lobby UI fields: `roomLobbyPanel`, `createRoomButton`, `joinRoomButton`, `roomCodeInput`, `roomCodeDisplay`, `connectedPanel`, `playerCountText`
- Added `ShowLobbyUI()` method called after ship selection
- Added `ShowConnectedUI()` method that shows room code + player count + Enter Battle button

**Deferred Player Spawning (NetworkManager.cs)**
- Added `_inGameplayScene` flag and `_pendingSpawns` list
- `OnPlayerJoined()` no longer spawns ships immediately — it queues players if still in menu scene
- Extracted `SpawnPlayer()` method from the old OnPlayerJoined logic
- `OnSceneLoadDone()` and `OnUnitySceneLoaded()` spawn all pending players when gameplay scene loads
- This prevents ships from spawning in the menu scene (which was causing firing sounds on left-click)

**Enter Battle Button (NetworkManager.cs)**
- Added `enterBattleButton` field — wired to `LoadGameplay()` in Start()
- `LoadGameplay()` loads the gameplay scene set in Inspector
- Button is hidden until room is connected, then shown via `ShowConnectedUI()`

**Scene Flow Fixes (Web3Bootstrap.cs)**
- All default scene names changed to empty strings — Inspector is the only source of truth
- Fixed false dedicated server detection: Unity's Build Target "Dedicated Server" auto-defines `UNITY_SERVER`, making `IsDedicatedServer()` return true even in Editor. Fix: switch Build Target back to "Windows, Mac, Linux"
- Added debug logging for command-line args

**WalletConnectPanel Raycast Fix (WalletConnectPanel.cs)**
- `Show(false)` now disables `raycastTarget` on the panel's background Image
- Previously, the hidden panel's background was eating clicks meant for the room lobby buttons behind it
- `OnPlayClicked()` now delegates to `NetworkManager.Instance.LoadGameplay()` instead of loading scenes directly

### Scene Flow (Final)

```
Bootstrap (Web3Bootstrap)
  ├── If dedicated server → load gameplay scene directly
  └── If normal client → load SCK_MainMenu_2

SCK_MainMenu_2
  ├── WalletConnectPanel: connect wallet (or play as guest)
  ├── Ship selection (ShipNFTManager)
  ├── Room Lobby: Create Room / Join Room with 4-char code
  └── Connected Panel: shows room code, player count, Enter Battle button

Room_Tests_2 (gameplay)
  └── Players spawn here after Enter Battle is clicked
```

### Files Changed

| File | Changes |
|------|---------|
| NetworkManager.cs | Removed Host mode, added room code system, lobby UI fields, deferred spawning, Enter Battle button, LoadGameplay() method |
| WalletConnectPanel.cs | Disabled raycastTarget on hide, delegated play button to NetworkManager, added ship selection → lobby flow |
| Web3Bootstrap.cs | Empty string defaults, debug logging, fixed UNITY_SERVER false positive |
| ServerBootstrap.cs | No changes (already correct) |

### Bugs Fixed

1. **Bootstrap loading wrong scene** — `UNITY_SERVER` auto-defined by Build Target caused `IsDedicatedServer()` to return true. Fix: switch Build Target.
2. **Hardcoded scene defaults overriding Inspector** — Code defaults persisted in serialized scene data. Fix: set all defaults to empty strings.
3. **Web3 UI disappearing after wallet connect** — WalletConnectPanel hid itself but room lobby wasn't shown yet. Fix: wired ship selection → ShowLobbyUI() flow.
4. **Create Room button not clickable** — Hidden WalletConnectPanel's background Image was blocking raycasts. Fix: disable raycastTarget when hiding.
5. **Spaceship firing in menu scene** — OnPlayerJoined spawned ships immediately in menu. Fix: deferred spawning with _inGameplayScene flag.
6. **Enter Battle not loading scene** — Old playButton from WalletConnectPanel wasn't wired to new flow. Fix: added enterBattleButton to NetworkManager, wired to LoadGameplay().

### Key Lessons

- **Unity serialization overrides code defaults**: Changing a default value in code does NOT change the value already saved in a scene. You must update the Inspector manually (or reset the component).
- **Build Target defines `UNITY_SERVER`**: Setting Build Target to "Dedicated Server" defines `UNITY_SERVER` globally — even in the Editor. This makes `IsDedicatedServer()` true everywhere. Always check Build Target if server detection is misfiring.
- **Overlapping UI panels block raycasts**: When hiding a UI panel, disable `raycastTarget` on its Image component — otherwise the invisible panel eats clicks meant for panels behind it.
- **Don't spawn players in menu scenes**: Use a flag (`_inGameplayScene`) and a pending list to defer spawning until the gameplay scene loads.
- **Inspector-only scene names are safer**: Using empty string defaults and requiring Inspector values prevents stale hardcoded names from loading wrong scenes during testing.

---

## Session 4 — March 3, 2026 (continued): Spawn Fix, UI Cleanup & Panel Isolation

### Overview

Fixed ships spawning in the wrong place after Enter Battle, then cleaned up several UI issues: panels bleeding into each other, buttons not clickable, and elements appearing before their time.

### Bug Fix: Ships Spawning Off-Spline After Enter Battle

**Problem:** After pressing Enter Battle, ships spawned at random positions instead of on the Spline_2 track.

**Root Cause (two layers):**

1. **DontDestroyOnLoad loses scene references:** NetworkManager persists across scenes, so its `spawnSpline` Inspector reference to a SplineContainer in the gameplay scene becomes null after scene load. The code fell back to a simple X-axis spread.

2. **Prefab asset reference vs scene instance:** The Network Manager *prefab* had `spawnSpline` pointing to the Spline_2 *prefab asset* (transform at origin), not the scene instance (correct world position). This prefab reference survived scene loads and looked non-null, so the auto-find code skipped the search — but all spawn positions were calculated relative to origin, putting ships nowhere near the actual track.

**Fix (NetworkManager.cs):**
- Added `TryFindSpawnSpline()` method — uses `GameObject.Find("Spline_2")` to locate the scene instance's SplineContainer on every gameplay scene load
- Always searches by name, never trusts the serialized prefab reference for world-space data
- Always clears `_cachedSpawnPositions` on scene change so positions are recalculated with the correct spline
- Added `spawnSplineName` field (defaults to "Spline_2") for the auto-find target
- Called in both `OnUnitySceneLoaded` and `OnSceneLoadDone` before spawning pending players

### UI Cleanup: Panel Isolation

**Problem:** ShipSelectionPanel was a child of WalletConnectPanel, causing UI elements to bleed between panels.

**Fix:** Moved ShipSelectionPanel to be a sibling root under Canvas:
```
Canvas
  ├── WalletConnectPanel
  └── ShipSelectionPanel   ← was nested inside WalletConnectPanel
```

### Bug Fix: ShipSelectionPanel Visible at Game Start

**Problem:** ConfirmButton and "Loading your ships..." status text from ShipSelectionPanel appeared immediately on the wallet connect screen.

**Root Cause:** ShipSelectionPanel was active in the scene hierarchy, so `OnEnable()` fired at game start — showing loading state before the wallet was even connected.

**Fix (ShipSelectionUI.cs):**
- Added `Awake()` that calls `gameObject.SetActive(false)` — starts fully hidden
- Changed `Show()` to toggle `gameObject.SetActive()` — since it's now its own root, hiding the whole GameObject is correct
- WalletConnectPanel's `showAfterConnect.SetActive(true)` wakes it up at the right time

### Bug Fix: DisconnectButton Not Clickable

**Problem:** DisconnectButton in ConnectedPanel wasn't responding to clicks.

**Root Cause:** Same raycast blocking pattern as session 3 bug #4. `WalletConnectPanel.Show(false)` hid child buttons but kept the root GameObject active. Its background Image was still receiving raycasts and blocking everything behind it.

**Fix (WalletConnectPanel.cs):**
- `HandleWalletConnected` now calls `gameObject.SetActive(false)` instead of `Show(false)` — fully removes the panel from UI
- Moved `HandleShipSelected` and `HandleRoomConnected` event subscriptions from `OnEnable`/`OnDisable` to `Start`/`OnDestroy` — these events fire after the wallet panel hides, so they need to survive `gameObject.SetActive(false)`

### UI Polish: Send OTP Button

**Change:** Renamed "Connect Email" button to "Send OTP". Button now only appears after the player starts typing in the email input field.

**Fix (WalletConnectPanel.cs):**
- `OnEnable()` hides `connectEmailButton` on start
- Added `emailInputField.onValueChanged` listener → `OnEmailInputChanged()`
- `OnEmailInputChanged()` shows the button when text is non-empty, hides when cleared

### Files Changed

| File | Changes |
|------|---------|
| NetworkManager.cs | Added `TryFindSpawnSpline()`, `spawnSplineName` field, auto-find spline on gameplay scene load, clear cached positions on scene change |
| ShipSelectionUI.cs | Added `Awake()` to start hidden, `Show()` uses `gameObject.SetActive()` |
| WalletConnectPanel.cs | Full deactivate on wallet connect, moved ship/room event subs to `Start`/`OnDestroy`, Send OTP button hidden until email typed |

### Bugs Fixed

1. **Ships spawning off-spline** — Prefab's `spawnSpline` pointed to Spline_2 asset (origin) not scene instance. Fix: auto-find scene instance by name on every gameplay scene load.
2. **ShipSelectionPanel visible at start** — Panel active in hierarchy, `OnEnable` showed loading UI immediately. Fix: `Awake()` sets `gameObject.SetActive(false)`.
3. **DisconnectButton not clickable** — Hidden WalletConnectPanel still blocking raycasts. Fix: fully deactivate with `gameObject.SetActive(false)`.
4. **Send OTP button always visible** — Shown before email input had any text. Fix: hidden by default, revealed on `onValueChanged`.

### Key Lessons

- **Prefab asset references vs scene instance references**: A serialized field pointing to a prefab asset gives wrong world-space positions (transform at origin). Always use `GameObject.Find()` at runtime to get the scene instance for world-space data.
- **Fully deactivate panels when done**: `Show(false)` that only hides children still leaves the root Image blocking raycasts. Use `gameObject.SetActive(false)` when the panel is no longer needed.
- **Event subscriptions must outlive the panel**: If events fire after a panel hides (ship selection, room connect), subscribe in `Start`/`OnDestroy` instead of `OnEnable`/`OnDisable` — otherwise deactivating the GameObject kills the subscription.
- **Separate UI panels as siblings, not parent-child**: Nesting panels causes bleed-through. Each panel should be its own root under Canvas.

---

## What's Left

### Completed
- [x] Test Host mode still works — confirmed, press H in editor works fine
- [x] Deploy dedicated server build to VPS
- [x] Clients connect and play via VPS dedicated server
- [x] Room code UI — Create Room / Join Room with 4-char codes
- [x] Scene flow: Bootstrap → Menu → Gameplay with Enter Battle button
- [x] Deferred player spawning (no ships in menu scene)
- [x] Ships spawn on spline correctly after Enter Battle
- [x] UI panel isolation — no more bleed-through between wallet/ship/lobby panels
- [x] DisconnectButton working in ConnectedPanel

### Remaining
- [ ] Test full multiplayer flow: second player joins with room code (ParrelSync clone)
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
- Unity serialization overrides code defaults — Inspector values persist even when you change the default in code
- Build Target "Dedicated Server" defines UNITY_SERVER globally, even in Editor
- Hidden UI panels with raycastTarget=true block clicks on panels behind them
- Don't spawn players in menu scenes — defer with a flag and pending list
- Prefab asset references vs scene instance references — always Find() the scene instance for world-space data
- All previous dedicated server lessons (guard patterns, LocalPlayer safety, camera crashes, etc.)
