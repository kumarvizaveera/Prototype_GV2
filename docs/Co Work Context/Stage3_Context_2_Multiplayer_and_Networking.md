# MythiX Battle Royale - Stage 3 Context (Part 2: Multiplayer and Networking)

## Networking Stack

| Component | Details |
|-----------|---------|
| Framework | Photon Fusion 2.0.9 |
| Topology | Host/Client (not dedicated server by default, but dedicated server supported) |
| Max Players | 4 per match |
| Scene Management | Manual (NoOpSceneManager disables Fusion auto-sync) |
| Room System | 4-character alphanumeric codes |
| Input Model | RPC-based (bypasses Fusion OnInput/GetInput due to IL weaver issues) |

### Room Code System

4-character codes using filtered alphabet: ABCDEFGHJKMNPQRSTUVWXYZ23456789
Excluded characters: O, 0, I, 1, L (to avoid ambiguity)
Max rooms per dedicated server: 10
Empty room timeout: 30 seconds

---

## Network Scripts Detailed Breakdown

All files located in: Assets/GV/Scripts/Network/
Namespace: GV.Network

### NetworkManager.cs
Central singleton handling Photon Fusion connection, player spawning, room management, and game flow orchestration.

Properties:
- IsDedicatedServer (bool)
- CurrentRoomCode (string)
- Runner (NetworkRunner)
- IsConnected (bool)

Events:
- OnConnectedEvent (NetworkRunner)
- OnPlayerJoinedGame (PlayerRef)
- OnPlayerLeftGame (PlayerRef)
- OnDisconnectedEvent (NetworkRunner)

Key Methods:
- ShowLobbyUI(): shows room lobby interface
- LoadGameplay(): loads gameplay scene
- SendStartMatchViaHttp(): HTTP POST to VPS for START_MATCH signaling (fallback from Fusion ReliableData)
- CountdownThenLoad(): countdown before scene load
- OnUnitySceneLoaded(): handles post-load setup

Constants:
- START_MATCH_SEND_REPEATS = 60 (magic % 1000 == 99)
- LOADING_SCREEN_TIMEOUT = 25f
- SWAP_GRACE_PERIOD = 1.0f

Server Mode Enum: Auto, DedicatedServer

### NetworkedGameManager.cs
Game state machine with synchronized countdown and race timing.

Game States (enum):
- WaitingForPlayers
- Countdown
- Racing
- Finished

Networked Properties:
- CurrentState (GameState)
- CountdownTimer (TickTimer)
- RaceTimer (TickTimer)

Events:
- OnCountdownStarted
- OnCountdownTick(int secondsRemaining)
- OnRaceStarted
- OnRaceFinished

RPC Methods:
- RPC_StartCountdown()
- RPC_CountdownTick(int)
- RPC_RaceStarted()
- RPC_RaceFinished()

Auto-starts countdown based on minPlayersToStart. Configurable countdown duration. Debug GUI display available.

### EliminationTracker.cs
Tracks player eliminations in Battle Royale mode. Records elimination order. Broadcasts results to all players via RPC.

Events:
- OnPlayerEliminated(PlayerRef, int placement)
- OnMatchResultsReady(List of PlayerPlacement)

RPC Methods:
- RPC_PlayerEliminated()
- RPC_PlayerResult()
- RPC_MatchComplete()

Key Methods:
- MonitorPlayerVehicles(): watches all VehicleHealth components
- CheckForEliminations(): detects destroyed ships
- HandlePlayerEliminated(): records elimination, assigns placement
- EndMatchWithResults(): broadcasts final placements to all clients

Data Structure:
- PlayerPlacement: playerRef, playerId, placement

Logic: monitors VehicleHealth components, respects GameManager race state, fires events on all clients. First eliminated = last place.

### BattleZoneController.cs
Controls the shrinking battle zone sphere. Central gameplay mechanic.

Networked Properties:
- CurrentRadius (float)
- IsShrinking (NetworkBool)
- ShrinkTimer (TickTimer)
- NetworkedShrinkDuration (float)

Parameters:
- initialRadius: 500f (500 meters)
- minRadius: 0f
- shrinkDuration: 60f (60 seconds)
- damagePerSecond: 10f
- damageInterval: 1f (applies damage every second)

Key Methods:
- StartShrinking(): begins zone collapse
- CheckPlayersAndApplyDamage(): damages ships outside radius via VehicleHealth
- FixedUpdateNetwork(): updates timer, radius, applies damage on host

