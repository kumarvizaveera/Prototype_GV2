# WORKLOG

## Chronological Milestones

### **Feb 06 - Feb 08: Foundations of Gameplay & Aesthetic**
-   **Objectives**: Linked turret destruction to gyro ring disappearance (`TurretDestructionLinker`).
-   **Aesthetics**: Rebalanced color palette towards "Mythic Futurism" (Electric Cyan, Neon Magenta, Holographic Green). Implemented 'Cinzel' font.
-   **Controls**: Added 'S' key braking to Spaceship Controls (`NetworkedPlayerInput`).
-   **Debug**: Investigated disabled checkpoint labels.

### **Feb 09 - Feb 10: Core Networking & Combat**
-   **Movement**: Fixed network synchronization for Boost functionality.
-   **Combat**: Resolved projectile damage issues (Ship self-collision, root transform errors).
-   **Multiplayer**: Initiated network race testing workflows (Host/Client builds).

### **Feb 11 - Feb 12: Advanced Mechanics & Power Systems**
-   **Power-Ups**: Connected all power-ups to `PowerSphereMasterController` for centralized duration management.
-   **UI**: Linked scene UI (Timers) to networked player prefabs (`NetworkedGameManager`, `NetworkedPlayerInput`).
-   **Teleportation**: Implemented +/- 6 Dice Logic (`Dice_TMP`) and checkpoint spawning tolerances.
-   **Issues**: Debugged major teleport failures and "sticking" camera bugs in multiplayer.

### **Feb 13 - Feb 14: Polishing & Battle Royale Mode**
-   **Battle Royale**: Implemented shrinking sphere mechanic (`BattleZoneController`) and networked health sync.
-   **Refill Systems**: Created networked missile refill spawners (`AstraMissileRefill`, `NetworkedResourceSync`).
-   **Debug Tools**: Built in-game debug console for runtime logging.
-   **Network**: Addressed "Region: Unknown" connection errors in Photon App ID setup.

## Major Design Decisions
1.  **Networking**: Transitioned to **Photon Fusion 2** for robust state synchronization.
2.  **Aesthetics**: Pivot to "Mythic Sci-Fi" (Vimanas/Astras) over generic space themes.
3.  **Power Management**: Centralized logic (`PowerSphereMasterController`) to avoid scattered magic numbers.
4.  **Game Mode**: Introduction of "Battle Royale" shrinking zone to force combat engagement.

## What Worked
-   **Photon Fusion**: Successfully synced complex movement (Boost/Flight) and object spawning.
-   **Centralized Config**: Master Controller for power-ups reduced iteration time significantly.
-   **Visual Identity**: Color palette shift created a unique, identifiable look.

## What Failed / Challenges
-   **Teleportation Desync**: Immediate position changes fight against network interpolation, causing visual glitches.
-   **Projectile Ownership**: Difficulty in reliably assigning damage source in networked environments ("hitting own ship").
-   **Connection Stability**: Issues with Photon App ID region locking ("Region: Unknown").

## Current Blockers
-   **"Region: Unknown" Error**: Prevents consistent multiplayer testing.
-   **Teleport Stability**: Needs a more robust networked execution to prevent player desync.

## Immediate Next Steps
1.  **Resolve Photon Region**: Fix App ID/Region settings in `PhotonAppSettings`.
2.  **Verify Battle Royale**: Full playtest of shrinking zone logic with multiple clients.
3.  **Stabilize Teleport**: Implement RPC-driven teleport commands with interpolation reset.
