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

---

## Session 5 — March 3, 2026 (continued): Multiplayer Scene Sync, Countdown & Reward Fixes

### Overview

Fixed the full ParrelSync multiplayer flow: host camera, client scene loading, spawn positions, synced countdown timer, and battle reward placement bugs. The room code flow now works end-to-end with two players.

### Bug Fix: Camera Not Following Host Ship After Enter Battle

**Problem:** Host pressed Enter Battle, ship spawned, but camera stayed static.

**Root Cause:** Ships were spawned inside `OnUnitySceneLoaded`, which fires after `Awake()` but BEFORE `Start()`. VehicleCamera hadn't run `Start()` yet, so `SetupCamera()` couldn't find it.

**Fix (NetworkManager.cs):**
- Added `SpawnPendingPlayersNextFrame()` coroutine — waits one frame (`yield return null`) before spawning
- This ensures all scene objects have their `Start()` called before we try to find VehicleCamera

### Bug Fix: Client Not Loading Gameplay Scene When Host Starts

**Problem:** Client stayed stuck on the join room UI when host pressed Enter Battle. Could hear firing sounds but no gameplay visuals.

**Root Cause (multiple failed approaches):**
1. Tried making `LoadGameplay()` HOST-only and relying on Fusion's `NetworkSceneManagerDefault` to sync — **failed**, it doesn't reliably sync mid-game scene changes
2. Tried sending raw data "LOAD" command with no delay — **failed**, host loaded scene before Fusion could transmit the data
3. Tried showing Enter Battle on BOTH sides — **failed**, user wanted client to auto-load

**Final Fix (NetworkManager.cs):**
- HOST sends 4-byte "LOAD" magic via `Runner.SendReliableDataToPlayer()` to all clients
- `LoadGameplayWithClientSync()` coroutine waits 10 frames after sending before loading the scene on host
- CLIENT receives "LOAD" in `OnReliableDataReceived()`, calls `HideAllMenuUI()` and `SceneManager.LoadScene()`
- Added `HideAllMenuUI()` method — hides all DontDestroyOnLoad UI panels (lobby, connected, buttons, status text)

### Bug Fix: Both Ships Spawning at Same Position

**Problem:** Both players' ships appeared on top of each other at the same spline position.

**Root Cause:** Fusion re-fires `OnPlayerJoined` after scene changes for existing players. HOST got spawned by `OnPlayerJoined` at slot 0 (via `_spawnedPlayers.Count` which was 0). Then the coroutine only had CLIENT left, also at index 0.

**Fix (NetworkManager.cs):**
- Changed `SpawnPendingPlayersNextFrame()` to use `_spawnedPlayers.Count` as the slot index in the spawn loop
- Since `_spawnedPlayers.Count` grows after each `SpawnPlayer()` call, each player gets the next available slot
- Added double-spawn guard in `SpawnPlayer()` — checks if player already has a non-null ship

### Feature: Synced Countdown Timer (5, 4, 3, 2, 1, GO!)

**What it does:** When host presses Enter Battle, both screens show "Match starting in 5... 4... 3... 2... 1... GO!" before loading gameplay.

**Implementation (NetworkManager.cs):**
- Added `COUNTDOWN_KEY` / `COUNTDOWN_MAGIC` raw data constants (4-byte "CNTD")
- `LoadGameplay()` now sends COUNTDOWN command to all clients, then starts `CountdownThenLoad()` coroutine on host
- CLIENT receives COUNTDOWN in `OnReliableDataReceived()` and starts the same coroutine
- `CountdownThenLoad()` shows countdown on `clientJoinedText` (inside connectedPanel), waits 1 second per tick
- After "GO!" + 0.3s pause, HOST starts `LoadGameplayWithClientSync()` to send LOAD and transition
- Cancels any running "Client has joined" auto-hide coroutine so it doesn't hide countdown text

### UI: Client Waiting State

**What it does:** After client joins a room, they see "Waiting for host to start the match..." instead of the Enter Battle button.

**Implementation (NetworkManager.cs):**
- `ShowConnectedUI()` now checks `Runner.IsServer` — only HOST gets the Enter Battle button
- CLIENT sees "Waiting for host to start the match..." on `clientJoinedText`

### Bug Fix: Battle Rewards — Client Gets 1st Place, Host Gets No Popup

**Problem:** When client's ship was destroyed, client showed "1st Place + 100 PRANA". Host (still alive) got no reward popup at all.

**Root Cause (3 issues):**

1. **EliminationTracker never tracked anything:** `Update()` was gated on `_isRacing` which waited for `NetworkedGameManager.OnRaceStarted`. Our match flow uses a custom countdown — not NetworkedGameManager — so `_isRacing` was never set to true. No eliminations were ever detected.

2. **Fallback hardcoded 1st place:** `BattleRewardBridge.DistributeWithDummyPlacement()` always gave `placement = 1`, even for dead players. When the 5-second EliminationTracker timeout expired, the dead client got 1st place.

3. **No reward trigger for survivors:** The host's ship was alive, so `HandleLocalShipDestroyed` never fired. With EliminationTracker broken, `OnMatchResultsReady` never fired either. The host had no way to receive rewards.

**Fix 1 (EliminationTracker.cs):**
- Removed `_isRacing` gate from `Update()` — now monitors vehicles as soon as ships exist
- Only checks for eliminations when `_monitoredVehicles.Count >= 2` (at least 2 ships detected)

**Fix 2 (BattleRewardBridge.cs — DistributeWithDummyPlacement):**
- If local ship is dead (`_localShipDead = true`), counts alive ships and calculates placement: `Mathf.Max(2, aliveCount + 1)`
- If local ship is alive, gives 1st place
- Added `CountAliveShips()` helper — iterates all VehicleHealth, checks Damageables for any with `CurrentHealth > 0`

**Fix 3 (BattleRewardBridge.cs — CheckIfLastStanding):**
- New method called every frame when local ship is alive and subscribed to vehicle events
- Counts total networked ships and alive ships
- If `totalNetworkedShips >= 2` and `aliveCount == 1` and local ship is alive → triggers 1st place reward
- This covers the surviving player (host) who never gets `HandleLocalShipDestroyed`

### Files Changed

| File | Changes |
|------|---------|
| NetworkManager.cs | HOST-only Enter Battle, raw data LOAD/COUNTDOWN commands, 10-frame delay, CountdownThenLoad coroutine, SpawnPendingPlayersNextFrame with 1-frame delay, HideAllMenuUI, ShowConnectedUI host/client split, double-spawn guard, _spawnedPlayers.Count slot index |
| EliminationTracker.cs | Removed _isRacing gate, track as soon as 2+ ships exist |
| BattleRewardBridge.cs | Smart fallback placement (dead=last, alive=1st), CountAliveShips helper, CheckIfLastStanding for survivors |

### Bugs Fixed

1. **Camera not following host ship** — Ships spawned before VehicleCamera.Start(). Fix: 1-frame delay before spawning.
2. **Client not loading gameplay** — Fusion's NetworkSceneManagerDefault doesn't sync mid-game scene changes. Fix: raw data LOAD command with 10-frame transmission delay.
3. **Ships at same position** — Fusion re-fires OnPlayerJoined after scene load, causing duplicate slot 0. Fix: use _spawnedPlayers.Count as slot index.
4. **Client gets 1st place when destroyed** — Fallback hardcoded placement=1. Fix: count alive ships.
5. **Host gets no reward popup** — No trigger for surviving player. Fix: CheckIfLastStanding polls for last-ship-alive state.
6. **EliminationTracker not detecting deaths** — Gated on _isRacing which was never set. Fix: removed gate, check as soon as 2+ ships monitored.

### Key Lessons

- **Fusion re-fires OnPlayerJoined after scene changes** — Don't assume it only fires once per player. Use a guard (`_spawnedPlayers.ContainsKey`) to prevent double-spawning.
- **NetworkSceneManagerDefault doesn't reliably sync manual scene loads** — If you use `SceneManager.LoadScene()` yourself, you need your own sync mechanism (raw data commands work).
- **Raw data needs transmission time** — If you send data and immediately load a scene, the data may not reach clients. Add a delay (10 frames works).
- **Unity's sceneLoaded fires before Start()** — Spawning objects in sceneLoaded means scene components like cameras haven't initialized yet. Wait one frame.
- **Fallback logic must be placement-aware** — A dead player should never default to 1st place. Always check ship health state.
- **Surviving players need their own reward trigger** — HandleLocalShipDestroyed only covers losers. Winners need a separate detection path (poll for last-standing state).
- **EliminationTracker shouldn't depend on NetworkedGameManager** — If your match flow doesn't use the game manager's race state, the tracker must work independently.

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
- [x] ParrelSync room code flow — host creates, clone joins, both load gameplay
- [x] Camera follows host ship after Enter Battle
- [x] Client auto-loads gameplay when host starts match
- [x] Ships spawn at different positions
- [x] Synced countdown timer (5, 4, 3, 2, 1, GO!)
- [x] Client sees "Waiting for host to start" after joining
- [x] Battle rewards — correct placement for destroyed (2nd) and surviving (1st) players

### Remaining
- [ ] Test position sync between two clients connected to VPS (proxy ships moving correctly)
- [ ] Rebuild Linux server with latest changes and deploy to VPS
- [ ] Test with more than 2 clients
- [ ] Set up systemd auto-restart on VPS (see VPS_Setup_Guide.md)
- [ ] Part B: Scale from 4 to 20-30 players per match

---

## Session 6 — March 6, 2026: Scene NetworkObject Registration, UI Null Safety & Ship Lock Feature

### Overview

Fixed scene-placed NetworkObjects (like BattleZoneController) not working after Enter Battle, fixed a NullReferenceException crash in the wallet connect flow, and added the ability to lock ships in the selection screen.

### Bug Fix: BattleZoneController Not Working (No Sphere, No Timer)

**Problem:** After pressing Enter Battle, BattleZoneController's `Spawned()` was never called — no sphere visual, no shrink timer, no zone damage.