Visual: sphere scales down matching CurrentRadius. UI timer shows remaining time.

### NetworkedSpaceshipBridge.cs
Bridges Fusion network input to SpaceCombatKit VehicleEngines3D. Handles player-to-remote sync, camera management, aim/target sync. Execution order -200.

Networked Properties:
- SyncPosition (Vector3)
- SyncRotation (Quaternion)
- SyncTick (int, diagnostic)
- SyncIsAActive (NetworkBool, for mesh swap state)

RPC Methods (all InputAuthority -> StateAuthority):
- RPC_SendInput(steerPitch, steerYaw, steerRoll, moveX, moveY, moveZ, boost, buttons)
- RPC_SendTransform(position, rotation)
- RPC_SendAimPosition(Vector3 aimPos)
- RPC_SendTargetLock(NetworkId targetId)
- RPC_SendTeam(string teamName)
- RPC_SendSwapState(NetworkBool isAActive)

Why RPC instead of Fusion Input: the IL weaver had issues with OnInput/GetInput, so input is sent directly via RPC from InputAuthority to StateAuthority.

Manual position sync via [Networked] properties prevents physics divergence between host and client.

### NetworkedPlayerInput.cs
Collects local player input for SpaceCombatKit controls using mouse and keyboard.

Input Struct (PlayerInputData):
- steerPitch, steerYaw, steerRoll (float)
- moveX, moveY, moveZ (float)
- boost (NetworkBool)
- buttons (NetworkButtons)
- magicNumber (int, diagnostic = 42)

Button Constants:
- BUTTON_FIRE_PRIMARY = 0
- BUTTON_FIRE_SECONDARY = 1
- BUTTON_FIRE_MISSILE = 2
- BUTTON_CYCLE_WEAPON = 3
- BUTTON_CYCLE_CHARACTER = 4
- BUTTON_SWAP_AIRCRAFT = 5

Reticle Logic:
- Mouse delta accumulates into virtual reticle (viewport 0-1, center 0.5, 0.5)
- Clamped to maxReticleDistance (0.475f) with aspect correction
- No auto-center

Controls:
- W/S: throttle (auto-forward support)
- A/D: strafe
- Ctrl/Space: vertical
- Tab: boost

### NetworkedHealthSync.cs
Synchronizes VehicleHealth (all damageables) across network without shield bypass.

Networked Properties:
- NetworkedHealthValues (NetworkArray of float, capacity 8)
- NetworkedDamageableCount (int)

Sync Direction:
- StateAuthority writes health values every FixedUpdateNetwork
- Clients read and apply every Render frame

Uses SetHealth() not Damage() to bypass shield when applying networked values. Handles destroy/restore transitions. Reflection-based restore disable.

MAX_DAMAGEABLES constant = 8

### NetworkedAimOverride.cs
Overrides weapon aim direction on host's copy of client ship. Execution order 50 (runs after WeaponsController at order 30).

Not a NetworkBehaviour. Regular MonoBehaviour. Called via NetworkedSpaceshipBridge RPCs.

Methods:
- SetAimPosition(Vector3): sets aim override
- SetTargetLock(Trackable): sets target lock
- LateUpdate(): applies aim to WeaponsController

### NetworkedResourceSync.cs
Synchronizes VSX ResourceContainer (ammo, fuel) across network.

Networked Property: NetworkedAmount (float)
StateAuthority: local to networked (FixedUpdateNetwork)
Clients: networked to local (FixedUpdateNetwork + Render)

### NetworkedCheckpointTracker.cs
Tracks individual player checkpoint progress.

Networked Properties: CurrentLap (int), CurrentCheckpointIndex (int)
Called by RaceCheckpoint trigger. Validates via StateAuthority.

### LevelSynchronizer.cs
Synchronizes deterministic seed across all players for synchronized level generation.

Networked Property: LevelSeed (int)
Host generates seed from Environment.TickCount. Replicated to all clients. DontDestroyOnLoad.

### NetworkedSpawnerInitializer.cs and NetworkedAstraRefillInitializer.cs
Wait for LevelSynchronizer seed, then initialize spawners with deterministic seed (global seed combined with position hash).

### NetworkPowerBridge.cs
Bridges power activation from local client to host.

