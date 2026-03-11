# MythiX Battle Royale - Complete Project Context for Stage 3

This document contains everything known about the MythiX Battle Royale project. It is divided into 3 parts for easy navigation.

Table of Contents:
- Part 1: Overview, Web3 and Blockchain (line ~15)
- Part 2: Multiplayer and Networking (line ~310)
- Part 3: Gameplay, Project Structure, and Tech Stack (line ~640)

---
---
---

# PART 1: OVERVIEW, WEB3 AND BLOCKCHAIN

## Project Identity

- Project Name: MythiX Battle Royale
- Repo: Prototype_GV2
- Active Branch: MythiX_GV (never merged to Main)
- Engine: Unity 6000.3.0f1 with Universal Render Pipeline (URP 17.3.0)
- Product Version: 2.9.6
- Theme: Mythic Futurism. Ancient Vedic mythology meets sci-fi. Ships are called Vimanas, weapons are Astras, reward tokens are PRANA.
- Built for: Avalanche Build Games hackathon

---

## Blockchain: Avalanche C-Chain (Fuji Testnet)

Chain ID: 43113
RPC URL: https://api.avax-test.network/ext/bc/C/rpc

### Smart Contracts

Ship and Character NFTs (ERC-1155): 0x8405209745b8f1A43D21876120543d20e4a7600C
Snowtrace: https://testnet.snowtrace.io/address/0x8405209745b8f1A43D21876120543d20e4a7600C

PRANA Battle Reward Token (ERC-20): 0xBF7c298C0f3E4745Ec902ED0008223747EEbd0d1
Snowtrace: https://testnet.snowtrace.io/address/0xBF7c298C0f3E4745Ec902ED0008223747EEbd0d1

Server Wallet (EOA): 0x2bBc1C32224a347eaF8d10cAFaF77F3aBCA2551f
Snowtrace: https://testnet.snowtrace.io/address/0x2bBc1C32224a347eaF8d10cAFaF77F3aBCA2551f

Reward Wallet (EOA, holds Minter role on PRANA contract): 0x78A75F10f4c2A20bd30c4A683607ABc1A22Bb352
Snowtrace: https://testnet.snowtrace.io/address/0x78A75F10f4c2A20bd30c4A683607ABc1A22Bb352

### On-Chain Flow

1. Player connects wallet via Thirdweb InAppWallet or Reown AppKit (external wallets)
2. Game queries ERC-1155 balanceOf to determine which ships and characters the player owns
3. Players who don't own NFTs get a free default ship and default characters
4. Match plays out with up to 4 players
5. When match ends, BattleRewardManager creates a fresh Nethereum Web3 instance using the reward wallet's private key
6. Calls mintTo(playerAddress, amount) on the PRANA ERC-20 contract for each player based on placement
7. Polls for transaction confirmation (up to 30 attempts, 1 second apart)
8. UI updates with confirmed on-chain balance

### Reward Tiers (Per Match)

| Placement | PRANA Tokens | XP  | Energy | Gems | Coins |
|-----------|-------------|-----|--------|------|-------|
| 1st       | 100         | 500 | 50     | 10   | 200   |
| 2nd       | 60          | 300 | 30     | 5    | 120   |
| 3rd       | 30          | 150 | 15     | 2    | 60    |
| 4th       | 10          | 50  | 5      | 0    | 20    |

### Why Nethereum Instead of Thirdweb for Minting

Thirdweb Unity SDK v6 does not expose PrivateKeyWallet in Unity builds. The game needs a server-side wallet to mint tokens without prompting the player for gas. Solution: use Nethereum v5.0.0 directly for write transactions (mintTo), while Thirdweb handles all read operations (balanceOf, wallet connect, balance queries).

### Why Fresh Web3 Instance Per Mint

During playtesting, rapid sequential mints (4 players at match end) caused stale nonce errors when reusing the same Nethereum Web3 instance. Creating a new Account and Web3 object per transaction eliminates this.

### Gas Cost