**Root Cause:** The gameplay scene is loaded manually with `SceneManager.LoadScene()` (bypassing Fusion's `NetworkSceneManagerDefault`). Fusion never discovers scene-placed NetworkObjects when scenes are loaded this way, so their `Spawned()` callback never fires and `[Networked]` properties never initialize.

**Fix (NetworkManager.cs):**
- Added `RegisterSceneNetworkObjects(scene)` method — called in `OnUnitySceneLoaded` right after the gameplay scene loads
- Uses `scene.GetComponents<NetworkObject>()` (Fusion's extension method) to find all scene-placed NetworkObjects
- Sorts them by `SortKey` using `NetworkObjectSortKeyComparer.Instance` for deterministic ordering (must match on host and clients)
- Calls `Runner.RegisterSceneObjects(sceneRef, sceneObjects)` — the same API Fusion's own scene manager uses internally
- This triggers `Spawned()` on all scene-placed NetworkBehaviours (BattleZoneController, etc.)

**Why it affects ALL scene-placed NetworkObjects:** Any NetworkBehaviour in the gameplay scene (not just BattleZoneController) was silently broken. This fix makes all of them work.

### Bug Fix: NullReferenceException in WalletConnectPanel.OnConnectGuestClicked

**Problem:** Clicking the Guest button (or any connect button) crashed with NullReferenceException at `Web3Manager.Instance.ConnectAsGuest()`.

**Root Cause:** `Web3Manager.Instance` was null — the Web3Manager GameObject wasn't in the scene or hadn't initialized yet when the button was clicked.

**Fix (WalletConnectPanel.cs):**
- Added `CheckWeb3Manager()` guard method that checks `Web3Manager.Instance != null`
- Added the guard to ALL button handlers: email, Google, Discord, guest, external wallet
- Instead of crashing, shows "Web3 not ready — is Web3Manager in the scene?" and logs an error

### Feature: Locked Ships in Selection Screen

**What it does:** Ships can be marked as locked in the Inspector — they show up greyed out with a lock overlay and cannot be selected, regardless of NFT ownership. Useful for "coming soon" ships, level-gated ships, or temporarily disabled ships.

**ShipNFTManager.cs (ShipDefinition):**
- Added `bool isLocked = false` field with Inspector tooltip
- Added `IsShipAvailable(ship)` method — returns true only if owned AND not locked
- `SelectShip()` now blocks locked ships before checking ownership

**ShipCardUI.cs:**
- `Setup()` now takes a `bool locked` parameter — card is only interactable when `owned && !locked`
- Added optional `lockReasonText` field (TMP_Text) — shows "Locked" vs "Not Owned" on the lock overlay
- Lock overlay activates for both locked and unowned ships

**ShipSelectionUI.cs:**
- Passes `ship.isLocked` to `ShipCardUI.Setup()`
- Clicking a locked card shows "{name} is locked." status message
- Auto-select on panel open skips locked ships

### Diagnostic Logging Added (BattleZoneController.cs)

- `Spawned()` logs authority, settings, and Inspector reference status
- `Render()` logs once whether it's running or being skipped (and why)
- Warns if `IsDedicatedServer` is true in Editor (common misconfiguration)

### Files Changed

| File | Changes |
|------|---------|
| NetworkManager.cs | Added `RegisterSceneNetworkObjects()` method, called in `OnUnitySceneLoaded` after gameplay scene loads |
| BattleZoneController.cs | Added diagnostic logging in `Spawned()` and `Render()` |
| WalletConnectPanel.cs | Added `CheckWeb3Manager()` null guard to all connect button handlers |
| ShipNFTManager.cs | Added `isLocked` to ShipDefinition, `IsShipAvailable()` method, locked guard in `SelectShip()` |
| ShipCardUI.cs | Added `locked` param to `Setup()`, `lockReasonText` field, "Locked"/"Not Owned" display |
| ShipSelectionUI.cs | Passes `isLocked` to card setup, locked-specific status message, skip locked on auto-select |

### Bugs Fixed

1. **BattleZoneController not working** — Scene-placed NetworkObjects never spawned because manual `SceneManager.LoadScene()` bypasses Fusion. Fix: `Runner.RegisterSceneObjects()` after scene load.
2. **Guest button NullReferenceException** — `Web3Manager.Instance` was null. Fix: null guard on all connect buttons.

### Key Lessons

- **Manual scene loads bypass Fusion's NetworkObject discovery** — If you use `SceneManager.LoadScene()` instead of Fusion's scene manager, you must manually call `Runner.RegisterSceneObjects()` with all scene-placed NetworkObjects, sorted by `SortKey` for deterministic ordering. Without this, `Spawned()` never fires and `[Networked]` properties don't work.
- **Singleton null checks on UI button handlers** — Any button that calls a singleton (like `Web3Manager.Instance`) should guard against null. The singleton's `Awake()` may not have run yet if it's in a different scene or the GameObject was destroyed.

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
- Fusion re-fires OnPlayerJoined after scene changes — guard against double-spawning
- NetworkSceneManagerDefault doesn't sync manual scene loads — use raw data commands
- Raw data needs transmission time before scene change — add a delay
- Unity's sceneLoaded fires before Start() — wait one frame before spawning
- Fallback reward logic must check ship health — dead players shouldn't get 1st place
- Surviving players need their own reward trigger — poll for last-standing state
- EliminationTracker shouldn't gate on NetworkedGameManager if match flow is custom
- All previous dedicated server lessons (guard patterns, LocalPlayer safety, camera crashes, etc.)

---

## Session 7 — March 7, 2026: Dual Ship Selection, Character Selection UI, Rarity System & NFT Minting

### Overview

Built the full ship + character selection pipeline: overhauled ship selection to a click-order dual-slot system, created the character selection UI with dual-roster layout, implemented the character improvement/rarity system with multiplicative stat stacking, analyzed character sprites, assigned rarity ranks, and minted all 8 characters as ERC-1155 NFTs.

### Feature: Click-Order Dual Ship Selection (ShipSelectionUI.cs)

Overhauled ship selection from single-pick to a two-slot system where players select a Primary and Secondary ship.

**How it works:**
- 1st click on any ship → assigns as Primary (badge shows "Primary")
- 2nd click on a different ship → assigns as Secondary (badge shows "Secondary")
- Clicking an already-assigned ship removes it; if Primary is removed, Secondary shifts up to Primary
- Ships must have different `meshRootIndex` (type conflict validation — can't pick two of the same type)
- Only owned and non-locked ships are selectable
- Confirm button enables only when both slots are filled
- Info panel shows selected ship's name, description, and rarity
- Dynamic status messages guide the player through the flow

### Feature: Character Selection UI (CharacterSelectionUI.cs)

New dual-roster character selection screen that appears after ship selection.

**Layout:**
- Two rows organized by ship type: Row 1 = characters for Ship 0 (Spaceship roster), Row 2 = characters for Ship 1 (Vimana roster)
- Each character has a `shipRosterIndex` (0 or 1) that determines which row it appears in

**4-slot selection system:**
- Slot [0] = Ship 0 Primary, Slot [1] = Ship 0 Secondary
- Slot [2] = Ship 1 Primary, Slot [3] = Ship 1 Secondary
- Click-order per roster mirrors the ship selection: 1st click = Primary, 2nd = Secondary
- Confirm button only enables when all 4 slots are filled

**Card UI (CharacterCardUI.cs):**
- Shows name, rarity color, icon/portrait
- Lock overlay with "Locked" vs "Not Owned" messaging
- Selection highlight and slot badges ("Primary", "Secondary")
- Dim colors for locked/unowned cards

### Feature: Character NFT Manager (CharacterNFTManager.cs)

**CharacterDefinition data structure:**
- `displayName`, `description`, `tokenId` (ERC-1155), `rarity` (Common/Rare/Epic/Legendary)
- `shipRosterIndex` (0=Spaceship, 1=Vimana), `isDefault` (free, no NFT required), `isLocked`
- `icon` (Sprite), `characterStats` (VSX.Engines3D.CharacterData — base stat multipliers)

**Rarity multiplier system:**
- Common: 1.00x, Rare: 1.06x, Epic: 1.12x, Legendary: 1.18x
- Accessed via `CharacterRarityHelper.GetMultiplier(rarity)`

**Ownership:** `OwnsCharacter()` checks NFT balance (defaults always return true). `IsCharacterAvailable()` checks ownership AND not locked. `GetCharactersForShip()` filters by `shipRosterIndex`.

### Feature: Character Improvements & Stat Stacking (mint_count_and_improvements.md)

Designed 13 shared stat multipliers across CharacterData and ArtifactData. Each character has a unique stat profile.

**Multiplicative stacking formula:**
```
Final Stat = Base × (Character Multiplier × Rarity Multiplier) × Artifact Multiplier × SuperWeapon Multiplier
```

**8 characters with unique class profiles:**
1. Aaryaveer — Balanced Striker (all-rounder, beginner-friendly)
2. Ishvaya — Precision/Control (tactical, accurate)
3. Vyanika — Mobility/Evasive (fast, slippery)
4. Roudra — Heavy Burst (aggressive damage)
5. Zorvan — Missile Hunter (lock-on specialist)
6. Kaevik — Rapid Assault (high fire rate)
7. Nysera — Long-Range (sniper feel)
8. Virexa — Glass Cannon (high-risk, high-reward)

**Mint supply per character:** 12 Common + 5 Rare + 2 Epic + 1 Legendary = 20 NFTs each, 160 total.

### Feature: CreateCharacterDataAssets Editor Script

Created `Assets/GV/Scripts/Editor/CreateCharacterDataAssets.cs` — Unity Editor script that generates 8 CharacterData ScriptableObject assets with the stat profiles from `mint_count_and_improvements.md`. Run from Unity menu to create assets at `Assets/GV/Data/Characters/`.

### Selection Flow (Complete)

```
Wallet Connect → Ship Selection (2 slots, different types)
                    → Character Selection (4 slots, 2 per roster)
                        → Room Lobby (Create/Join)
                            → Enter Battle → Gameplay
```

### Files Created/Changed (UI & Selection)

| File | Status | Changes |
|------|--------|---------|
| ShipSelectionUI.cs | Modified | Click-order dual-slot system, type conflict validation, slot badges, info panel |
| CharacterSelectionUI.cs | New | Dual-roster layout, 4-slot selection, per-roster click-order |
| CharacterNFTManager.cs | New | CharacterDefinition, rarity enum/multiplier, ownership checks, roster filtering |
| CharacterCardUI.cs | New | Card component with lock overlay, badges, rarity colors, click callbacks |
| CreateCharacterDataAssets.cs | New | Editor script to generate 8 CharacterData ScriptableObjects |
| ShipCardUI.cs | Modified | Added slot badge support, lock reason text |
| mint_count_and_improvements.md | New | Full character stat profiles, rarity tiers, stacking formula, mint supply |

---

### Character Sprite Analysis

Reviewed all 8 character sprites in `Assets/GV/Characters/Sprites/`:

| Character | Visual Traits | Complexity |
|-----------|--------------|------------|
| Roudra | Massive gold/bronze armor, Om symbol, glowing energy orbs | Highest |
| Kaevik | Steampunk circuit-board armor, incredibly detailed textures | Very High |
| Vyanika | Purple halo, iridescent teal/rose armor, eagle shoulder plates | High |
| Aaryaveer | Royal crown, gold halo, ornate ceremonial armor | High |
| Ishvaya | Dark armor with blue circuit lines, deity chest plate | Medium |
| Zorvan | Blue tactical power armor, battle scars | Medium |
| Nysera | Sleek grey/orange tactical suit, cybernetic implant | Lower |
| Virexa | Minimal dark tactical vest, stylish but simple | Lowest |

### Rarity Assignment (Visual + Stat Multipliers Combined)

Combined visual complexity ranking with stat multiplier totals from `mint_count_and_improvements.md`. Characters with lower total multiplier sums (stronger individual stats) and more ornate visuals got higher rarity. Characters with higher sums (more balanced/average stats) and simpler designs got lower rarity.

| Rarity | Character | Total Multiplier Sum | Peak Stat | Visual Match |
|--------|-----------|---------------------|-----------|--------------|
| Legendary | Roudra | 13.31 (lowest = strongest) | 1.15 | Most ornate visuals |
| Legendary | Kaevik | 13.53 | 1.10 | Very detailed steampunk |
| Epic | Vyanika | 13.46 | 1.10 | Iridescent halo armor |
| Epic | Aaryaveer | 13.46 | 1.08 | Royal ceremonial |
| Rare | Ishvaya | 13.60 | 1.10 | Dark circuit armor |
| Rare | Zorvan | 13.59 | 1.12 | Tactical power armor |
| Common | Nysera | 13.60 | 1.12 | Clean tactical suit |
| Common | Virexa | 13.64 (highest = most balanced) | 1.12 | Minimal vest |

### NFT Minting

Minted all 8 characters on contract `0x8405209745b8f1A43D21876120543d20e4a7600C` (MythiX Tests, Avalanche Fuji). Token IDs 0–3 were already used for ships.

**Minting approach:** Attempted thirdweb API (`writeContract`, `sendTransactions`) first — failed with "Chain does not support EIP-7702" because Avalanche Fuji doesn't support the EIP-7702 bundler thirdweb uses for server wallets. Pivoted to browser-based minting through the Thirdweb dashboard UI with the owner wallet.

| Token ID | Character | Rarity | Supply | Class | Attributes |
|----------|-----------|--------|--------|-------|------------|
| 4 | Roudra | Legendary | 1 | Heavy Burst | Rarity=Legendary, Class=Heavy Burst |
| 5 | Kaevik | Legendary | 1 | Rapid Assault | Rarity=Legendary, Class=Rapid Assault |
| 6 | Vyanika | Epic | 2 | Mobility/Evasive | Rarity=Epic, Class=Mobility Evasive |
| 7 | Aaryaveer | Epic | 2 | Balanced Striker | Rarity=Epic, Class=Balanced Striker |
| 8 | Ishvaya | Rare | 5 | Precision/Control | Rarity=Rare, Class=Precision Control |
| 9 | Zorvan | Rare | 5 | Missile Hunter | Rarity=Rare, Class=Missile Hunter |
| 10 | Nysera | Common | 12 | Long-Range | Rarity=Common, Class=Long-Range |
| 11 | Virexa | Common | 12 | Glass Cannon | Rarity=Common, Class=Glass Cannon |

**Total: 40 character NFTs** across 8 token IDs. Combined with 4 ship tokens (IDs 0–3), the contract holds 12 token types total.

Each NFT includes on-chain metadata: Name, Description, and two attributes (Rarity, Class). Sprite images not uploaded (file upload from VM to browser not supported — can be added manually via Thirdweb dashboard).

### On-Chain Verification

Verified via `readContract` batch call:
- `nextTokenIdToMint()` = 12 (IDs 0–11 exist)
- All `totalSupply()` values match: 1, 1, 2, 2, 5, 5, 12, 12

### Contract Capabilities Confirmed

- **Edit metadata after mint:** Yes — `setTokenURI(uint256 _tokenId, string _uri)` allows updating name, description, attributes by pointing to a new metadata URI
- **Increase supply later:** Yes — call `mintTo()` with the same token ID and additional quantity (ERC-1155 allows minting more of existing tokens)
- **Freeze metadata:** `freezeMetadata()` permanently locks all URIs — don't call until all edits are finalized. `uriFrozen` is currently `false`

### Competition Demo Defaults

For the Build Games competition judge review, default unlocked characters (mirroring the 2 default ships):

| Type | Default Unlocked | Locked |
|------|-----------------|--------|
| Ships | Spaceship, Vimana (2) | Vimana Ship, Drone Shooter (2) |
| Characters | Nysera (Common), Aaryaveer (Epic) (2) | 6 remaining |

Rationale: Nysera is beginner-friendly (Long-Range, Common), Aaryaveer is a balanced all-rounder (Epic) that lets judges feel the rarity multiplier difference. Legendary characters stay locked to create intrigue and showcase the collectible depth.

### Wallet Balance

Owner wallet `0x9824...8D59` topped up to **1.809 AVAX** on Fuji (received 1.8 AVAX from another wallet). Sufficient for hundreds of future transactions.

### Server Wallet Note

Thirdweb server wallet `0x2bBc1C32224a347eaF8d10cAFaF77F3aBCA2551f` (smart wallet: `0x2355169625b332ecB1F86634e4c37170050a03E7`) was granted MINTER_ROLE on the contract but cannot be used for minting due to EIP-7702 incompatibility with Avalanche Fuji. All minting done through the owner wallet via Thirdweb dashboard.

### Key Lessons

- **Thirdweb server wallets use EIP-7702 bundler** — Not supported on all chains. Avalanche Fuji rejects all server wallet transactions with "Chain does not support EIP-7702". Workaround: use the owner wallet directly via dashboard UI.
- **ERC-1155 allows post-mint supply increases** — Just call `mintTo()` with the same token ID. Unlike ERC-721, token IDs are not unique per unit.
- **`setTokenURI` updates metadata** — Name, description, and attributes are in the JSON the URI points to. Change the URI to update metadata. But once `freezeMetadata()` is called, it's permanent.
- **Rarity assignment should combine visual + stats** — Pure visual ranking or pure stat ranking each miss half the picture. Best results come from weighting both: visually impressive characters with strong stat profiles get Legendary, balanced stats with simpler designs get Common.

---

## What's Left

### Completed
- [x] Test Host mode still works
- [x] Deploy dedicated server build to VPS
- [x] Clients connect and play via VPS dedicated server
- [x] Room code UI — Create Room / Join Room with 4-char codes
- [x] Scene flow: Bootstrap → Menu → Gameplay with Enter Battle button
- [x] Deferred player spawning (no ships in menu scene)
- [x] Ships spawn on spline correctly after Enter Battle
- [x] UI panel isolation
- [x] ParrelSync room code flow working end-to-end
- [x] Camera follows host ship after Enter Battle
- [x] Client auto-loads gameplay when host starts match
- [x] Ships spawn at different positions
- [x] Synced countdown timer (5, 4, 3, 2, 1, GO!)
- [x] Battle rewards — correct placement for destroyed and surviving players
- [x] BattleZoneController scene NetworkObject registration
- [x] Ship lock feature in selection screen
- [x] Click-order dual ship selection (Primary + Secondary slots)
- [x] Character selection UI with dual-roster layout (4 slots, 2 per ship)
- [x] CharacterNFTManager with CharacterDefinition, rarity multipliers, ownership
- [x] CharacterCardUI with lock overlay, badges, rarity colors
- [x] Character improvements system — 8 unique stat profiles, multiplicative stacking formula
- [x] CreateCharacterDataAssets editor script
- [x] mint_count_and_improvements.md documentation
- [x] Character sprite analysis and rarity assignment
- [x] All 8 character NFTs minted on-chain (Token IDs 4–11)
- [x] On-chain verification of all minted tokens
- [x] Owner wallet funded (1.809 AVAX)

### Remaining
- [ ] Update CharacterSelectionUI to set Nysera + Aaryaveer as default unlocked
- [ ] Wire CharacterDefinition's characterStats and RarityMultiplier into AircraftCharacterManager (bridge NFT selection → gameplay)
- [ ] Run CreateCharacterDataAssets editor script in Unity
- [ ] Wire CharacterData assets to CharacterDefinition entries in Inspector
- [ ] Upload character sprite images to NFT metadata via Thirdweb dashboard
- [ ] Test position sync between two clients connected to VPS
- [ ] Rebuild Linux server with latest changes and deploy to VPS
- [ ] Test with more than 2 clients
- [ ] Set up systemd auto-restart on VPS
- [ ] Part B: Scale from 4 to 20-30 players per match

---

## Session 8 — March 7, 2026: Character & Ship Lore Popup System

### Overview

Built a complete two-level lore popup system for both characters and ships. Clicking any character or ship icon (even locked/unowned) opens a programmatically-built popup with rich lore data. Created ScriptableObject-based lore data for all 8 characters and 4 ships with world-building content tied to the 9 Kingdoms (Sarathi) and 9 Countries (Atom Riders).

### Feature: Character Lore Popup (CharacterLorePopup.cs)

Auto-builds its own UI from code at runtime (no prefab needed). Matches PostMatchRewardUI style with dark panels and gold accents.

**Two-level design:**
- Level 1: Name, tagline, rarity, role, region/faction, power class, terrain mastery, Thara resonance + 4 detail buttons
- Level 2: Backstory, Strengths (green bullets), Weaknesses (red bullets), Rivals (orange bullets)

**Key implementation details:**
- Panel size: 640x580, detail panel: 580x340, body text: 540x280
- `popupOffset` Inspector field (Vector2) to position popup anywhere on screen
- Non-blocking dark overlay (`raycastTarget = false`) so clicking another icon switches the popup without closing first
- `.Trim()` on name comparison to handle trailing whitespace in displayName fields

### Feature: Character Lore Data (CharacterLoreData.cs)

ScriptableObject with fields: characterName, tagline, rarity, role, regionLabel, regionName, factionLabel, factionName, powerClass, terrainMastery, tharaResonance, backstory, strengths, weaknesses, rivals.

**8 character lore assets created** (`Assets/GV/Data/CharacterLore/`):

| Character | Rarity | Region | Faction | Role |
|-----------|--------|--------|---------|------|
| Aaryaveer | Epic | Nandana | Devas | Balanced Striker |
| Ishvaya | Rare | Ratna Shila | Yakshas | Precision/Control |
| Vyanika | Epic | Garuda Vana | Garudajas | Mobility/Evasive |
| Roudra | Legendary | Daitya Giri | Asuras | Heavy Burst |
| Zorvan | Rare | Aurkana | NEO-TERRANs | Missile Hunter |
| Kaevik | Legendary | Silkvale | QiNEXIs | Rapid Assault |
| Nysera | Common | Inkara | INTARIs | Long-Range |
| Virexa | Common | Elgorn Fall | ERGOVANs | Glass Cannon |

### Feature: Icon Click Overlay Pattern (CharacterCardUI.cs, ShipCardUI.cs)

**Problem:** The selectButton covers the entire card, intercepting all clicks. Adding a Button to iconImage directly doesn't work because it renders underneath the selectButton in raycast order.

**Solution:** Created a transparent `IconClickOverlay` GameObject as the LAST child of the card (renders on top of selectButton in raycast order), matching the icon's exact position/size. Works for all cards including locked/unowned.

**Applied to both CharacterCardUI and ShipCardUI** with identical pattern:
- `BuildIconClickOverlay()` creates transparent Image + Button as last sibling
- Handles nested icon transforms by converting world corners to local card space
- Always interactable regardless of lock/ownership state

### Feature: Ship Lore Popup (ShipLorePopup.cs)

Same auto-build pattern as CharacterLorePopup but with ship-specific fields.

**Two-level design:**
- Level 1: Ship name, tagline, rarity + ship class, origin/faction, power system/combat role, special ability (cyan, word wrap) + 4 detail buttons
- Level 2: Backstory, Strengths (green), Weaknesses (red), History (orange)

**Sizing:** Panel 760x720, detail panel 700x440, body text 660x380. Buttons 130x42. Inspector `popupOffset` field.

### Feature: Ship Lore Data (ShipLoreData.cs)

ScriptableObject with fields: shipName, tagline, rarity, shipClass, originLabel, originName, factionLabel, factionName, powerSystem, combatRole, specialAbility, backstory, strengths, weaknesses, history.

**4 ship lore assets created** (`Assets/GV/Data/ShipLore/`):

| Ship | Rarity | Class | Origin | Pilots | Special Ability |
|------|--------|-------|--------|--------|-----------------|
| Spaceship | Rare | Nuclear Strike Vessel | Earth Countries in Akasya | Atom Riders | Reactor Overclock — burst damage at hull cost |
| Vimana | Epic | Ancient Divine Carrier | Akasa Raajyas | Sarathi | Thara Surge — area pulse disrupts targeting, supercharges Astra |
| Vimana Ship | Rare | Hybrid Assault Carrier | Akasa Raajyas + Earth Countries | Sarathi & Atom Riders | Resonance Shift — toggles Thara/Nuclear mode (3s vulnerable transition) |
| Drone Shooter | Uncommon | Autonomous Strike Drone | Inkara (INTARi Network) | INTARi Remote Corps | Swarm Protocol — splits into 3 sub-drones for 8 seconds |

### Ship Lore World-Building

**Spaceship:** Classified NEO-TERRAn weapons program. 9 Countries pooled technology — Aurkana's targeting, Silkvale's weapons, Inkara's sensors, Elgorn Fall's reactors, Saharai's heat shielding. Atom Riders are independent pilots (engineers, deserters, mercenaries).

**Vimana:** Ancient pre-Kingdom vessels discovered in sealed chambers under Garuda Vana. Divine-alloy hulls, Thara-powered cores. Bond with pilot through Thara Resonance — permanent bond, no backup pilots. If pilot dies, Vimana goes silent for generations.

**Vimana Ship (Hybrid):** Born from a stalemate at the Battle of Garuda Vana's Lower Peaks. Rogue Garudaja + QiNEXi + INTARi coalition stole a dormant Vimana and spliced Nuclear-P reactor housing alongside its Thara Core. First ignition failed (resonance cascade leveled the hangar). Second attempt created the only ship that runs on two hearts.

**Drone Shooter:** Started as INTARi surveillance drones — Atom Riders and ERGOVAns kept shooting them down. INTARi armed them. Mass-produced, expendable, no pilot. Manufactured in 3 days from standard components. Both Kingdoms and Countries tried to ban autonomous weapons — INTARi ignored both.

### Wiring Changes

**CharacterSelectionUI.cs:**
- Added `[SerializeField] private CharacterLorePopup lorePopup`
- Updated `PopulateAllCards()` to pass `OnCharacterIconClicked` callback
- Added `OnCharacterIconClicked(CharacterDefinition character)` handler

**ShipSelectionUI.cs:**
- Added `[SerializeField] private ShipLorePopup lorePopup`
- Updated `PopulateShipCards()` to pass `OnShipIconClicked` callback
- Added `OnShipIconClicked(ShipDefinition ship)` handler with debug logging

### Bugs Fixed This Session

1. **No popup on icon click** — selectButton covers entire card, blocking icon clicks. Fix: transparent overlay as last child sits on top in raycast order.
2. **Level 2 detail text blank** — ScrollRect/ContentSizeFitter/VerticalLayoutGroup fighting each other, body text had zero height. Fix: replaced with simple fixed-size TMP_Text.
3. **Unicode box characters** — LiberationSans SDF doesn't support U+25B8 (bullet), U+2715 (X mark), U+2014 (em dash), U+2019 (curly apostrophe). Fix: replaced with ASCII equivalents (`-`, `X`, `-`, `'`) in code and all 8 lore assets.
4. **Zorvan popup not working** — trailing space in displayName `'Zorvan '`. Fix: `.Trim()` on both sides of name comparison.
5. **Popup won't switch without closing** — dark overlay blocking clicks to cards underneath. Fix: `overlay.GetComponent<Image>().raycastTarget = false`.
6. **ShipLoreData "Missing (Mono Script)"** — Asset files created with placeholder GUID that didn't match actual ShipLoreData.cs.meta GUID. Fix: updated asset GUIDs to `604b577af8a43c942bd5251f25245dcd`. Vimana asset required full delete + recreate due to Unity caching the broken reference.
7. **CS0618 deprecation warning** — `enableWordWrapping` obsolete in TMP. Fix: wrapped with `#pragma warning disable/restore CS0618` (safe approach since `TextWrappingModes` enum may not exist in all TMP versions).

### Files Created

| File | Description |
|------|-------------|
| CharacterLoreData.cs | ScriptableObject for character lore fields |
| CharacterLorePopup.cs | Auto-built two-level character popup UI |
| ShipLoreData.cs | ScriptableObject for ship lore fields |
| ShipLorePopup.cs | Auto-built two-level ship popup UI |
| 8x Character Lore .asset files | `Assets/GV/Data/CharacterLore/` |
| 4x Ship Lore .asset files | `Assets/GV/Data/ShipLore/` |

### Files Modified

| File | Changes |
|------|---------|
| CharacterCardUI.cs | Added `_onIconClick` callback, `BuildIconClickOverlay()` method |
| CharacterSelectionUI.cs | Added lorePopup field, icon click handler |
| ShipCardUI.cs | Added `_onIconClick` callback, `BuildIconClickOverlay()` method |
| ShipSelectionUI.cs | Added lorePopup field, icon click handler with debug logging |

### Key Lessons

- **Raycast order follows sibling order** — last child in hierarchy receives raycasts first. Use `SetAsLastSibling()` to put overlays on top.
- **LiberationSans SDF has limited Unicode** — Stick to ASCII for UI text. No em dashes, curly quotes, triangle bullets, or special X marks.
- **Unity caches broken ScriptableObject references** — Changing the GUID in an .asset file may not be enough. Sometimes you need to delete both .asset and .meta files and recreate from scratch.
- **Pragma warning disable is safer than API migration** — When replacing deprecated APIs, the new API might not exist in all versions. Suppressing the warning keeps code compiling everywhere.
- **Non-blocking overlays enable popup switching** — Setting `raycastTarget = false` on the dark overlay lets clicks pass through to cards underneath, enabling seamless popup switching without close-reopen.

---

## Session 9 — March 8, 2026: RoomManager Architecture & Multi-Room Dedicated Server

### Overview

Replaced the single fixed-session dedicated server with a **multi-room architecture**. The VPS now runs a `RoomManager` that creates rooms on demand — each room is an independent Fusion session with its own NetworkRunner and NetworkManager. Clients create and join rooms via an HTTP API + Fusion sessions. **Host mode has been completely removed** — all multiplayer goes through the VPS dedicated server.

### Architecture

```
VPS Server Process
├── RoomManager (singleton, HTTP API on port 7350)
│   ├── Room "ABCD" → NetworkManager + NetworkRunner (GameMode.Server)
│   ├── Room "WXYZ" → NetworkManager + NetworkRunner (GameMode.Server)
│   └── Room "QRST" → NetworkManager + NetworkRunner (GameMode.Server)
```

**Client flow — Create Room:**
1. Player clicks "Create Room"
2. Client sends `POST http://VPS_IP:7350/create`
3. RoomManager creates a new NetworkManager instance + Fusion session with unique 4-char code
4. Returns `{"code":"ABCD"}` to client
5. Client joins Fusion session "ABCD" as `GameMode.Client`
6. Player shares code with friends

**Client flow — Join Room:**
1. Player types room code (e.g. "ABCD") and clicks "Join Room"
2. Client joins Fusion session "ABCD" as `GameMode.Client`
3. All players see Enter Battle button (dedicated server mode)

**Enter Battle flow (carried over from earlier in this session):**
1. Any client clicks Enter Battle → sends `START_MATCH` ("STRT") raw data to server
2. Server receives it → sends `COUNTDOWN` ("CNTD") to all clients
3. Server runs `DedicatedServerCountdownThenLoad()` coroutine
4. After countdown, server sends `SCENE_LOAD` ("LOAD") to all clients
5. Clients load gameplay scene, server spawns players

### Files Created

| File | Description |
|------|-------------|
| `Assets/GV/Scripts/Network/RoomManager.cs` | Multi-room manager for dedicated server. HTTP API (port 7350) for room creation/listing/querying. Creates per-room NetworkManager instances. Auto-cleans empty rooms after timeout. |

### Files Modified

| File | Changes |
|------|---------|
| **NetworkManager.cs** | Removed `StartHost()` and all `GameMode.Host` references. `OnCreateRoomClicked()` now calls VPS HTTP API (`POST /create`) via `UnityWebRequest`, parses room code, joins as client. `OnJoinRoomClicked()` always sets `_connectedToDedicatedServer = true`. `ShowConnectedUI()` always shows Enter Battle to all clients. `LoadGameplay()` simplified — only client→server path (no Host branch). Added `StartServerForRoom(code, maxPlayers, callback)` for RoomManager to call per-room. Added `_isRoomManagerControlled` flag to skip singleton enforcement (multiple instances on server). Added `vpsRoomApiUrl` field for VPS HTTP endpoint. Added `using UnityEngine.Networking` for `UnityWebRequest`. Removed `autoHostInBuild` auto-start. |
| **Web3Bootstrap.cs** | Added `roomManagerPrefab` field. On dedicated server, instantiates RoomManager instead of NetworkManager directly. RoomManager handles all room lifecycle. |

### RoomManager HTTP API

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/create` | POST | Creates a new room, returns `{"code":"XXXX"}` |
| `/rooms` | GET | Lists all active rooms with player counts |
| `/rooms/{code}` | GET | Gets info for a specific room |
| `/rooms/{code}` | DELETE | Shuts down a room (admin/debug) |
| `/health` | GET | Health check — returns status and room count |

### Enter Battle Flow (implemented earlier in this session)

Added raw data command system for dedicated server match start:

| Constant | Key | Magic Bytes | Direction | Purpose |
|----------|-----|-------------|-----------|---------|
| `START_MATCH_KEY/MAGIC` | "GVSM" | "STRT" | Client → Server | Client requests match start |
| `COUNTDOWN_KEY/MAGIC` | "GVCD" | "CNTD" | Server → Client | Server tells clients to start countdown |
| `SCENE_LOAD_KEY/MAGIC` | "GVSC" | "LOAD" | Server → Client | Server tells clients to load gameplay scene |

Added `DedicatedServerCountdownThenLoad()` coroutine on server side — counts down, sends LOAD, marks `_inGameplayScene = true`, spawns pending players. Server never changes scene (already in gameplay).

### Deployment Changes

- VPS firewall: opened port **7350/tcp** for HTTP room API
- Systemd service unchanged (still runs single process with `-batchmode -nographics -server`)
- Unity Player Settings: must set **"Allow downloads over HTTP" → "Always allowed"** (Unity 6 blocks insecure connections by default, needed for `http://VPS_IP:7350` calls)

### Bug Fixes During Deployment (earlier in this session)

1. **NetworkManager never instantiated on dedicated server** — Fusion never started because NetworkManager lives in menu scene, Web3Bootstrap skips menu. Fix: added `networkManagerPrefab` field to Web3Bootstrap, instantiate before loading gameplay.
2. **Runner destroyed by scene change** — `StartGame` returned Ok but Runner was null due to `SceneManager.LoadScene` destroying it. Fix: added `DontDestroyOnLoad(Runner.gameObject)` after creation.
3. **Session name casing mismatch** — Server used "GV_Race", client uppercased to "GV_RACE". Fix: changed default to "GV2S" (4 chars, uppercase). Now irrelevant since RoomManager generates unique codes.
4. **Unity 6 blocks HTTP** — `UnityWebRequest` to `http://` throws `InsecureConnectionNotAllowed`. Fix: Player Settings → Allow downloads over HTTP → Always allowed.
5. **DontDestroyOnLoad warning** — RoomManager prefab nested under another GameObject. Harmless but should be root-level.

### Unity Inspector Setup Required

1. **Create RoomManager prefab**: Empty GameObject → add RoomManager component → set `networkManagerPrefab` to existing Network Manager prefab → save as prefab in `Assets/GV/Prefabs_GV/Network/`
2. **Web3Bootstrap Inspector**: Assign RoomManager prefab to `roomManagerPrefab` field
3. **NetworkManager Inspector**: Set `vpsRoomApiUrl` to `http://187.124.96.178:7350`
4. **Player Settings**: Allow downloads over HTTP → Always allowed

### Key Lessons

- **Multiple Fusion sessions per process** — Fusion 2 supports multiple NetworkRunners in one process, each hosting a separate session. This enables multi-room dedicated servers without spawning separate processes.
- **HTTP signaling for room creation** — Clients can't communicate with the server before joining a Fusion session. A lightweight HTTP API bridges this gap for out-of-band room management.
- **Singleton pattern breaks with multi-instance** — NetworkManager's `Instance` singleton must be bypassed when RoomManager creates per-room instances. Added `_isRoomManagerControlled` flag.
- **Unity 6 insecure connection policy** — `UnityWebRequest` blocks `http://` by default. Must explicitly allow in Player Settings.
- **Thread-safe Unity API access** — HTTP requests arrive on background threads but Unity API calls (Instantiate, etc.) must run on the main thread. RoomManager queues actions with `ManualResetEvent` synchronization.

### Remaining Issues / Next Steps

- ~~Test full flow: Create Room → Join Room → Enter Battle → gameplay → ship spawning~~ (tested — two bugs found and fixed below)
- Verify multiple simultaneous rooms work on VPS
- Room cleanup after match end (currently auto-cleans empty rooms after 30s timeout)
- Consider adding room browser UI (GET /rooms endpoint exists but no client UI yet)
- DontDestroyOnLoad warning for RoomManager prefab (make it root-level)

---

## Session 10 — Bug Fixes (March 8, 2026)

### Bug Fix 1: Create Room Immediately Loads Gameplay Scene

**Problem:** After clicking "Create Room", the game skipped the lobby UI (room code, player count, Enter Battle button) and immediately loaded the gameplay scene. The lobby UI appeared for less than a second before being overridden.

**Root Cause:** `NetworkSceneManagerDefault` (Fusion's built-in scene synchronizer) was always added to `StartGameArgs`. The VPS server is already in the gameplay scene (loaded by `Web3Bootstrap`). When a client connects, `NetworkSceneManagerDefault` detects the scene mismatch (client=menu, server=gameplay) and forces the client to load the gameplay scene — bypassing the manual LOAD/COUNTDOWN/START_MATCH command flow.

**Fix (NetworkManager.cs, `StartGame()`):** Conditionally skip `NetworkSceneManagerDefault` when `_isRoomManagerControlled` (server) or `_connectedToDedicatedServer` (client) is true. Scene loading is handled manually via raw data commands, and all spawn/registration logic uses Unity's native `SceneManager.sceneLoaded` callback, so this is safe.

### Bug Fix 2: Violent Aircraft Swap on Gameplay Start

**Problem:** After gameplay starts, aircraft meshes swap violently 2-3 times within less than a second, without any player input.

**Root Cause:** `_prevSyncIsA` (the local tracker for detecting networked swap changes) defaults to `true` but was never initialized on the client (non-state-authority). Meanwhile, `SyncIsAActive` is a `[Networked] NetworkBool` that defaults to `false`. This mismatch caused `Render()` to detect phantom changes:

1. Frame 1: `SyncIsAActive=false` (NetworkBool default) ≠ `_prevSyncIsA=true` → **Swap #1** (spurious A→B)
2. Server writes `SyncIsAActive=true` (from meshSwap default) → **Swap #2** (B→A)
3. Client's NFT RPC round-trips, server writes `SyncIsAActive=false` → **Swap #3** (A→B)

Additionally, when the client called `RPC_SendSwapState(wantA)` during NFT→mesh link, it never updated `_prevSyncIsA`, so the round-trip was detected as a "new" change.

**Fix (NetworkedSpaceshipBridge.cs, `Spawned()`):**
1. Added `else` branch to initialize `_prevSyncIsA = (bool)SyncIsAActive` on non-state-authority (client), preventing the first-frame phantom change detection.
2. After `RPC_SendSwapState(wantA)` in the NFT→mesh link section, also set `_prevSyncIsA = wantA` so the RPC round-trip doesn't trigger a duplicate swap in `Render()`.

### Files Modified

- `Assets/GV/Scripts/Network/NetworkManager.cs` — Conditional `NetworkSceneManagerDefault` in `StartGame()`
- `Assets/GV/Scripts/Network/NetworkedSpaceshipBridge.cs` — `_prevSyncIsA` initialization in `Spawned()`

---

## Session — March 8, 2026: Scene Loading & Ship Spawn Timing Fix

### Bug: Gameplay Scene Loads Before Pressing Enter Battle

**Symptom:** After clicking Create Room, the gameplay scene (`Room_Tests_2`) loads immediately and ships spawn — the player can fire and hear sounds while still on the lobby UI. Gameplay should only start after clicking Enter Battle.

### Investigation & Failed Approaches

**Approach 1: NoOpSceneManager (INetworkSceneManager)**
Created `NoOpSceneManager.cs` — a custom `INetworkSceneManager` that returns `true` from `OnSceneInfoChanged` (telling Fusion "I'll handle it") but does nothing, preventing Fusion from auto-syncing scenes.

- Confirmed via debug logs: NoOpSceneManager IS correctly installed on the Runner GO, IS receiving `OnSceneInfoChanged(source=Remote)` calls, and IS returning true
- **Problem:** Fusion internally shuts down our NoOpSceneManager after a short time. After shutdown, Fusion loads the gameplay scene anyway. The `[NoOpSceneManager] Shutdown called!` log confirmed this.
- Added the NoOpSceneManager to `Runner.gameObject` (not `this.gameObject`) since the Runner lives on a separate `new GameObject("NetworkRunner")` — confirmed by `Runner.gameObject == this.gameObject? False`
- Added post-StartGame cleanup to remove any auto-added `NetworkSceneManagerDefault`
- None of this prevented Fusion from eventually loading the scene

**Approach 2: Fake Scene Index**
Tried having the server advertise build index 0 (pretending to be on the menu scene) in `StartGameArgs.Scene` to eliminate the scene mismatch between server and client.

- **Problem:** The menu scene (`SCK_MainMenu_3`) is at buildIndex=1, not 0. More importantly, Fusion overrides `StartGameArgs.Scene` with the server's actual scene info internally. `OnSceneInfoChanged(source=Remote)` still fired with the server's real gameplay scene.

**Approach 3: Guard OnUnitySceneLoaded with `_manualSceneLoadRequested` flag**
Added a flag that's only set to `true` right before our intentional `SceneManager.LoadScene()` calls (after Enter Battle countdown). Both `OnUnitySceneLoaded` and `OnSceneLoadDone` check this flag.

- **Problem:** This correctly guarded the CLIENT from setting `_inGameplayScene = true`, but the ships were being spawned on the SERVER (which has `_inGameplayScene = true` set in `StartServerForRoom`). Fusion replicates networked ship objects from server to client regardless of client-side flags.

### Root Cause

The dedicated server set `_inGameplayScene = true` immediately in `StartServerForRoom()`, causing `OnPlayerJoined()` to call `SpawnPlayer()` the instant a client connects. Fusion then replicates the networked ship objects to the client. No amount of client-side guarding could prevent this — the ships came from the server.

Additionally, `OnSceneLoadDone` had a `!IsDedicatedServer` bypass in its guard, meaning the dedicated server's initial `OnSceneLoadDone` (when already in the gameplay scene) would also set `_inGameplayScene = true` and trigger spawning.

### Fix Applied

**NetworkManager.cs:**

1. **`StartServerForRoom()`**: Changed `_inGameplayScene = true` → `_inGameplayScene = false`. Players who join are now queued in `_pendingSpawns` instead of spawned immediately.

2. **`DedicatedServerCountdownThenLoad()`**: After the countdown finishes (triggered by START_MATCH from client), the server now:
   - Sets `_inGameplayScene = true`
   - Calls `TryFindSpawnSpline()`
   - Spawns ALL queued players from `_pendingSpawns` plus any active players without ships
   - THEN sends LOAD command to clients

3. **`OnSceneLoadDone()` guard**: Changed from `!IsDedicatedServer` bypass to `!_inGameplayScene` check. The server's initial `OnSceneLoadDone` is now blocked because `_inGameplayScene` starts as false.

4. **`OnUnitySceneLoaded()` guard**: Added `_manualSceneLoadRequested` check — if Fusion auto-loads the gameplay scene on the client, the handler returns without activating gameplay.

5. **`_roomFlowActive` flag**: Persists through `OnShutdown` (unlike `_connectedToDedicatedServer` which gets reset). Ensures NoOpSceneManager is always used for room flows even after unexpected Runner shutdown/restart.

6. **`_manualSceneLoadRequested` flag**: Set to `true` only before intentional `SceneManager.LoadScene()` calls (3 locations: client countdown→load, host load, client LOAD command from host). Reset after processing or on shutdown.

**NoOpSceneManager.cs:**
- `Shutdown()` now logs full stack trace via `System.Environment.StackTrace`
- `OnSceneInfoChanged()` now logs `sceneCount` for better diagnostics

### Current Status

- Gameplay scene no longer loads before pressing Enter Battle ✓
- **Ship spawning after Enter Battle not yet working** — ships are queued on the server but the spawn timing after START_MATCH needs debugging (to be fixed in next session)
- Aircraft swap bug fix from previous session still working ✓
- KeyNotFoundException fix in Tracker.cs still working ✓

### Files Modified

- `Assets/GV/Scripts/Network/NetworkManager.cs` — Delayed ship spawning, scene load guards, `_roomFlowActive` + `_manualSceneLoadRequested` flags
- `Assets/GV/Scripts/Network/NoOpSceneManager.cs` — Shutdown stack trace logging, improved OnSceneInfoChanged logging
- `Assets/GV/Scripts/Network/NetworkedSpaceshipBridge.cs` — Per-frame RPC spam fix + grace period (from earlier in session)

---

## Session — March 9, 2026: Ship Spawn Reliability & START_MATCH Delivery

### Problem

After the previous session's work, pressing Enter Battle would sometimes spawn ships and sometimes not. Multiple cascading bugs were discovered and fixed across this session.

### Bug 1: DUAL INSTANCE — Fusion Callbacks Firing on Destroyed NetworkManager

**Symptom:** On the 2nd room creation, two rooms were created (e.g., FVKU + CYWL). The second room's `StartServerForRoom` threw `NullReferenceException`. Callbacks like `TriggerMatchStart` and `OnPlayerJoined` fired on the destroyed instance (instanceID=0) instead of the live `Instance` singleton.

**Root cause:** When RoomManager creates a new room, a duplicate NetworkManager gets instantiated then destroyed, but Fusion callbacks still fire on it. `TriggerMatchStart` set `_countdownActive` and `_matchStartTriggered` flags on the dead instance, but `Update()` only runs on the live instance — so the countdown never advanced.

**Fix:**
- `TriggerMatchStart()` — redirects all flag operations to `Instance` (the live singleton) instead of `this`. Logs a warning when redirection happens.
- `OnPlayerJoined()` — queues players on `liveInst._pendingSpawns` instead of `this._pendingSpawns`
- Both 4-byte and 38-byte START_MATCH handlers — guard checks (`_countdownActive`, `_inGameplayScene`) performed on `Instance` instead of `this`

### Bug 2: Fusion ReliableKey Coalescing — START_MATCH Never Delivered

**Symptom:** Client sent START_MATCH 60 times (confirmed in client Unity logs), but server received zero. Diagnostic logs for "NON-STANDARD magic" and "Received 4-byte message" never appeared in server output.

**Root cause:** Fusion's `SendReliableDataToServer` with the same `ReliableKey` coalesces/deduplicates messages. The 60 magic=2099 input frames shared `INPUT_DATA_KEY` with regular magic=2042 frames. Fusion delivered only the latest value (2042), silently dropping all 2099 frames. The 4-byte `START_MATCH_KEY` also got crowded out when `INPUT_DATA_KEY` flooded the reliable channel.

**Fix:** Created a completely separate `ReliableKey`:
- New key: `START_MATCH_INPUT_KEY = ReliableKey.FromInts(0x47, 0x56, 0x53, 0x49)` (ASCII "GVSI")
- Client sends 38-byte packets (37 bytes input data + 1 marker byte `0xFF`) on this dedicated key
- Server detects `data.Count == 38` as START_MATCH packets, separate from regular 37-byte input
- The 4-byte "STRT" channel remains as a bonus fallback

**Result after fix — successful delivery on both tries:**
- 4-byte "STRT" arrived first → triggered `TriggerMatchStart`
- Five 38-byte packets arrived right after (rest skipped since countdown already active)
- Ship spawned successfully in 0.5s after START_MATCH

### Bug 3: Server Countdown Too Long (5.3s)

**Symptom:** Server waited 5.3 seconds to spawn ships, but client already loaded the gameplay scene after its own 5-second countdown. Steer values were changing (client sending input) before START_MATCH was received, and the ship appeared ~5s late.

**Fix:** Reduced server-side spawn delay from `COUNTDOWN_SECONDS + 0.3f` (5.3s) to `0.5f`. The server now spawns almost immediately after receiving START_MATCH — the client handles its own visual countdown independently.

### Bug 4: START_MATCH_SEND_REPEATS Too Low (3)

**Symptom:** Only 3 frames of the START_MATCH signal were sent. With network batching and Fusion's internal timing, all 3 could be lost before delivery.

**Fix:** Increased `START_MATCH_SEND_REPEATS` from 3 to 60 (~1 second of frames). Combined with the separate ReliableKey fix, this ensures reliable delivery.

### Post-Fix: Loading Screen for Scene Load Delay

**Symptom:** After all spawn bugs were fixed, ships spawned correctly on both 1st and 2nd tries, but the ship appeared ~10 seconds after the "GO!" countdown finished. The server spawns quickly (0.5s) but the client takes time to load the gameplay scene and receive the Fusion-replicated ship.

**Fix:** Added a persistent loading screen:
- `ShowLoadingScreen()` — creates a `DontDestroyOnLoad` Canvas with semi-transparent black background (85% opacity) and "Loading..." text (TMPro, white, 48pt). `sortingOrder=9999` ensures it renders on top of everything.
- Called in `CountdownThenLoad()` right before `SceneManager.LoadScene()`
- `HideLoadingScreen()` — destroys the loading screen GameObject
- Detection in `Update()`: iterates `Runner.GetAllNetworkObjects()` looking for any `NetworkObject` with `InputAuthority == Runner.LocalPlayer` (excluding NetworkManager itself). Once found, calls `HideLoadingScreen()`.
- Initial attempt used `Runner.GetPlayerObject(Runner.LocalPlayer)` which always returned null because `Runner.SetPlayerObject()` was never called in `SpawnPlayer()`. Fixed by switching to the `GetAllNetworkObjects()` iteration approach.

### Countdown Reduced: 5s → 3s

Reduced `COUNTDOWN_SECONDS` from 5 to 3. The "3…2…1…GO!" sequence is now shorter, reducing total wait time before the match starts.

### All Code Changes (NetworkManager.cs)

1. `START_MATCH_SEND_REPEATS`: 3 → 60
2. `COUNTDOWN_SECONDS`: 5 → 3
3. Server spawn delay in `UpdateMatchStartCountdown`: `COUNTDOWN_SECONDS + 0.3f` → `0.5f`
4. `TriggerMatchStart()`: redirects to live `Instance` singleton
5. `OnPlayerJoined()`: queues on `liveInst._pendingSpawns`
6. 4-byte START_MATCH handler: guard checks on `Instance`
7. New `START_MATCH_INPUT_KEY` ReliableKey ("GVSI")
8. Client sends 38-byte packets on separate key (37 input + 1 marker byte `0xFF`)
9. Server detects 38-byte packets as START_MATCH
10. Debug logs: "NON-STANDARD magic", "Received 4-byte message", "Received 38-byte START_MATCH packet"
11. `ShowLoadingScreen()` / `HideLoadingScreen()` with `DontDestroyOnLoad` Canvas
12. `Update()` ship detection via `GetAllNetworkObjects()` → hides loading screen when ship appears
13. `_inGameplayScene = true` set before scene load in `CountdownThenLoad`

### Current Status

- Ship spawning works reliably on both 1st and 2nd room creations ✓
- START_MATCH delivered via dual channels (4-byte "STRT" + 38-byte "GVSI") ✓
- DUAL INSTANCE issue handled — all callbacks redirect to live Instance ✓
- Server spawns in 0.5s after START_MATCH ✓
- Client countdown reduced to 3 seconds ✓
- Loading screen shows during scene load gap, hides when ship appears ✓
- Debug logs in place for future diagnostics ✓

### Files Modified

- `Assets/GV/Scripts/Network/NetworkManager.cs` — All changes listed above

---

## Session — March 9, 2026: Spawn-Before-Load Race Condition & CLIENT_READY Handshake

### Problem

Timing sync was working but spawning was inconsistent — sometimes ships spawned, sometimes didn't, sometimes both screens stuck on loading. Diagnosed via `timingsynclogs.txt` through `timingsynclogs_4.txt` (server-side logs across multiple deploy/test cycles).

### Root Cause Chain (4 bugs, each uncovered after fixing the previous)

**Bug 1 — Spawn-before-load race condition (original issue)**
The server spawned `NetworkObject`s before clients had loaded the gameplay scene. With `NoOpSceneManager`, Fusion doesn't manage scene transitions, so the server had no way to know clients were still on the lobby scene. Spawned ships had no scene to exist in on the client side → stuck on loading screen.

**Bug 2 — CLIENT_READY signal silently dropped by Fusion**
Implemented a CLIENT_READY handshake: server sends SCENE_LOAD, waits for clients to acknowledge scene load, then spawns. But the CLIENT_READY signal (magic=777777) was sent as a **separate** `SendReliableDataToServer()` call on `INPUT_DATA_KEY` — the same key used for regular input in the same `Update()` frame. Fusion only delivers one message per key per tick, so CLIENT_READY was always overwritten by the regular input packet (magic=2042). Server never received magic=777777.

**Bug 3 — `SceneManager.LoadScene` called from inside `OnReliableDataReceived`**
The server flooded SCENE_LOAD signals every frame during the wait phase. The client received SCENE_LOAD before its 3s countdown finished (`_inGameplayScene` was still false), triggering `SceneManager.LoadScene()` **directly from inside Fusion's `OnReliableDataReceived` callback**. Synchronous scene load in the middle of Fusion's tick processing corrupted the runner's internal state, breaking all subsequent networking — client could no longer send any data.

**Bug 4 — Synchronous scene load kills Fusion connection**
Even after fixing bugs 2 and 3, the synchronous `SceneManager.LoadScene()` in `CountdownThenLoad()` blocked Unity's entire game loop for 5-7 seconds while loading the heavy gameplay scene. During this time: no `Update()` runs, no input sent, no network ticks. Fusion interprets 8-10s of silence (3.3s countdown + ~6s scene load) as a disconnection → "Player left" on server.

### Fixes Applied

#### 1. CLIENT_READY Handshake System (NetworkManager.cs)

**Server-side (UpdateMatchStartCountdown — 3-phase flow):**
- **Phase 1**: Send COUNTDOWN signals to all clients every frame (unchanged)
- **Phase 2**: When countdown finishes (3.5s), send SCENE_LOAD **once** on both channels (37-byte INPUT_DATA_KEY + 4-byte SCENE_LOAD_KEY). Set `_waitingForClientsReady = true`. Do NOT spawn yet.
- **Phase 3**: Wait for CLIENT_READY from all clients. Track `_clientsReady` HashSet. When all connected clients are ready (or 15s timeout), call `ServerSpawnAllPendingPlayers()`.

**Client-side (CountdownThenLoad + Update):**
- After scene loads, `OnUnitySceneLoaded` sets `_clientSendingReady = true`
- `Update()` embeds CLIENT_READY in the regular input magic number: `playerId * 1000 + 77` (e.g. 2077) instead of the normal `+ 42`. No separate send needed — piggybacks on the already-delivering input packet.
- Send duration: 10 seconds (no extra network cost since it's embedded in existing input)

**Server-side detection (OnReliableDataReceived):**
- Checks `inputData.magicNumber % 1000 == 77` → adds player to `_clientsReady`
- Strips signal before storing as regular input: `magic = (magic / 1000) * 1000 + 42`
- Same pattern as START_MATCH (`% 1000 == 99`)

**Constants added:**
```csharp
private const int CLIENT_READY_SIGNAL_MAGIC = 777777;  // Legacy 37-byte channel (backup)
// Primary: magic % 1000 == 77 embedded in regular input
private const float CLIENT_READY_TIMEOUT = 15f;
private const float CLIENT_READY_SEND_DURATION = 10f;
```

**New fields:**
```csharp
private HashSet<PlayerRef> _clientsReady;
private HashSet<PlayerRef> _playersAwaitingReady;
private bool _waitingForClientsReady;
private bool _clientSendingReady;
private float _clientReadySendStartTime;
private bool _pendingSceneLoadFromServer;
```

#### 2. Deferred Scene Load from OnReliableDataReceived

Both SCENE_LOAD handlers (4-byte and 37-byte) no longer call `SceneManager.LoadScene` directly. Instead they set `_pendingSceneLoadFromServer = true`, which is consumed in `Update()` on the next frame (safe context). Also added `!_countdownActive` guard — if countdown coroutine is already handling scene loading, SCENE_LOAD signal is ignored.

#### 3. Stopped SCENE_LOAD Flooding

Removed the every-frame SCENE_LOAD re-send in Phase 3. SCENE_LOAD is now sent **once** when entering Phase 3. Clients that received COUNTDOWN already handle scene loading via `CountdownThenLoad()`.

#### 4. Async Scene Loading

Changed all client-side `SceneManager.LoadScene()` calls to `SceneManager.LoadSceneAsync()`:

- **`CountdownThenLoad()`** — `yield return SceneManager.LoadSceneAsync(gameplaySceneName)` keeps the game loop running during load. Client continues sending input → Fusion connection stays alive.
- **Deferred load in `Update()`** — uses `LoadSceneAsync()` for the SCENE_LOAD fallback path.
- **`LoadGameplayWithClientSync()`** (host path) — same async fix.

#### 5. Disconnect False-Positive Fix

The wait loop previously used `allReady = true` as default, then skipped disconnected players with `continue`. If ALL players disconnected, no player was found to be "not ready", so `allReady` stayed true → false "All clients reported READY" → spawned for disconnected players.

Fixed: track `connectedWaiting` and `connectedReady` counts. `allReady` is only true if `connectedWaiting > 0 && connectedReady == connectedWaiting`. If all players disconnect, abort spawn entirely. Fixed in both `UpdateMatchStartCountdown()` (flag-based) and `DedicatedServerCountdownThenLoad()` (coroutine) paths.

#### 6. MissileCycleController Fix

Fixed `InvalidOperationException` on player leave — `Update()` accessed `[Networked]` property `currentIndex` after despawn.

**MissileCycleController.cs** and **MissileCycleControllerDynamic.cs:**
```csharp
// Before: allowed invalid object access
if (Object != null && Object.IsValid && !Object.HasInputAuthority) return;

// After: proper null/despawn guard
if (Object == null || !Object.IsValid) return;
if (!Object.HasInputAuthority) return;
```

#### 7. Deploy Script Enhancement

**deploy.ps1** — Added `-Logs` switch parameter that tails server logs after deploy:
```powershell
param([switch]$Logs)
# ... existing deploy commands ...
if ($Logs) {
    ssh -o ConnectTimeout=10 "${User}@${VPS}" "tail -f /var/log/gv2-server.log"
}
```

### Key Architectural Insight

Fusion's `SendReliableDataToServer/Player` with custom `ReliableKey` has several pitfalls:
1. **4-byte messages on custom keys are silently dropped** — Fusion only reliably delivers 37-byte (INPUT_DATA_KEY-sized) packets
2. **Same key, same frame = only one delivery** — sending two messages on the same ReliableKey in one Update tick causes the second to overwrite the first
3. **Never call SceneManager.LoadScene from OnReliableDataReceived** — synchronous scene load inside a Fusion callback corrupts the runner
4. **Synchronous scene loads kill the connection** — for heavy scenes, always use `LoadSceneAsync` to keep the game loop running

The proven pattern for client→server signals: **embed the signal in the regular input's magic number** (`% 1000` encoding), so it piggybacks on the packet that's already guaranteed to deliver every frame.

### Files Modified

- `Assets/GV/Scripts/Network/NetworkManager.cs` — CLIENT_READY handshake, async scene loading, deferred SCENE_LOAD handler, disconnect false-positive fix, SCENE_LOAD flood removal
- `Assets/GV/Scripts/MissileCycleController.cs` — Spawned guard fix
- `Assets/GV/Scripts/MissileCycleControllerDynamic.cs` — Spawned guard fix
- `deploy.ps1` — Added `-Logs` switch

### Current Status

- CLIENT_READY handshake via magic % 1000 == 77 ✓
- Async scene loading keeps Fusion connection alive during heavy scene loads ✓
- No SceneManager.LoadScene calls inside OnReliableDataReceived ✓
- SCENE_LOAD sent once (not flooded every frame) ✓
- Disconnect false-positive fixed (abort if all players leave) ✓
- MissileCycleController despawn crash fixed ✓
- Deploy script supports `-Logs` for live log tailing ✓

---

## Session — March 10, 2026: ParrelSync UserId Collision & OnPlayerLeft Cleanup

### Problem

After Create Room on the clone editor (Player 2) and Join Room on the main editor (Player 3), Player 2 was immediately kicked — "player count went to zero on clone." Both clients then got stuck on the loading screen. Server logs showed CLIENT_READY never arrived (waited 13.2s until Player 3 also disconnected).

### Bug 1: Photon UserId Collision — Player 2 Kicked When Player 3 Joins

**Symptom:** The instant Player 3 registered (`RegisterUniqueIdPlayerMapping`), Player 2 disconnected. Server log sequence:
```
[Fusion] RegisterUniqueIdPlayerMapping actorid:3 ...player:[Player:3]
[NetworkManager] Player 2 left
[Fusion] adding player [Player:3]
```

**Root Cause:** ParrelSync clones share the same project folder and Photon settings. Fusion/Photon auto-generates a `UserId` from the project's settings, so both editors connected with the **same Photon UserId**. Photon treats the second connection as the same user reconnecting and kicks the first.

**Fix (NetworkManager.cs — `StartGame()`):**
- Added unique `AuthenticationValues.UserId` generation before `Runner.StartGame()`
- In Editor: main editor gets `"editor_main_<guid>"`, ParrelSync clone gets `"clone_<cloneArg>"`
- In builds: uses `System.Guid.NewGuid().ToString()`
- Set via `AuthValues` property on `StartGameArgs` (NOT on `FusionAppSettings` — that class doesn't have `AuthValues`)

### Bug 2: OnPlayerLeft Doesn't Clean Up _pendingSpawns

**Symptom:** After Player 2 was kicked, it remained as a ghost entry in `_pendingSpawns` (log showed `pendingSpawns=2` even though only Player 3 was connected). This caused the server to think 2 players needed spawning.

**Root Cause:** `OnPlayerLeft()` only cleaned up `_spawnedPlayers` (ships already spawned) but never removed the disconnected player from `_pendingSpawns`, `_playersAwaitingReady`, or `_clientsReady`.

**Fix (NetworkManager.cs — `OnPlayerLeft()`):**
- Added `_pendingSpawns.Remove(player)` — removes ghost from spawn queue
- Added `_playersAwaitingReady.Remove(player)` — prevents waiting for disconnected players
- Added `_clientsReady.Remove(player)` — cleanup

### Why CLIENT_READY Never Arrived

With the UserId collision as the primary issue, the chain reaction was:
1. Player 2 kicked → clone UI shows "player count 0"
2. Player 3 was the only player, match triggered via HTTP `/start`
3. Server sent COUNTDOWN (both 4-byte and 37-byte channels) to Player 3
4. Server moved to Phase 2 (SCENE_LOAD) after 3.5s and waited for CLIENT_READY
5. Player 3 likely received COUNTDOWN and started loading, but the overall network state was degraded from the collision event
6. CLIENT_READY never arrived → 15s timeout → Player 3 disconnected → server aborted spawn

Fixing the UserId collision should resolve the entire chain since both players will remain connected and the normal Enter Battle flow (client button → START_MATCH → COUNTDOWN → scene load → CLIENT_READY → spawn) will work with two stable connections.

### Files Modified

- `Assets/GV/Scripts/Network/NetworkManager.cs` — Unique Photon UserId per client, OnPlayerLeft cleanup of _pendingSpawns/_playersAwaitingReady/_clientsReady

### Key Lessons

- **ParrelSync clones share Photon UserId** — Both editors generate the same default UserId because they share the project's Photon settings. Photon kicks the first connection when the second connects with the same UserId. Fix: always set a unique `AuthenticationValues.UserId` before `StartGame()`.
- **OnPlayerLeft must clean ALL player tracking collections** — Not just `_spawnedPlayers` (for already-spawned ships), but also `_pendingSpawns` (queued pre-spawn), `_playersAwaitingReady` (waiting for CLIENT_READY), and `_clientsReady` (acknowledged scene load). Ghost entries in any of these cause spawn logic to wait for or reference disconnected players.

## Session — March 10, 2026 (continued): CLIENT_READY Delivery, Loading Screen Timeout, Camera Fix & Deploy Pipeline

**BUILD_VERSION: 2026-03-10-camera-fix-v8**

### Problem Chain

After the UserId collision fix, testing revealed a cascade of issues — each hidden behind the previous one:

1. **CLIENT_READY never arrives at server** — magic number stays at `2042`/`3042` (never `x077`), meaning the client's scene-loaded acknowledgment never reaches the server
2. **Loading screen stuck forever** — even after server force-spawns ships at 15s timeout, the client stays on the loading screen
3. **Camera stuck at scene origin** — after loading screen eventually disappears, the game scene is visible but the camera stays at the default scene position instead of following the spawned ship

All three log files analyzed (timingsynclogs_7/8/9) showed `BUILD_VERSION v7` — none of the code fixes were actually deployed (see Deploy Pipeline Fix below).

### Bug 1: CLIENT_READY Never Sent — RegisterSceneNetworkObjects Blocks All 3 Channels

**Symptom:** Server logs show `CLIENT_READY 0/1` or `CLIENT_READY 0/2` for 15 seconds, then timeout. Client magic stays at `playerId*1000+42` (never switches to `+77` ready signal).

**Root Cause:** In `OnUnitySceneLoaded`, `RegisterSceneNetworkObjects(scene)` is called BEFORE the three CLIENT_READY send channels. If it throws an exception, all three channels silently fail — no try-catch existed.

**Fix — 3 Layers of Defense (NetworkManager.cs):**

**Layer 1: Try-catch around RegisterSceneNetworkObjects**
```csharp
try { RegisterSceneNetworkObjects(scene); }
catch (System.Exception ex) {
    Debug.LogError($"RegisterSceneNetworkObjects THREW — continuing to CLIENT_READY sends. Error: {ex}");
}
```

**Layer 2: Fallback CLIENT_READY in CountdownThenLoad**
After `yield return asyncOp` (scene load complete), if `_clientSendingReady` is still false, sends on all 3 channels:
- Channel 1: 4-byte `CLIENT_READY_KEY` ("RDY!") via `SendReliableDataToServer`
- Channel 2: 37-byte `CLIENT_READY_SIGNAL_MAGIC` (777777) on `INPUT_DATA_KEY`
- Channel 3: Sets `_clientSendingReady = true` to embed `magic % 1000 == 77` in regular input

**Layer 3: Watchdog Timer in Update()**
3-second watchdog after `_inGameplayScene` is set true. If `_clientSendingReady` is still false, fires all 3 channels as last resort. Fields: `_inGameplaySceneTimestamp`, `_clientReadyWatchdogFired`, `CLIENT_READY_WATCHDOG_DELAY = 3f`.

### Bug 2: Loading Screen Never Hides — Silent Exception Swallowing

**Symptom:** Client stuck on loading screen even after server force-spawns ships. Ship detection loop in `Update()` runs but never finds the ship.

**Root Cause:** The `_waitingForShipToAppear` loop in Update() had `catch (System.Exception) { }` — a completely empty catch block. If `Runner.GetAllNetworkObjects()` throws every frame, the loading screen stays forever with zero diagnostic output.

**Fix (NetworkManager.cs):**
- Changed empty catch to log exceptions (once per second via `_shipDetectLogCounter`)
- Added periodic diagnostic logging (`[SHIP-DETECT]` tag) showing elapsed time, total network objects, LocalPlayer info
- Added 25-second timeout (`LOADING_SCREEN_TIMEOUT`) to force-hide loading screen
- Fields: `_loadingScreenShownTime`, `_shipDetectLogCounter`

### Bug 3: Camera Stuck at Scene Origin — GameAgentManager Overrides Manual Camera Setup

**Symptom:** After loading screen disappears, the game scene is visible but camera stays at the default scene position. Ship is spawned far away (e.g., pos=(1690, -67, -1132)) but camera doesn't follow it.

**Root Cause:** `VehicleCamera` subscribes to `GameAgentManager.onFocusedVehicleChanged` in its `Awake()`. In `NetworkedSpaceshipBridge.Spawned()`, `SetupClientOwnShip()` is called BEFORE `SetupCamera()`. Inside `SetupClientOwnShip()`:
1. `GameAgentManager.Instance.Unregister(ga)` — may fire `onFocusedVehicleChanged(null)`
2. `ga.EnterVehicle(vehicle)` — may fire `onFocusedVehicleChanged(vehicle)`
3. `ga.enabled = false` — `OnDisable()` may fire `onFocusedVehicleChanged(null)` AFTER `SetupCamera()` completes

If step 3 fires asynchronously or on the next frame, it calls `VehicleCamera.SetVehicle(null)`, undoing the manual camera assignment from `SetupCamera()`.

**Fix — 3 Layers of Defense (NetworkedSpaceshipBridge.cs):**

**Layer 1: Disconnect VehicleCamera from GameAgentManager**
In `SetupCamera()`, before calling `vehicleCamera.SetVehicle(vehicle)`:
```csharp
if (GameAgentManager.Instance != null)
    GameAgentManager.Instance.onFocusedVehicleChanged.RemoveListener(vehicleCamera.SetVehicle);
```
This prevents any GameAgentManager events from overriding the manual camera assignment.

**Layer 2: Camera Health Check (every 2s for 15s)**
In `Update()`, periodically verifies `_assignedVehicleCamera.TargetVehicle` still matches the local player's ship. If something reset it, re-assigns the camera and removes the GameAgentManager listener again. Tags: `[CAM-HEALTH]`.

**Layer 3: Last-Resort Camera Teleport**
If camera setup fails after 10s of retries (extended from 5s), moves `Camera.main` directly to the ship position + offset as a fallback.

**Diagnostic Logging Added:**
- `[CAM-SETUP]` — VehicleCamera discovery, SetVehicle calls, CameraTarget presence, all fallback paths
- `[CAM-DIAG]` — Camera state snapshots BEFORE SetupClientOwnShip, AFTER SetupClientOwnShip, AFTER SetupCamera
- `[CAM-HEALTH]` — Periodic camera target verification, re-assignment if reset detected

### Bug 4: `_inGameplayScene` Not Reset in OnShutdown (from previous session)

**Symptom:** First game works, second game doesn't. Client ignores SCENE_LOAD signals on subsequent games.

**Fix:** Complete state reset in `OnShutdown()` — all flags, timers, collections, and loading screen cleanup:
```
_inGameplayScene, _inGameplaySceneTimestamp, _clientReadyWatchdogFired,
_waitingForShipToAppear, _loadingScreenShownTime, _shipDetectLogCounter,
_clientSendingReady, _connectedToDedicatedServer, _isRoomCreator,
_pendingSceneLoadFromServer, _countdownActive, _sendStartMatchViaInput,
_manualSceneLoadRequested, _serverSendingCountdown, _serverSendingSceneLoad,
_waitingForClientsReady, _clientsReady, _playersAwaitingReady,
_spawnedPlayers, _pendingSpawns, _loadingScreenGO (destroyed)
```

### Deploy Pipeline Fix

**Problem:** All test logs (timingsynclogs 7/8/9) showed `BUILD_VERSION v7` despite user rebuilding multiple times. Code fixes were never reaching the server.

**Root Causes Found:**
1. **Wrong executable name in systemd service** — Service config had `ExecStart=Server_Test.x86_64` but Unity produces `Server_Tests.x86_64` (with an "s"). The service was running a stale binary.
2. **ServerBuild folder not updated by Unity editor Play** — Pressing Play in the editor only recompiles `Library/ScriptAssemblies/`, NOT the `ServerBuild/` folder. Must explicitly do File → Build → Dedicated Server (Linux) to update `ServerBuild/`.
3. **scp not overwriting files** — On some attempts, `scp -r` didn't overwrite existing files on the VPS.

**Fix:**
1. Fixed systemd service: `sed -i 's/Server_Test.x86_64/Server_Tests.x86_64/' /etc/systemd/system/gv2-server.service`
2. Clean deploy procedure: `rm -rf /opt/gv2-server && mkdir -p /opt/gv2-server` before copying
3. Documented proper deploy commands:
```bash
ssh root@187.124.96.178 "systemctl stop gv2-server && rm -rf /opt/gv2-server && mkdir -p /opt/gv2-server"
scp -r "ServerBuild\." root@187.124.96.178:/opt/gv2-server/
ssh root@187.124.96.178 "chmod +x /opt/gv2-server/Server_Tests.x86_64 && systemctl start gv2-server"
ssh root@187.124.96.178 "tail -f /var/log/gv2-server.log"
```

### Files Modified

- `Assets/GV/Scripts/Network/NetworkManager.cs` — CLIENT_READY 3-layer defense, loading screen timeout, ship detection diagnostics, complete OnShutdown resets, BUILD_VERSION bumped to v8
- `Assets/GV/Scripts/Network/NetworkedSpaceshipBridge.cs` — Camera 3-layer defense (GameAgentManager disconnect, health check, last-resort teleport), comprehensive `[CAM-SETUP]`/`[CAM-DIAG]`/`[CAM-HEALTH]` diagnostic logging
- `/etc/systemd/system/gv2-server.service` (on VPS) — Fixed executable name from `Server_Test` to `Server_Tests`

### Key Lessons

- **Three layers of defense pattern** — For critical handshake/setup operations, don't rely on a single mechanism. Use: (1) fix the root cause, (2) add a fallback path, (3) add a watchdog/health-check. Applied to both CLIENT_READY delivery and camera setup.
- **Empty catch blocks are silent killers** — `catch (System.Exception) { }` in a per-frame loop means a bug can persist forever with zero evidence. Always log caught exceptions, even if you can't handle them.
- **GameAgentManager event interference** — SCK's `VehicleCamera` listens to `GameAgentManager.onFocusedVehicleChanged`. When manually setting camera targets (bypassing SCK's normal flow), you must disconnect this listener to prevent SCK from resetting your assignment.
- **Unity build targets are independent** — Editor Play mode compiles to `Library/ScriptAssemblies/`. The Dedicated Server build in `ServerBuild/` is a completely separate output. Code changes are NOT reflected in server builds until you explicitly rebuild for that target.
- **Verify deployed builds** — Always check `BUILD_VERSION` in logs after deploy. If it doesn't match, the build pipeline is broken. Use `strings -e l Assembly-CSharp.dll | grep BUILD_VERSION` to verify DLL content directly.
- **systemd service filename must match exactly** — A typo in `ExecStart` (missing an "s") means the service silently runs a stale binary or fails entirely.

---

## Session 16 — March 10, 2026: Multi-Room Scene Object Fix, Loading Screen & Camera Fixes, WalletHUD

### Problem Summary

After deploying the dedicated server, four issues were reported:
1. **Shrinking sphere (BattleZoneController) and power spheres (RandomPowerSphere) only worked on the first room** — creating a second room on the VPS resulted in visible but non-functional scene objects
2. **Loading screen stuck** — clients sometimes got stuck on the loading screen after clicking Enter Battle
3. **Camera not following ship** — VehicleCamera spawned but didn't attach to the player's ship
4. **WalletHUD text overlap** — wallet address, AVAX balance, and PRANA balance text stacked on top of each other

### Root Cause Analysis

#### 1. Scene Objects Broken on Room 2+ (Critical)

**Architecture:** RoomManager creates a NEW NetworkManager instance (from prefab) per room. Each gets its own Fusion NetworkRunner. But scene-placed NetworkObjects (BattleZoneController, RandomPowerSphere) are single instances in the gameplay scene shared across all rooms.

**Root cause:** When Room 1's Runner shuts down, Fusion's internal state buffers on the scene NetworkObjects (object IDs, networked property data, registration flags) are corrupted/stale. When Room 2's Runner calls `RegisterSceneObjects()`, the objects either fail to register or register with corrupted state, so `Spawned()` never fires properly.

**Failed approaches:**
- **Toggle GameObject off/on** — After shutdown, `netObj.Runner` returns null, so stale detection couldn't identify corrupted objects. Even when forced, toggling didn't clear Fusion's internal state.
- **Reflection to clear internal Runner field** — Same null-Runner problem; the corruption is in Fusion's state buffers, not the Runner reference.
- **Destroy-and-clone from templates** — Cached clean clones of scene objects before Fusion touched them, then destroyed stale objects and instantiated fresh clones for Room 2+. Failed because dynamically instantiated clones aren't true scene-placed objects; Fusion's scene object matching between server and client broke (server had clones, clients had originals from their scene load).

**Working solution: Scene reload between rooms.** Before Room 2 starts, reload the entire gameplay scene via `LoadSceneAsync(sceneName, Single)`. This destroys all non-DontDestroyOnLoad objects and recreates them from the scene asset. The fresh objects have zero Fusion state. DontDestroyOnLoad objects (RoomManager, NetworkManagers, Web3Manager) survive the reload.

#### 2. Loading Screen Stuck

**Root cause:** CLIENT_READY signal sent via 3 channels (reliable data, input embedding, 37-byte packet) but all could be lost in transit. The existing watchdog fired once at 3 seconds but never retried.

**Fix:** Added periodic CLIENT_READY retry — re-sends on both reliable channels every 5 seconds starting at 8s post-scene-load (at ~8s, ~13s, ~18s). Gives the server multiple chances to receive the signal.

#### 3. Camera Not Following Ship

**Root cause:** Two camera setup paths conflicted:
- `NetworkManager.SetupCameraFollow()` — called immediately after spawn
- `NetworkedSpaceshipBridge.SetupCamera()` — called in FixedUpdateNetwork (1+ frames later)

`SetupClientOwnShip()` unregisters the GameAgent from `GameAgentManager`, which fires `onFocusedVehicleChanged` events that reset VehicleCamera's target to null. The listener disconnection happened inside `SetupCamera()` (too late — the damage was already done by `SetupClientOwnShip()`).

**Fix:** Disconnect `VehicleCamera` from `GameAgentManager.onFocusedVehicleChanged` BEFORE calling `SetupClientOwnShip()`, not after. Applied in both camera setup paths (NetworkedSpaceshipBridge and NetworkManager).

#### 4. WalletHUD Text Overlap

**Root cause:** TMP text fields were too narrow with word wrapping enabled, causing addresses like "0x6dd8...a77c" and "0.0000 AVAX" to wrap across multiple lines.

**Fix:** Rewrote WalletHUD layout: `FixTextField()` disables word wrapping, sets overflow mode to `TextOverflowModes.Overflow`, widens to 300px minimum. `EnforceTextLayout()` positions all 3 texts with explicit `fontSize * 1.5` spacing.

#### 5. Power Sphere Cooldown (Within Same Room)

**Root cause:** After collection, `IsActive = false` disables the collider via `Render()` + ChangeDetector. When cooldown ends and `IsActive` returns to true, the ChangeDetector could miss the transition, leaving the collider permanently disabled.

**Fix:** Added `FixedUpdateNetwork()` override on RandomPowerSphere that directly syncs collider state with `IsActive` every simulation tick on the server. Also made `CooldownRoutine()` force-enable the collider immediately when setting `IsActive = true`.

### Files Modified

**`Assets/GV/Scripts/Network/NetworkManager.cs`**
- `StartServerForRoom()` — Room 1 uses scene objects as-is; Room 2+ calls `ReloadGameplaySceneAsync()` before registration
- Added `ReloadGameplaySceneAsync()` — reloads gameplay scene via `LoadSceneAsync` to get fresh scene objects
- Added `_hasUsedSceneObjectsBefore` static flag with `[RuntimeInitializeOnLoadMethod]` reset
- Added periodic CLIENT_READY retry (every 5s starting at 8s post-scene-load)
- `SetupCameraFollow()` — disconnects VehicleCamera from GameAgentManager before `SetVehicle()`

**`Assets/GV/Scripts/Network/NetworkedSpaceshipBridge.cs`**
- `CheckAuthorityAndSetupInput()` — pre-emptive GameAgentManager disconnection BEFORE `SetupClientOwnShip()` call

**`Assets/GV/Scripts/Network/BattleZoneController.cs`**
- Added `Despawned()` override — resets local state (`_damageTimer`, `_loggedRenderOnce`) for room reuse

**`Assets/GV/Scripts/RandomPowerSphere.cs`**
- Added `FixedUpdateNetwork()` — server-side collider sync with `IsActive` every tick
- Added `Despawned()` override — clears cooldowns, stops cycle coroutine, resets index
- Enhanced `CooldownRoutine()` — force-enables collider immediately, validates Object state
- Enhanced `OnTriggerEnter()` — detailed logging for collection attempts

**`Assets/GV/Scripts/Web3/WalletHUD.cs`**
- `EnforceTextLayout()` — explicit positioning with `fontSize * 1.5` line spacing
- `FixTextField()` — disables word wrapping, overflow mode, 300px minimum width
- `CreateTokenBalanceText()` — simplified, delegates positioning to `EnforceTextLayout()`

### Key Lessons

- **Scene-placed NetworkObjects cannot survive Runner lifecycle changes** — Fusion's internal state on NetworkObjects is tied to a specific Runner. No code-level cleanup (toggle, reflection, cloning) reliably resets it. The only guaranteed approach is to reload the scene from the asset.
- **Dynamically instantiated objects ≠ scene-placed objects** — Even if clones have identical SortKeys and components, Fusion treats runtime-instantiated objects differently from scene-placed ones. Server/client scene object matching relies on objects being genuine scene instances.
- **Disconnect event listeners BEFORE triggering events** — When manually overriding framework behavior (like camera assignment), disconnect from the framework's event system before the action that fires events, not after.
- **Don't rely solely on ChangeDetector for critical state** — Fusion's ChangeDetector is designed for visual updates in `Render()`. For gameplay-critical state like collider enable/disable, use `FixedUpdateNetwork()` to directly enforce state every tick.
- **Periodic retry > single watchdog** — For unreliable network handshakes (CLIENT_READY), a single retry after N seconds can still fail. Periodic retries at intervals give the server multiple chances to receive the signal.

---

## Session 17 — March 13, 2026: URP Camera Fix, Server Build Compilation & CLIENT_READY Timeout

### Camera Stuck at MainCamera Default Position

**Problem:** Camera view stuck at the MainCamera's default scene position instead of following the spawned ship. Sometimes it works, sometimes it doesn't.

**Root Cause — URP Base/Overlay Camera Architecture:**
The SCK (SpaceCombatKit) scene uses a URP dual-camera setup:
- "Camera" (from Background_Space prefab) — URP **Base** camera (renderType=0, depth=-2), renders the space skybox
- "MainCamera" (from VehicleCamera_SCK prefab) — URP **Overlay** camera (renderType=1, depth=-1), stacks on top of the Base camera

The Base camera's `cameraStack` contains MainCamera. A URP Overlay camera **cannot render on its own** — it requires a Base camera in the stack to work. Our previous camera cleanup code was disabling ALL cameras except the VehicleCamera's MainCamera, which killed the Base camera and left MainCamera (an Overlay) unable to render anything.

**Fix — URP-Aware Camera Management:**
Updated 4 locations across 2 files to detect and preserve the URP Base camera:

1. **`NetworkedSpaceshipBridge.DisableConflictingCameras()`** — Detects if MainCamera is Overlay, searches all cameras for the Base camera that has MainCamera in its `cameraStack`, keeps it enabled. Falls back to switching MainCamera to Base renderType if no Base camera found.
2. **`NetworkedSpaceshipBridge` per-frame health check** — Added `_urpBaseCam` skip so the rogue-camera-killer doesn't disable the Base camera.
3. **`NetworkedSpaceshipBridge` CAM-PROPS diagnostic** — Logs URP renderType and Base camera status.
4. **`NetworkManager.EnforceCameraAfterLoadingScreen()`** — Same URP-aware fix using `FindURPBaseCameraForOverlay()` helper.
5. **`NetworkManager.SetupCameraFollow()`** — Same URP-aware fix.

Added `FindURPBaseCameraForOverlay()` helper to NetworkManager that finds the Base camera containing a given Overlay camera in its stack.

### Dedicated Server Build Failing Silently (No BUILD_VERSION)

**Problem:** After adding `using UnityEngine.Rendering.Universal;` to both NetworkManager.cs and NetworkedSpaceshipBridge.cs, the dedicated server build deployed but showed no BUILD_VERSION in logs and shut down immediately. The server log had only startup messages with no NetworkManager initialization.

**Root Cause:** URP assemblies are stripped from Dedicated Server builds when "Dedicated Server Optimizations" is enabled (confirmed by `Trying to access a shader but no shaders were included` messages in logs). The `using UnityEngine.Rendering.Universal` directive caused the scripts to fail to load on the server.

**Fix:** Wrapped all URP code in `#if !UNITY_SERVER` preprocessor guards:
- `using UnityEngine.Rendering.Universal;` — wrapped in both files
- `UniversalAdditionalCameraData`, `CameraRenderType`, `.renderType`, `.cameraStack` — all references wrapped
- `FindURPBaseCameraForOverlay()` — entire method wrapped
- Server-side CAM-PROPS diagnostic falls back to basic logging without URP data

The server doesn't have cameras anyway, so all URP camera logic is client-only.

### CLIENT_READY Never Received by Server

**Problem:** Despite 7 SCENE_LOAD retries over 14 seconds, the server never received CLIENT_READY from the client. The 15-second timeout force-spawned the ship every time.

**Investigation:**
- Server sends COUNTDOWN first, then SCENE_LOAD after countdown finishes
- Client receives COUNTDOWN, which triggers `CountdownThenLoad()` coroutine
- `CountdownThenLoad()` sets `_countdownActive=true` and `_inGameplayScene=true`, then loads the scene and attempts to send CLIENT_READY
- When SCENE_LOAD retries arrive, the client's handler sees `_inGameplayScene=true` and `_countdownActive=true`, so it ignores the message entirely
- CLIENT_READY sent by `CountdownThenLoad` never reaches the server (reason unknown — possibly Fusion reliable data routing issue)

**Fix (two-part):**

1. **SCENE_LOAD retry now triggers CLIENT_READY re-send:** When the client receives SCENE_LOAD but is already in gameplay, instead of just ignoring it, the client now re-sends CLIENT_READY on the 4-byte key channel and activates the magic%77 embed in regular input. Each server retry becomes a fresh trigger to re-send CLIENT_READY.

2. **CLIENT_READY_TIMEOUT reduced from 15s to 3s:** Since the COUNTDOWN already tells the client to start loading, 15 seconds was unnecessarily long. The force-spawn timeout now fires after 3 seconds, meaning ships spawn ~6 seconds after match start instead of ~18 seconds. The force-spawn has proven reliable across all tests — the player connects fine and plays normally even without CLIENT_READY.

### AWS EC2 Deployment

**Single-command deployment pipeline (PowerShell):**
```powershell
ssh -i "C:\Users\Veera\Desktop\Unity\MythiX AWS Key.pem" ubuntu@13.203.212.222 "pkill -f MythiX_GV; rm -rf ~/ServerBuild"; scp -i "C:\Users\Veera\Desktop\Unity\MythiX AWS Key.pem" -r "C:\Users\Veera\Desktop\Unity\GitHub\Prototype_GV2\ServerBuild" ubuntu@13.203.212.222:/home/ubuntu/; ssh -t -i "C:\Users\Veera\Desktop\Unity\MythiX AWS Key.pem" ubuntu@13.203.212.222 "chmod +x ~/ServerBuild/MythiX_GV.x86_64; cd ~/ServerBuild; nohup ./MythiX_GV.x86_64 -nographics -batchmode -dedicatedServer > server.log 2>&1 & sleep 5; cat server.log"
```

**Deployment issues resolved:**
- PuTTY .ppk to .pem conversion via PuTTYgen (Conversions > Export OpenSSH key)
- SSH timeout caused by IP change (WiFi to mobile hotspot changed public IP; updated security group inbound rule to "My IP")
- .pem file permissions too open on Windows — fixed with `icacls` to restrict to current user only
- PowerShell doesn't support `&&` — use `;` separator
- `%USERNAME%` doesn't expand in PowerShell — use literal username

### Files Modified

**`Assets/GV/Scripts/Network/NetworkManager.cs`**
- Added `#if !UNITY_SERVER` around `using UnityEngine.Rendering.Universal`
- Added `FindURPBaseCameraForOverlay()` wrapped in `#if !UNITY_SERVER`
- `EnforceCameraAfterLoadingScreen()` — URP-aware camera preservation
- `SetupCameraFollow()` — URP-aware camera preservation
- SCENE_LOAD handler — re-sends CLIENT_READY when already in gameplay instead of ignoring
- `CLIENT_READY_TIMEOUT` reduced from 15s to 3s
- BUILD_VERSION: `2026-03-13-v3-fast-spawn-3s-timeout`

**`Assets/GV/Scripts/Network/NetworkedSpaceshipBridge.cs`**
- Added `#if !UNITY_SERVER` around `using UnityEngine.Rendering.Universal`
- `DisableConflictingCameras()` — rewrote with URP Base/Overlay detection, wrapped in `#if !UNITY_SERVER`
- Added `_urpBaseCam` field — cached reference to URP Base camera
- Per-frame camera health check — skips `_urpBaseCam`
- `SetupCamera()` — URP diagnostic logging wrapped in `#if !UNITY_SERVER`
- CAM-PROPS diagnostic — URP-aware logging with server fallback

### Key Lessons

- **URP Overlay cameras cannot render alone** — Disabling the Base camera that has an Overlay in its stack causes the Overlay to show nothing. Always detect and preserve the Base camera in the `cameraStack`.
- **Dedicated Server builds strip rendering assemblies** — `using UnityEngine.Rendering.Universal` compiles in the editor but fails in Dedicated Server builds. Wrap all rendering-specific code in `#if !UNITY_SERVER` guards.
- **CLIENT_READY handshake is unreliable** — Despite 3 separate channels (4-byte key, 37-byte input key, magic embed in regular input) and 7 retries, CLIENT_READY never reached the server. A fast timeout with force-spawn is more robust than waiting for the handshake.
- **COUNTDOWN already handles scene loading** — The COUNTDOWN message triggers the full scene load flow on the client. SCENE_LOAD is redundant but useful as a trigger to re-send CLIENT_READY.

---

## Session 18 — March 13, 2026: Manual Scene Load Cleanup, Timeout Fix & Local Ship Callback

### Problem Summary

Current dedicated-server tests still showed intermittent bad outcomes:
- loading screen sometimes stayed up forever
- clients sometimes entered gameplay late or inconsistently
- server always timed out waiting for `CLIENT_READY`
- ship/camera setup could succeed but the loading screen still relied on polling

### Root Causes Addressed

1. **Room flow still passed a startup `Scene` into Fusion** even though the room architecture uses `NoOpSceneManager` and manual scene loading. That left room clients vulnerable to hidden scene sync / mismatched scene state.
2. **`CLIENT_READY_TIMEOUT` was reduced to 3 seconds**, but the gameplay scene load is heavy enough that clients often cannot finish loading before that timeout expires.
3. **Client state flags marked gameplay as active too early** during manual load flow, which blurred the difference between "countdown started," "scene load in progress," and "scene fully loaded."
4. **Loading screen exit depended mostly on polling**, even when the local ship/camera had already been set up successfully.

### Fixes Applied

**`NetworkManager.cs`**
- In room/manual scene flow, `StartGame()` now leaves `StartGameArgs.Scene` empty instead of registering the current scene into Fusion at startup
- Added `_clientSceneLoadInProgress` + `_clientSceneLoadCoroutine` to serialize manual gameplay scene loads
- Replaced duplicated client load logic with `BeginClientGameplayLoad()` + `ClientLoadGameplaySceneAsync()`
- Added `SendClientReadySignals()` helper so all CLIENT_READY sends use the same 3-channel path
- `SCENE_LOAD` retries now only re-send `CLIENT_READY` if the client is **actually already in gameplay**
- Duplicate `SCENE_LOAD` received during countdown/load-in-progress is ignored instead of being treated as readiness
- `CLIENT_READY_TIMEOUT` increased from **3s** to **10s**
- Added `NotifyLocalShipSpawned()` so the local ship can dismiss the loading screen immediately
- `OnShutdown()` now resets the client load-in-progress coroutine/flag too
- BUILD_VERSION bumped to `2026-03-13-v4-manual-load-fix`

**`NetworkedSpaceshipBridge.cs`**
- After successful local camera assignment, it now calls `NetworkManager.NotifyLocalShipSpawned()` to hide the loading screen immediately instead of waiting for polling

### Expected Outcome

- No hidden scene sync during room join/startup
- Fewer cases where server spawns before clients have any chance to finish loading
- Cleaner distinction between countdown, scene loading, and scene loaded
- Loading screen should drop as soon as the local ship/camera is truly ready

### Verification Status

Code updated, but this session ended before a fresh Unity build + live multiplayer retest. The next test should verify:
- no background gameplay scene load before Enter Battle
- `CLIENT_READY` arrives before timeout in normal conditions
- loading screen hides immediately on local ship/camera setup
- both clients see both ships consistently

---

## Session 19 - March 13, 2026: CLIENT_READY Retry Recovery and Timeout Extension

### Problem Summary

`camerastucklogs_5.txt` showed a later dedicated-server run still failing after Session 18:
- both clients connected and sent normal raw input before match start
- after `Enter Battle`, neither client ever reported `CLIENT_READY`
- server waited the full timeout, then force-spawned both ships anyway
- the user reported the client camera freezing on the loading/transition path

### What The Server Log Proved

This was not a total disconnect:
- server received regular input from Player 2 and Player 3 before and during the early part of the match-start wait
- the failure was specifically that the clients never transitioned into the `CLIENT_READY` state in time
- this points at the client scene-load/retry flow, not basic transport or room join

### Root Cause Addressed

The client retry guard had a real hole:
- `_countdownActive` was set `true` when countdown started
- that same flag was used to ignore duplicate `SCENE_LOAD` commands from the server
- but the client never cleared `_countdownActive` after the countdown ended
- result: if the first load attempt stalled or failed, every later `SCENE_LOAD` retry was ignored forever

There was also still a backup gap:
- `OnSceneLoadDone()` marked the client as being in gameplay
- but it still did not send `CLIENT_READY` there
- if Unity's `sceneLoaded` backup path or the async fallback missed, the server could still time out

### Fixes Applied

**`Assets/GV/Scripts/Network/NetworkManager.cs`**
- Cleared `_countdownActive` immediately after the visible countdown ends so later `SCENE_LOAD` retries can recover a failed first load attempt
- Added a direct `CLIENT_READY` backup send in `OnSceneLoadDone()` for clients
- Increased `CLIENT_READY_TIMEOUT` from **10s** to **20s**
- Added 1-second progress diagnostics inside `ClientLoadGameplaySceneAsync()`
- BUILD_VERSION bumped to `2026-03-13-v5-clientready-retry-fix`

### Expected Outcome

- If the first client load attempt fails, server `SCENE_LOAD` retries can trigger recovery instead of being ignored forever
- If Fusion/Unity reaches gameplay via `OnSceneLoadDone()` first, the client still reports ready immediately
- Heavy scene loads get more time before the server force-spawns
- The next log should clearly show whether clients are stuck inside `LoadSceneAsync()` or failing after the scene becomes active

### Verification Status

Code updated, but no fresh Unity client/server retest was possible in this session. The next run should confirm:
- build log shows `2026-03-13-v5-clientready-retry-fix`
- client logs show either async progress or `OnSceneLoadDone` / `OnUnitySceneLoaded` sending `CLIENT_READY`
- server receives at least one `CLIENT_READY` before timeout on the bad cases that previously froze

---

## Session 20 - March 14, 2026: Dedicated CLIENT_READY Packet and Duplicate Countdown Guard

### Problem Summary

The next redeployed test looked better on the surface, but the logs showed the handshake was still broken:
- both clients loaded gameplay successfully
- both clients logged repeated `CLIENT_READY` sends
- both clients eventually got ships, hid the loading screen, and had working cameras
- but the server still never logged a single `Received CLIENT_READY`
- the server only spawned after the full `20s` timeout

There was also still a client-side state bug:
- after gameplay loading had already begun, a late COUNTDOWN signal could start a second countdown
- that second countdown usually self-healed, but it was unnecessary and noisy

### Root Cause Addressed

The strongest remaining suspicion is packet merging / replacement on the shared reliable input key:
- normal raw input and the 37-byte `CLIENT_READY` signal both used the same `INPUT_DATA_KEY`
- server logs showed only ordinary `2042/3042` input, never the ready variants
- clients were therefore "sending ready" locally, but the dedicated server was not observing it

### Fixes Applied

**`Assets/GV/Scripts/Network/NetworkManager.cs`**
- Added a dedicated `CLIENT_READY` reliable packet path using its own key plus a packet marker byte
- Server now accepts `CLIENT_READY` from four paths:
  - 4-byte `RDY!`
  - dedicated ready packet
  - 37-byte exact ready signal
  - regular input magic `% 1000 == 77`
- Tightened the old 38-byte special-packet handler so it distinguishes START_MATCH vs CLIENT_READY by marker byte instead of treating any 38-byte packet as START_MATCH
- Added `MarkClientReadyReceived()` helper so all ready paths update the server state consistently
- Added `ShouldStartClientCountdown()` so clients ignore late duplicate COUNTDOWN messages once scene loading or gameplay is already active
- Periodic ready retries now include the dedicated ready packet too
- BUILD_VERSION bumped to `2026-03-14-v6-dedicated-ready-packet`

### Expected Outcome

- Server should finally record at least one `CLIENT_READY` before timeout
- Match start should happen because clients are actually acknowledged ready, not because timeout force-spawned them
- Clients should no longer start a second countdown after scene loading is already underway

### Verification Status

Code updated, but this session ended before a fresh retest. The next run should verify:
- server log contains `Received CLIENT_READY (...)`
- server no longer reaches `CLIENT_READY TIMEOUT`
- clients do not start a second countdown after `LoadSceneAsync('Gameplay')` has already started

---

## Session 21 - March 14, 2026: Ready Bit on Proven Input Path and Countdown Flood Reduction

### Problem Summary

The first `v6` redeploy still did not complete the handshake correctly:
- both clients loaded gameplay
- both clients logged repeated `CLIENT_READY` sends, including the dedicated ready packet
- duplicate COUNTDOWN starts were blocked correctly on the client
- but the server still never logged `Received CLIENT_READY`
- the server still hit `CLIENT_READY TIMEOUT after 20s`

The client logs also revealed that COUNTDOWN signals were still arriving for a very long time, even after gameplay had started. That suggested a reliable-message backlog caused by the old repeated COUNTDOWN signal strategy.

### Root Cause Addressed

Two follow-up problems were targeted:

1. **`CLIENT_READY` still depended too much on side-channel sends**
   - regular raw input definitely reached the server
   - but dedicated ready packets / separate ready sends still were not being observed server-side
   - the safest path is to embed readiness directly inside the proven normal raw input stream

2. **Server-side repeated COUNTDOWN sends were too aggressive**
   - repeated reliable COUNTDOWN signals over `INPUT_DATA_KEY` appear to have created a large delayed delivery backlog
   - clients spent a long time ignoring stale COUNTDOWN packets even after scene load had already begun

### Fixes Applied

**`Assets/GV/Scripts/Network/NetworkManager.cs`**
- Added reserved `CLIENT_READY_BUTTON_BIT = 30`
- While `_clientSendingReady` is active, normal raw input now embeds:
  - magic `% 1000 == 77`
  - button bit 30
- Server now recognizes CLIENT_READY from raw input button bit 30, even if the magic number path fails
- Server clears button bit 30 before storing the packet as normal gameplay input
- Added `CLIENT READY EMBED` diagnostic log on clients so the next run can prove the raw input packet is carrying the ready signal
- Stopped repeated every-frame COUNTDOWN sends on `INPUT_DATA_KEY`
- COUNTDOWN is now sent once at trigger time on both channels instead of being flooded continuously
- BUILD_VERSION bumped to `2026-03-14-v7-ready-bit-and-countdown-throttle`

### Expected Outcome

- Server should finally observe CLIENT_READY through the same raw input stream it already receives reliably
- Clients should stop being spammed by stale COUNTDOWN signals long after scene loading starts
- Match start should happen because the server acknowledged readiness, not because timeout force-spawned

### Verification Status

Code updated, but not retested in this session. The next run should verify:
- client logs contain `CLIENT READY EMBED: magic=... buttons=...`
- server logs contain `Received CLIENT_READY (button bit 30)` or another ready path
- server does not hit `CLIENT_READY TIMEOUT`
- client logs no longer show massive post-start COUNTDOWN-signal spam

---

## Session 22 - March 14, 2026: Pre-Game Reliable Input Backlog Reduction

### Problem Summary

The first `v7` redeploy still failed:
- server and both clients were definitely on `2026-03-14-v7-ready-bit-and-countdown-throttle`
- both clients logged `CLIENT READY EMBED: magic=2077/3077, buttons=0x40000000`
- both clients also sent all explicit `CLIENT_READY` packets and completed scene load successfully
- but the server still only logged ordinary raw input `2042/3042` and still timed out waiting for ready

That narrowed the issue further: the client was constructing the ready-bearing packets, but the server was still draining older normal reliable input for too long.

### Root Cause Addressed

The raw client input transport was still sending a reliable `INPUT_DATA_KEY` packet every frame from the room scene onward, even before gameplay:
- idle room input
- countdown
- scene loading
- repeated post-load retries

Because that stream is reliable, it creates a backlog of stale `2042/3042` packets ahead of the later `CLIENT_READY` packets. The server logs support that: by timeout it was still mostly receiving ordinary raw input and had not observed the ready-bearing packets yet.

### Fixes Applied

**`Assets/GV/Scripts/Network/NetworkManager.cs`**
- Suppressed normal pre-game raw reliable input traffic
- The client now uses the raw reliable stream only when one of these is true:
  - gameplay scene is active
  - `_clientSendingReady` is active
  - `_sendStartMatchViaInput` is active
- This prevents room/countdown/loading idle input from filling the reliable queue before `CLIENT_READY`
- Extended `CLIENT_READY_SEND_DURATION` from `10s` to `20s` so the ready embed remains active for the full server-side timeout window
- BUILD_VERSION bumped to `2026-03-14-v8-pregame-input-throttle`

### Expected Outcome

- The reliable input queue should no longer be full of stale pre-game `2042/3042` packets
- The server should see `CLIENT_READY` much earlier in the wait window
- Timeout-based force-spawn should stop being the normal path

### Verification Status

Code updated, but not retested in this session. The next run should verify:
- server log contains `BUILD_VERSION: 2026-03-14-v8-pregame-input-throttle`
- server log contains `Received CLIENT_READY (...)`
- server does not reach `CLIENT_READY TIMEOUT`
- client logs no longer show `CLIENT FIRST RAW INPUT SEND` before the gameplay/ready phase
