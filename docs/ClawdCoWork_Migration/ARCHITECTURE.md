# ARCHITECTURE

## Folder Structure Overview
The project is organized under `Assets/GV` to isolate custom logic from third-party assets (`SpaceCombatKit`, `Photon`).

-   **Assets/GV/**
    -   **Scripts/**: Core C# logic.
        -   **Network/**: Photon Fusion integration (`NetworkManager`, `NetworkedGameManager`).
        -   **Race Network/**: Race-specific networking logic (`CheckpointNetwork`, `RaceCheckpoint`).
        -   **Teleport/**: Teleportation logic (`Dice_TMP`, `ShortcutCrystalJump`).
        -   **Combat/**: Weapon logic, targets.
        -   **Vehicle/**: Vehicle-specific controllers.
        -   **AI/**: Bot logic.
        -   **Weapons/**: Weapon data.
    -   **Prefabs_GV/**: Pre-configured objects (Vimanas, Astras, Pickups).
    -   **Materials/Astra Pranas**: Custom shaders and materials for visual effects.

## Core Systems

### 1. Networking Infrastructure (Photon Fusion)
-   **NetworkManager**: Singleton managing connection state and callbacks.
-   **NetworkedGameManager**: Handles game state, match start/end, and player spawning.
-   **Synchronization**:
    -   **Movement**: `NetworkedSpaceshipBridge` syncs transform/rigidbody.
    -   **Input**: `NetworkedPlayerInput` captures local input and sends to host.
    -   **State**: `NetworkedHealthSync`, `NetworkedResourceSync` (Ammo/Fuel).

### 2. Power Logic ("Prana System")
-   **Master Controller**: `PowerSphereMasterController` defines global constants (durations, strengths).
-   **Individual Power-Ups**: `SuperBoostOrb`, `InvisibilityPowerUp`, etc., read from Master Controller.
-   **Effect Handling**: Local visual effects trigger immediately; state changes (invulnerability, speed) are synced via Networked Properties.

### 3. Teleport System
-   **Dice Mechanic**: `Dice_TMP` rolls a random value (+/- 6).
-   **Execution**: `CheckpointTeleporter` moves the player to the target checkpoint index.
-   **Synchronization**: Teleport events must update the `NetworkedCheckpointTracker` to prevent desync.

### 4. Astra Weapon System
-   **Inventory**: `MissileCycleControllerDynamic` manages missile types and counts.
-   **Spawning**: `AstraRefillSpawner` places pickups dynamically.
-   **Firing**: Networked RPCs trigger launch; `AstraMissileRefill` handles replenishment logic.

## Data Flow Summary
1.  **Input**: User Input (W/A/S/D, Space) -> `NetworkedPlayerInput`.
2.  **Processing**: Input sent to Host -> `NetworkedSpaceshipBridge` applies physics.
3.  **State Update**: Host updates Position/Rotation -> Synced to Clients via `NetworkTransform` (or custom interpolation).
4.  **Events**: Collisions (Pickup/Hit) -> Trigger RPCs -> Update `NetworkedHealthSync` / `NetworkedResourceSync`.

## Dependencies & APIs
-   **Photon Fusion 2**: Real-time networking.
-   **Space Combat Kit (SCK)**: Base flight physics and camera systems (heavily modified).
-   **ProBuilder**: Level geometry.
-   **Unity Input System**: New input handling.

## Known Technical Risks
-   **Teleport Desync**: Instant position changes can cause interpolation artifacts in Fusion. Needs rigorous `Teleport()` handling.
-   **Race Condition**: Pickup collection latency (two players hitting same orb) resolved by "First claimed by Host" logic.
-   **Performance**: High-poly Vimanas + Real-time lighting + Network overhead on low-end devices.