Approximately 0.006 AVAX per mint transaction on Fuji. Transactions confirm in 1 to 2 seconds.

---

## Web3 SDK Stack

| Component | Version | Purpose |
|-----------|---------|---------|
| Thirdweb Unity SDK | v6 | Wallet connect (InAppWallet), NFT read queries, balance reads |
| Nethereum | v5.0.0 | ERC-20 mintTo() write transactions (signing + sending) |
| Reown AppKit | v1.5.2 | External wallet support (MetaMask, Coinbase Wallet, WalletConnect) |

### Authentication Methods (5 total)

1. Email (via Thirdweb InAppWallet)
2. Google OAuth (via Thirdweb InAppWallet)
3. Discord OAuth (via Thirdweb InAppWallet)
4. Guest (one-tap, no credentials, via Thirdweb InAppWallet)
5. External Wallet (MetaMask, Coinbase Wallet, WalletConnect via Reown AppKit)

---

## Web3 Scripts Detailed Breakdown

All files located in: Assets/GV/Scripts/Web3/

### Web3Manager.cs
Central singleton for all blockchain functionality. Handles wallet connection across all 5 auth methods. Manages wallet address and AVAX balance. Fires events for UI systems. Must initialize before NetworkManager.

Serialized Fields:
- chainId (ulong, default 43113 = Fuji)
- defaultAuthProvider (AuthProvider, default Google)
- reownProjectId (string, for external wallets)

Events:
- OnWalletConnected(string address)
- OnWalletDisconnected()
- OnBalanceUpdated(string formatted)
- OnError(string message)

Key Methods:
- ConnectWithEmail(string email): validates email, creates InAppWallet
- ConnectWithSocial(AuthProvider): Google or Discord OAuth
- ConnectAsGuest(): one-tap guest wallet
- ConnectWithExternalWallet(): Reown/WalletConnect QR code or app popup
- Disconnect(): clears all wallet state
- RefreshBalance(): fetches AVAX balance from chain
- FetchBalance(): converts wei to ETH display format

All connect methods are async void, internally calling async Task ConnectWallet() which handles InAppWallet creation, address retrieval, balance fetch, and event firing.

### Web3Bootstrap.cs
Bootstrap script ensuring Web3 systems are ready before anything else. On dedicated server: instantiates RoomManager, loads gameplay scene, skips menu. On client: loads menu scene.

Serialized Fields:
- nextSceneName (string): scene to load on client
- gameplaySceneName (string): scene for dedicated server
- autoLoadNextScene (bool, default true)
- networkManagerPrefab (GameObject)
- roomManagerPrefab (GameObject)

Server detection: checks UNITY_SERVER define and command-line args (-server or --server).

### BattleRewardManager.cs
Singleton that survives scene changes. Manages ERC-20 token reward minting using Nethereum. Uses the reward wallet private key (set via Inspector, never committed to git).

