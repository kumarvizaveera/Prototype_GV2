# PROJECT_CONTEXT

## High-Level Game Concept
**Title**: [Project Code Name: GV2]
**Genre**: Sci-Fi Combat Racing / Battle Royale
**Theme**: Mythic Futurism. Ancient Vedic mythology (Vimanas, Astras) blended with high-tech sci-fi aesthetics.
**Core Loop**: High-speed racing on twisted tracks, utilizing strategic power-ups ("Pranas"), deploying "Astra" weapons, and navigating dynamic shortcuts via teleportation logic.

## Core Mechanics

### 1. Movement & Traversal
-   **Vimana Flight**: Physics-based flight with "Super Boost" capabilities (`AircraftSuperBoostHandler`).
-   **Spline Navigation**: Track-based movement assistance with `Path Auto Pilot` and `SplineTether`.
-   **Teleportation**:
    -   **Dice Logic**: Random outcome (+/- 6) determines teleport destination (`Dice_TMP`).
    -   **Shortcut Crystals**: Strategic jumps triggered by `ShortcutCrystalJump`.
    -   **Checkpoint Teleport**: Fallback mechanisms (`CheckpointTeleporter`).

### 2. Combat Systems ("Astras")
-   **Missiles**: Networked projectile system managed by `MissileCycleControllerDynamic` and `AstraMissileRefill`.
-   **Turrets**: Stationary defenses on the track, linked to objective systems (`TurretDestructionLinker`).
-   **Battle Zone**: Shrinking sphere mechanic for Battle Royale mode (`BattleZoneController`).

### 3. Power Systems ("Pranas")
-   **System**: Centralized management via `PowerSphereMasterController`.
-   **Types**:
    -   **Super Boost**: Extreme speed burst.
    -   **Invisibility**: Stealth mode (`InvisibilityPowerUp`, `InvisibilityHandler`).
    -   **Shields**: Defensive barriers (`ShieldPowerUp`, `NetworkShieldHandler`).
    -   **Super Weapon**: Ultimate attack capability (`SuperWeaponOrb`, `NetworkSuperWeaponHandler`).

## Current Prototype Status
-   **Phase**: Multiplayer Testing (Phase 5).
-   **Features Active**:
    -   Photon Fusion networking integrated for movement, combat, and syncing.
    -   Lobby and automated matching (`NetworkedGameManager`).
    -   Battle Royale mechanic (Shrinking Zone).
    -   Procedural/Randomized pickup spawning (`MassObjectSpawner`, `AstraRefillSpawner`).
-   **Stability**: Core gameplay functional; focusing on edge-case debugging (teleport glitches, lag compensation).

## Technical Stack
-   **Engine**: Unity 6000.3.0f1
-   **Networking**: Photon Fusion 2
-   **Language**: C#
-   **Key Assets**: Space Combat Kit (Modified), ProBuilder.
-   **Platform**: Windows (Primary), potentially Cross-platform.

## Long-Term Vision
Create a "Mythic Sports" ecosystem where players customize Vimanas with distinct "Astras" and navigate non-Euclidean tracks. The goal is a highly replayable, competitive experience blending *Wipeout* speed with *Mario Kart* chaos, wrapped in a serious, "Ancient Aliens" aesthetic.
