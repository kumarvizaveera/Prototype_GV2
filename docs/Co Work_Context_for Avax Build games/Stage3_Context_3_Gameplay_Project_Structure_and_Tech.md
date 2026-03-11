# MythiX Battle Royale - Stage 3 Context (Part 3: Gameplay, Project Structure, and Tech Stack)

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
│   ├── Multiplayer_Debug_Session_Context.md
│   ├── SCK_D.md
│   ├── Session_Mar1_DedicatedServer.md
│   ├── Turret_Multiplayer_Issues.docx
│   ├── Web3_Integration_Memory.md
│   ├── mint_count_and_improvements.md
│   ├── plan.md
│   ├── Stage3_Context_1_Overview_and_Web3.md
│   ├── Stage3_Context_2_Multiplayer_and_Networking.md
│   └── Stage3_Context_3_Gameplay_Project_Structure_and_Tech.md
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