Serialized Fields:
- tokenContractAddress (default: 0xBF7c298C0f3E4745Ec902ED0008223747EEbd0d1)
- tokenDisplayName (default: "PRANA")
- tokenSymbol (default: "PRANA")
- tokenDecimals (default: 18)
- rewardWalletPrivateKey (empty string, set via Inspector)
- rpcUrl (default: https://api.avax-test.network/ext/bc/C/rpc)
- rewardsByPlacement (List of PlacementReward, 4 entries)

Constants:
- MINT_TO_ABI: JSON ABI for mintTo(address, uint256)

Key Methods:
- DistributeRewards(List of PlayerRewardInfo): main async method. Creates fresh Web3 instance, calls mintTo for each player, polls confirmation 30 times at 1 second intervals, fires OnRewardsDistributed
- FetchTokenBalance(): reads player's PRANA balance via Thirdweb SDK balanceOf
- CreateFreshRewardWeb3(): creates new Nethereum Web3+Account per transaction to avoid stale nonce
- ToWei(float): converts token amount to wei using 10^18
- TestDistributeRewards(): debug method, mints 1st place rewards to connected wallet

Events:
- OnRewardsDistributed(List of RewardResult)
- OnRewardMinted(string address, float amount)
- OnTokenBalanceUpdated(string formatted)
- OnRewardError(string message)

Data Classes:
- PlayerRewardInfo: walletAddress (string), placement (int)
- RewardResult: walletAddress, placement, tokenAmount, success, message
- PlacementReward: placementLabel, tokenAmount, xpAmount, energyAmount, gemsAmount, coinsAmount

### BattleRewardBridge.cs
Bridges multiplayer game state to the reward system. Listens for match results from EliminationTracker. Detects local ship destruction. Distributes token rewards based on placement. Shows PostMatchRewardUI popup. Implements a 5-second fallback timeout if EliminationTracker doesn't respond.

Serialized Fields:
- postMatchUI (PostMatchRewardUI)
- enableTestKey (bool, default true)
- testKey (KeyCode, default R)
- eliminationTimeout (float, default 5)

Event Subscriptions:
- NetworkedGameManager.OnRaceFinished -> HandleRaceFinished
- EliminationTracker.OnMatchResultsReady -> HandleMatchResults
- Damageable.onDestroyed -> HandleLocalShipDestroyed (per ship)

Key Flow:
1. HandleMatchResults receives real placements from EliminationTracker
2. Extracts local player's placement
3. Checks wallet connection via Web3Manager
4. Calls BattleRewardManager.DistributeRewards
5. Shows PostMatchRewardUI with placement and rewards
6. Fallback: if EliminationTracker times out, calculates placement from alive/dead ship count

### ShipNFTManager.cs
Singleton managing ship NFT ownership. Queries ERC-1155 contract for balances. Supports 2-slot selection: Primary and Secondary ships must have different meshRootIndex (different ship types).

Contract: 0x8405209745b8f1A43D21876120543d20e4a7600C

Serialized Fields:
- contractAddress (string)
- ships (List of ShipDefinition)

Ship Types (by meshRootIndex):
- 0 = Aircraft A (Spaceship)
- 1 = Aircraft B (Vimana)

ShipDefinition fields: displayName, description, tokenId (int, NFT token ID), rarity (Common/Uncommon/Rare/Legendary), meshRootIndex (0 or 1), isDefault (bool), isLocked (bool), icon (Sprite)

Key Methods:
- FetchShipBalances(): async, queries balanceOf for each non-default ship via ThirdwebContract.Read
- SelectShipForSlot(int slot, ShipDefinition ship): validates ownership, lock status, and meshRootIndex conflict
- OwnsShip(ShipDefinition): default ships always return true

Events:
- OnShipsFetched(List of ShipDefinition)
- OnShipsConfirmed(ShipDefinition[])
- OnFetchError(string)

### CharacterNFTManager.cs
Singleton managing character NFT ownership. Queries same ERC-1155 contract. 4-slot selection system: 2 characters per ship roster. Auto-selects defaults into slots 0 and 2.

8 Characters total across 2 rosters:
- Roster A (shipRosterIndex 0, Spaceship): 4 characters
- Roster B (shipRosterIndex 1, Vimana): 4 characters

CharacterDefinition fields: displayName, description, tokenId, rarity (Common/Rare/Epic/Legendary), shipRosterIndex (0 or 1), isDefault (bool), isLocked (bool), icon (Sprite), characterStats (ScriptableObject)

Rarity Multipliers (applied to base stats):
- Common: 1.00x
- Rare: 1.06x
- Epic: 1.12x
- Legendary: 1.18x

Character Names: Aaryaveer, Ishvaya, Kaevik, Nysera, Roudra, Virexa, Vyanika, Zorvan

Key Methods:
- FetchCharacterBalances(): async, queries contract for each non-default character
- SelectCharacterForSlot(int slot, CharacterDefinition): validates slot (0-3), lock, ownership
- GetCharactersForShip(int shipRosterIndex): filters by roster
- ConfirmCharacters(): validates all 4 slots filled, fires OnCharactersConfirmed

### ShipSelectionUI.cs
Ship selection screen shown after wallet connection. Click-order selection: 1st click = Primary, 2nd click = Secondary. Ships must be different types (different meshRootIndex). Confirm button enables when both slots filled. Shows lore popup on icon click.

### CharacterSelectionUI.cs
Character selection screen shown after ship selection. Displays characters from both rosters in separate rows. Click-order selection: 1st click = Primary, 2nd click = Secondary per roster. Requires 4 total selections. Shows lore popup on icon click.

### ShipCardUI.cs and CharacterCardUI.cs
Reusable card components for selection screens. Display name, rarity, icon. Show lock overlay if not owned. Show selection highlight when picked. Show slot badge text (Primary/Secondary).

### WalletConnectPanel.cs
Wallet connection UI panel. Shows login options: email, Google, Discord, guest, external wallet. Orchestrates the full flow: Wallet Connect -> Ship Selection -> Character Selection -> Room Lobby -> Gameplay.

Serialized Fields:
- All button references (email, Google, Discord, guest, external)
- emailInputField (TMP_InputField)
- showAfterConnect (ship selection panel)
- characterSelectionPanel
- roomLobbyPanel
- gameplaySceneName (default: "MP_Mechanics_6")

### WalletHUD.cs
In-game HUD displaying wallet address, AVAX balance, and PRANA token balance. Auto-creates token balance text if not assigned. Refreshes balances every 30 seconds.

### PostMatchRewardUI.cs
Post-match popup showing placement (1st/2nd/3rd/4th), token reward earned, bonus rewards (XP/Energy/Gems/Coins), minting status, and total balance. Auto-creates UI if no panel assigned. Uses gold color scheme (#e6b818). Panel size 500x470 pixels.

---

## Event-Driven Architecture

The Web3 layer is fully decoupled from gameplay via events:

Web3Manager fires:
- OnWalletConnected -> WalletHUD updates, ShipNFTManager fetches ships, CharacterNFTManager fetches characters, BattleRewardManager fetches balance
- OnWalletDisconnected -> all systems reset
- OnBalanceUpdated -> WalletHUD updates AVAX display
- OnError -> WalletConnectPanel shows error

BattleRewardManager fires:
- OnRewardsDistributed -> PostMatchRewardUI shows results
- OnRewardMinted -> per-mint notification
- OnTokenBalanceUpdated -> WalletHUD updates PRANA display
- OnRewardError -> PostMatchRewardUI shows error

ShipNFTManager fires:
- OnShipsFetched -> ShipSelectionUI populates cards
- OnShipsConfirmed -> WalletConnectPanel transitions to character selection

CharacterNFTManager fires:
- OnCharactersFetched -> CharacterSelectionUI populates cards
- OnCharactersConfirmed -> WalletConnectPanel transitions to room lobby

All blockchain calls use async/await. None block the game loop.

---

## Scripting Define Symbols (Web3 related)

- THIRDWEB_REOWN: enables Reown AppKit integration in Thirdweb SDK
- Present on Standalone platform build

## IL2CPP Linker Preservation

The project preserves all types from: Thirdweb.dll, Nethereum.Web3, Nethereum.Contracts, Nethereum.Accounts, Nethereum.Signer, Nethereum.Signer.EIP712, Nethereum.HdWallet, Nethereum.KeyStore, Nethereum.JsonRpc, Nethereum.RLP, Nethereum.ABI, Nethereum.Util, Newtonsoft.Json, Portable.BouncyCastle, ADRaffy.ENSNormalize


---
---
---

# PART 2: MULTIPLAYER AND NETWORKING

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


---
---
---

# PART 3: GAMEPLAY, PROJECT STRUCTURE, AND TECH STACK

## Gameplay Overview

MythiX is a 4-player multiplayer battle royale where players fly mythic-futuristic ships (Vimanas) and fight in a shrinking zone arena. The game integrates Avalanche blockchain for NFT ship ownership and play-to-earn PRANA token rewards.

### Core Loop

1. Connect wallet (5 auth options)
2. Select Primary and Secondary ships (NFT-gated, free default available)
3. Select 4 characters (2 per ship roster, NFT-gated, free defaults available)
4. Create or join match via 4-character room code
5. Battle in shrinking zone arena (500m to 0 over 60 seconds)
6. 10 DPS to anyone outside zone
7. Last ship standing wins
8. PRANA tokens minted to all player wallets based on placement
9. Post-match screen shows on-chain tx confirmation

### Ship System

Two ship types (by meshRootIndex):
- Type 0: Spaceship (Aircraft A)
- Type 1: Vimana (Aircraft B)

Players select Primary and Secondary ships. Must be different types (enforced by meshRootIndex conflict check). Ships can be swapped mid-match (BUTTON_SWAP_AIRCRAFT). Swap has a 1.0 second grace period (SWAP_GRACE_PERIOD).

Ship properties: displayName, description, tokenId (NFT), rarity (Common/Uncommon/Rare/Legendary), meshRootIndex, isDefault, isLocked, icon

### Character System

8 playable characters: Aaryaveer, Ishvaya, Kaevik, Nysera, Roudra, Virexa, Vyanika, Zorvan

Split across 2 rosters:
- Roster A (shipRosterIndex 0, Spaceship): 4 characters
- Roster B (shipRosterIndex 1, Vimana): 4 characters

4 slots total: [0] Roster A Primary, [1] Roster A Secondary, [2] Roster B Primary, [3] Roster B Secondary

Rarity tiers with stat multipliers:
- Common: 1.00x
- Rare: 1.06x
- Epic: 1.12x
- Legendary: 1.18x

Each character has a ScriptableObject (CharacterData) with base stats that get multiplied by rarity. Characters also have lore displayed in a popup on icon click.

Character Lore ScriptableObjects: Aaryaveer, Ishvaya, Kaevik, Nysera, Roudra, Virexa, Vyanika, Zorvan

### Battle Zone

- Initial radius: 500 meters
- Final radius: 0 meters
- Shrink duration: 60 seconds
- Damage: 10 per second to anyone outside zone
- Damage applied every 1 second interval
- Visual sphere scales down in real-time
- UI timer shows remaining time
- Auto-starts on match begin

### Elimination System

- EliminationTracker monitors all VehicleHealth components
- When a ship is destroyed, player is eliminated
- First eliminated = last place (4th)
- Last standing = 1st place
- Results broadcast to all clients via RPC
- BattleRewardBridge listens for results and triggers reward distribution

### Power-Up System

Power types available:
- Shield: temporary invincibility (NetworkShieldHandler manages state)
- Invisibility: stealth mode (InvisibilityHandler)
- Super Boost: speed burst (AircraftSuperBoostHandler)
- Super Weapon: enhanced weapon stats with multipliers for damage, range, speed, fire rate, reload (NetworkSuperWeaponHandler, separate multipliers for projectiles and missiles)
- Teleport: dice-based teleportation (TeleportPowerUp)

All power-ups route through NetworkPowerBridge: client requests activation via RPC, host executes through type-specific handler.

### Weapon System

Button mappings:
- Primary Fire (BUTTON_FIRE_PRIMARY = 0)
- Secondary Fire (BUTTON_FIRE_SECONDARY = 1)
- Missile Fire (BUTTON_FIRE_MISSILE = 2)
- Cycle Weapon (BUTTON_CYCLE_WEAPON = 3)
- Cycle Character (BUTTON_CYCLE_CHARACTER = 4)
- Swap Aircraft (BUTTON_SWAP_AIRCRAFT = 5)

Aim system: virtual reticle driven by mouse delta, viewport-space (0-1, center at 0.5, 0.5), clamped to maxReticleDistance 0.475 with aspect correction, no auto-center. Aim position synced from client to host via RPC_SendAimPosition. Target lock synced via RPC_SendTargetLock.

### Controls

- W/S: throttle (auto-forward support available)
- A/D: strafe
- Ctrl/Space: vertical movement
- Tab: boost
- Mouse: aim (virtual reticle)
- Left click: primary fire
- Right click: secondary fire
- R key: test rewards (debug, when enabled)

---

## Complete Project Structure

```
Prototype_GV2/
├── .gitignore
├── README.md
├── docs/
│   ├── CLAUDE.md
│   ├── CharacterLore.md
│   ├── ClawdCoWork_Migration/
│   ├── Co Work Context/
│   │   └── MythiX_Stage3_Full_Context.md (this file)
│   ├── Multiplayer_Debug_Session_Context.md
│   ├── SCK_D.md
│   ├── Session_Mar1_DedicatedServer.md
│   ├── Turret_Multiplayer_Issues.docx
│   ├── Web3_Integration_Memory.md
│   ├── mint_count_and_improvements.md
│   └── plan.md
├── scripts/
│   ├── deploy.ps1
│   └── push-build.ps1
├── Assets/
│   ├── GV/
│   │   ├── Audio/
│   │   │   ├── Spaceship/
│   │   │   └── Vimana/
│   │   ├── Buttons/
│   │   ├── Characters/
│   │   │   └── Sprites/
│   │   ├── Data/
│   │   │   ├── CharacterLore/ (8 ScriptableObjects)
│   │   │   ├── Characters/ (8 ScriptableObjects)
│   │   │   ├── ModuleTypes/
│   │   │   │   ├── SpaceshipModuleType.asset
│   │   │   │   └── VimanaModuleType.asset
│   │   │   └── ShipLore/ (4 ScriptableObjects)
│   │   ├── Documentation/
│   │   ├── FBX/
│   │   ├── Fonts/ (KumarOne-Regular SDF variants)
│   │   ├── HDRI/
│   │   ├── Materials/
│   │   │   ├── Additive/
│   │   │   ├── Astra Pranas/
│   │   │   ├── Mountains/
│   │   │   ├── Spaceship/ (7 material variants)
│   │   │   └── Vimana/ (10 material variants)
│   │   ├── Meshes/
│   │   │   ├── Mountains/ (7 biomes: ruins, crystal, desert, forest, forest green, snowy, volcanic)
│   │   │   └── Tris/ (6 Mountain_Tri mesh variants)
│   │   ├── Prefabs_GV/
│   │   │   ├── Gyro variants, Exhausts
│   │   │   ├── Mountains/
│   │   │   ├── Mounts/Turrets/
│   │   │   ├── Network/
│   │   │   ├── References/
│   │   │   ├── Weapon Cubes/
│   │   │   └── Web3/Vehicle Icons/
│   │   ├── Renderer/
│   │   ├── Ring/
│   │   ├── Scenes/
│   │   ├── Scripts/
│   │   │   ├── AI/ (enemy spaceship behaviors)
│   │   │   ├── Character/ (character data, selection)
│   │   │   ├── Combat/ (weapon, damage systems)
│   │   │   ├── Debug/ (debug utilities)
│   │   │   ├── Editor/ (Unity editor tools)
│   │   │   ├── Network/ (23 Photon Fusion scripts)
│   │   │   ├── Race Network/ (race-specific networking)
│   │   │   ├── Swap/ (aircraft swap mechanics)
│   │   │   ├── Teleport/ (dice-based teleportation)
│   │   │   ├── UI/ (UI systems)
│   │   │   ├── Vehicle/ (Vimana ship systems)
│   │   │   ├── Weapons/ (weapon systems)
│   │   │   └── Web3/ (13 blockchain scripts)
│   │   ├── Thara Enhancements/
│   │   └── Ui/
│   │       ├── Energy Meter/
│   │       └── Reticle/
│   ├── Thirdweb/ (SDK with plugins, prefabs, IL2CPP config)
│   └── (SpaceCombatKit, Photon Fusion, other third-party assets)
├── Packages/
│   └── manifest.json
└── ProjectSettings/
    └── ProjectSettings.asset
```

---

## Full Tech Stack

### Engine and Rendering
- Unity 6000.3.0f1
- Universal Render Pipeline (URP) 17.3.0
- Post Processing Stack v2
- Cinemachine 2.10.5
- Vector Graphics (preview)
- Linear color space
- Forward rendering path

### Blockchain / Web3
- Avalanche C-Chain, Fuji Testnet (Chain ID 43113)
- Thirdweb Unity SDK v6 (wallet connect, NFT reads, balance queries)
- Nethereum v5.0.0 (ERC-20 mintTo write transactions)
- Reown AppKit v1.5.2 (MetaMask, Coinbase Wallet, WalletConnect)
- ERC-1155 (ship and character NFTs)
- ERC-20 (PRANA reward token)
- EIP-712 signing support (via Reown.Sign.Nethereum)

### Multiplayer Networking
- Photon Fusion 2.0.9
- Host/Client topology
- RPC-based input (bypasses Fusion IL weaver issues)
- Manual position sync
- HTTP API for dedicated server room management

### Gameplay Framework
- SpaceCombatKit (VSX) with 20+ assembly definitions
- VehicleEngines3D for ship physics
- WeaponsController for aim and firing
- Damageable/VehicleHealth for health system
- ResourceContainer for ammo/fuel
- Trackable for target locking

### Input
- New Input System 1.16.0
- Mouse + Keyboard
- Virtual reticle system

### UI
- TextMesh Pro
- Canvas-based UI (uGUI 2.0.0)
- Auto-generated UI panels (PostMatchRewardUI, WalletHUD)

### Audio
- Per-ship audio (Spaceship and Vimana folders)
- NetworkAudioHelper prevents duplicate playback in multiplayer

### Tools and Development
- Rider IDE integration
- Visual Studio integration
- ParrelSync (parallel editor instances for multiplayer testing)
- Unity Recorder 5.1.3
- ProBuilder 6.0.8
- Unity Test Framework 1.6.0
- Tripo3D Unity Bridge (local package)
- Unity MCP (GitHub package)

### Build Targets
- PC/Standalone (primary)
- Android (configured)
- iOS (configured)
- WebGL (configured)
- Dedicated Server (UNITY_SERVER define)
- Console (Switch, PS4/PS5, Xbox configured in ProjectSettings)

### Build Configuration
- Scripting Backend: IL2CPP
- API Compatibility: .NET 6
- Allow Unsafe Code: enabled
- Deterministic Compilation: enabled
- GPU Skinning: enabled
- Strip Engine Code: enabled
- Dedicated Server Optimizations: enabled

---

## Script Folders Summary

### Assets/GV/Scripts/Web3/ (13 files)
Blockchain integration layer. Wallet connection, NFT ownership queries, token minting, balance display, selection UIs, post-match rewards.

### Assets/GV/Scripts/Network/ (23 files)
Photon Fusion multiplayer layer. Game state machine, elimination tracking, battle zone, health sync, input bridging, power-up networking, room management, dedicated server bootstrap.

### Assets/GV/Scripts/AI/
Enemy spaceship AI behaviors.

### Assets/GV/Scripts/Character/
Character data definitions, selection logic.

### Assets/GV/Scripts/Combat/
Weapon systems, damage calculation, hit detection.

### Assets/GV/Scripts/Debug/
Debug utilities and visualization tools.

### Assets/GV/Scripts/Editor/
Unity Editor custom inspectors and tools.

### Assets/GV/Scripts/Race Network/
Race-specific networking components (checkpoint tracking, lap counting).

### Assets/GV/Scripts/Swap/
Aircraft swap mechanics (switching between Primary and Secondary ships mid-match).

### Assets/GV/Scripts/Teleport/
Dice-based teleportation power-up system.

### Assets/GV/Scripts/UI/
General UI systems (menus, HUD elements, loading screens).

### Assets/GV/Scripts/Vehicle/
Vimana ship systems, vehicle configuration, engine parameters.

### Assets/GV/Scripts/Weapons/
Weapon configuration, projectile systems, missile systems.

---

## Dependencies (from Packages/manifest.json)

### Scoped Registry (OpenUPM)
- com.reown (Reown AppKit packages)
- com.nethereum (Nethereum packages)

### Key Packages
- com.reown.appkit.unity: 1.5.2
- com.unity.render-pipelines.universal: 17.3.0
- com.unity.postprocessing: 3.5.1
- com.unity.cinemachine: 2.10.5
- com.unity.inputsystem: 1.16.0
- com.unity.ugui: 2.0.0
- com.unity.probuilder: 6.0.8
- com.unity.recorder: 5.1.3
- com.unity.vectorgraphics: 3.0.0-preview.2
- com.unity.collab-proxy: 2.10.2
- com.unity.multiplayer.center: 1.0.1
- com.unity.test-framework: 1.6.0
- com.unity.ide.rider: 3.0.38
- com.unity.ide.visualstudio: 2.0.25
- com.unity.timeline: 1.8.9

### GitHub Packages
- com.coplaydev.unity-mcp (Unity MCP support)
- com.veriorpies.parrelsync (parallel editor instances)
- com.unity.vectorgraphics (preview)

### Local Packages
- com.tripo3d.unitybridge (Tripo3D Unity Bridge, local file path)

---

## Art and Assets

### Environments
7 mountain biomes: Ancient Ruins, Crystal, Desert, Forest, Forest Green, Snowy, Volcanic
6 Mountain Tri mesh variants for terrain generation

### Ships
Multiple material variants per ship type:
- Spaceship: Mandala (3 variants), Metal Texture, Red Metal (2 variants), Mandala Red Plain
- Vimana: Copper, Garuda, Gold, Gold Modern, Metal Pattern, Metal Sci-Fi, Standard, Test, URP, Wood Gate

### Characters
8 characters with individual:
- Sprites (Assets/GV/Characters/Sprites/)
- CharacterData ScriptableObjects (Assets/GV/Data/Characters/)
- CharacterLore ScriptableObjects (Assets/GV/Data/CharacterLore/)

### Ship Lore
4 ship lore ScriptableObjects: DroneShooter, Spaceship, VimanaShip, Vimana

### Module Types
SpaceshipModuleType.asset, VimanaModuleType.asset

### Prefabs
- Gyro variants (shield, super boost, invisible, random power)
- Mountains
- Mounts/Turrets
- Network prefabs
- Weapon Cubes
- Web3/Vehicle Icons
- Exhaust effects

### Fonts
KumarOne-Regular SDF (multiple variants for different text sizes)

### UI Assets
Energy Meter, Reticle graphics

---

## Scenes

Entry point: Assets/GV/Scenes/Bootstrap.unity
Gameplay scene: MP_Mechanics_6
Flow: Bootstrap -> Main Menu -> Connect Wallet -> Ship Selection -> Character Selection -> Room Lobby -> Gameplay

---

## Deployment Scripts

Located in: scripts/

### deploy.ps1
PowerShell deployment script for building and deploying.

### push-build.ps1
PowerShell script for pushing builds.

---

## .gitignore Highlights

Ignores:
- Standard Unity ignores (Library, Temp, Obj, Build, Builds, Logs, UserSettings, MemoryCaptures, Recordings)
- .utmp/, .agent/, *.slnx
- /ServerBuild/ (compiled server binaries, 38MB+)
- Debug logs: timingsynclogs*.txt, jointest*.txt, *_Debug_notepad*
- *_DoNotShip/

---

## Stage 1 and Stage 2 Submission Summary

### Stage 1 (completed)
Submitted project overview, team info, and initial demo.

### Stage 2 (drafted)
All 11 fields drafted:
1. GitHub Repository (MythiX_GV branch)
2. Technical Documentation
3. Architecture Design Overview
4. User Journey
5. MoSCoW Framework (with known multiplayer bugs acknowledged)
6. Walkthrough Video
7. Live Prototype Links
8. What is Currently Playable
9. Smart Contracts Deployed (with Snowtrace links)
10. New Player Onboarding Flow
11. Playtesting Results (with known networking edge cases acknowledged)

### Branch
All work is on branch: MythiX_GV
Main branch does not contain latest code.