RPC: InputAuthority -> StateAuthority
Power Types routed to handlers:
- Shield -> NetworkShieldHandler
- Invisibility -> InvisibilityHandler
- SuperBoost -> AircraftSuperBoostHandler
- SuperWeapon -> NetworkSuperWeaponHandler
- Teleport -> TeleportPowerUp

### NetworkShieldHandler.cs
Manages shield power-up state across network.

Networked Properties:
- IsShieldActive (NetworkBool)
- ShieldTimer (TickTimer)

Uses local cache for change detection. StateAuthority handles expiration.

### NetworkSuperWeaponHandler.cs
Manages super weapon power-up with stat multipliers.

Networked Properties:
- IsSuperWeaponActive (NetworkBool)
- SuperWeaponTimer (TickTimer)
- NetProjDmg, NetProjRange, NetProjSpeed, NetProjRate, NetProjReload (all float)
- NetMissileDmg, NetMissileRange, NetMissileSpeed, NetMissileRate, NetMissileReload (all float)

Uses ChangeDetector for reliable state polling.

### RoomManager.cs
Manages multiple game rooms on dedicated server via HTTP API.

HTTP Endpoints:
- POST /create: creates new room
- GET /rooms: lists all rooms
- GET /rooms/{code}: get specific room
- DELETE /rooms/{code}: shutdown room
- POST /start/{code}: start match in room
- GET /health: health check

RoomInfo: Code, Manager, Runner, CreatedAt, LastPlayerLeftAt, State, IsEmpty, PlayerCount
RoomState: Starting, Lobby, InGame, Closing

Features: unique 4-char codes, empty room timeout 30s, max 10 rooms, HTTP listener on configurable port, thread-safe room dictionary, main-thread action queue.

### ServerBootstrap.cs
Initializes dedicated server: disables audio, sets framerate to 60, disables rendering, enables run in background, disables vsync, prevents screen sleep.

Detects server via UNITY_SERVER define or -server command-line flag.

### NoOpSceneManager.cs
Prevents Fusion from auto-syncing scenes. All Load/Unload operations are no-op. Enables manual room-based scene loading via RoomManager.

### PlayerLabelController.cs
3D TextMeshPro label on ships showing player role (Host/Client/Client 2 etc.) to opponents only.

Networked Property: PlayerNumber (int, default -1)
Billboard effect (faces camera). Skips on dedicated server.

### RaceCheckpoint.cs
Trigger on checkpoint objects. Notifies NetworkedCheckpointTracker when player passes through.

### NetworkAudioHelper.cs
Static utility preventing duplicate audio in multiplayer. Audio plays only on local player's ship. Checks HasInputAuthority.

---

## Key Multiplayer Architecture Patterns

1. Authority Model: Host has StateAuthority over all networked objects. Clients have InputAuthority over their own player. All game logic runs on host.

2. RPC-Based Input: NetworkedSpaceshipBridge sends input via RPC instead of Fusion's built-in OnInput/GetInput (IL weaver issues). Input flows: Client InputAuthority -> RPC -> Host StateAuthority.

3. Manual Position Sync: Position and rotation synced via [Networked] properties because physics timing diverges between host and client.

4. Deterministic Level Generation: LevelSynchronizer broadcasts seed to all clients. Spawners combine global seed with position hash for deterministic checkpoint and pickup placement.

5. HTTP Fallback for Match Start: NetworkManager sends START_MATCH via HTTP POST to VPS (more reliable than Fusion ReliableData for critical signaling).

6. Room-Based Architecture: NoOpSceneManager disables Fusion auto-sync. RoomManager HTTP API manages multiple rooms per dedicated server.

7. Health Sync Without Shield Bypass: Uses SetHealth() instead of Damage() to apply networked health values without triggering shield responses.

8. Event-Driven Game States: NetworkedGameManager broadcasts state transitions via RPC to all clients (countdown ticks, race start, race finish).

9. Power-Up Routing: NetworkPowerBridge routes power activations through type-specific handlers, each with their own networked state and timers.

10. Change Detector Pattern: NetworkSuperWeaponHandler uses Fusion ChangeDetector for reliable state change detection instead of event subscriptions.

---

## Known Multiplayer Issues

The core battle loop works end to end. However, there are intermittent bugs in multiplayer session handling around player sync and reconnection edge cases. These can make testing inconsistent, particularly when players drop or join mid-session. The blockchain reward pipeline is not affected by these issues and executes reliably after every completed match. Stability fixes for networking edge cases are actively in progress.
