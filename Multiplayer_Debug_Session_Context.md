# Multiplayer Debug Session - Full Context Transfer
## Unity Space Combat Kit + Photon Fusion 2
### Date: February 15, 2026

---

## PROJECT OVERVIEW

- **Engine**: Unity 6.0.3.0f1
- **Networking**: Photon Fusion 2 (Host/Client model)
- **Game Framework**: Space Combat Kit (VSX Games)
- **Project Path**: `Assets/GV/Scripts/Network/`
- **Photon AppID**: `4f22d424-faf1-48c0-a0f6-ef7db60063dc`
- **Session Name**: `GV_Race`
- **Region**: `us`
- **Max Players**: 4

---

## FILES MODIFIED

### 1. NetworkedSpaceshipBridge.cs (HEAVILY MODIFIED - Main file)
**Path**: `Assets/GV/Scripts/Network/NetworkedSpaceshipBridge.cs`

This is the bridge between Photon Fusion networking and SCK's VehicleEngines3D. All major fixes live here.

**Current code structure:**

```csharp
public override void Spawned()
{
    // Find engines (VehicleEngines3D) and triggerablesManager
    // Make Rigidbody kinematic if !HasStateAuthority (client-side fix for NetworkTransform conflict)
    // Determine isLocalPlayer via HasInputAuthority
    // If remote: DisableLocalInput()
    // If local: SetupCamera() with retry mechanism in Update()
}

private void DisableLocalInput()
{
    // 1. Disable assigned localInputScripts array
    // 2. Disable all VehicleInput components (namespace VSX.Vehicles)
    // 3. Disable all GeneralInput components (namespace VSX.Controls)
    // 4. Disable Unity PlayerInput components
    // 5. Unregister GameAgent from GameAgentManager, call EnterVehicle(vehicle) to activate modules, Destroy(ga)
    // 6. Re-disable input scripts that EnterVehicle may have reactivated
}

public override void FixedUpdateNetwork()
{
    // FIRST TICK one-time debug log (unconditional)
    // CheckAuthorityAndSetupInput()
    // engines null check with warning
    // GetInput<PlayerInputData>() with warning if no value

    // MOVEMENT: Only if HasStateAuthority && !HasInputAuthority (host processing remote ship)
    //   - Force-activate engines if not activated
    //   - ALWAYS set engines.ControlsDisabled = false (EVERY TICK - critical fix)
    //   - SetSteeringInputs, SetMovementInputs, SetBoostInputs
    //   - Debug log every 2 seconds (throttled)

    // WEAPONS: if !isHostLocalShip && (HasStateAuthority || HasInputAuthority)
    //   - ApplyWeaponInput(data) - handles primary, secondary, missile fire
}
```

### 2. NetworkManager.cs (NOT MODIFIED - Read only)
**Path**: `Assets/GV/Scripts/Network/NetworkManager.cs`
- Handles Photon Fusion connection, player spawning in OnPlayerJoined
- Spawns with `runner.Spawn(playerPrefab, spawnPos, spawnRot, inputAuthority: player)`
- Camera setup via `SetupCameraFollow()` for local player on host

### 3. NetworkedPlayerInput.cs (MODIFIED)
**Path**: `Assets/GV/Scripts/Network/NetworkedPlayerInput.cs`
- Collects keyboard/mouse input in Update(), sends via OnInput() callback
- `enableAutoForward` field — **code default changed to `false`** (was `true`)
- When autoForward is off: W = forward, S = reverse (no automatic throttle)
- When autoForward is off: Shift = boost (instead of W)
- Input struct: `PlayerInputData` with steering (Vector3), movement (Vector3), boost (NetworkBool), buttons (NetworkButtons)

### 4. Player_ForMultiplayer.prefab
**Path**: Prefab used for network spawning
- Has: NetworkObject, NetworkTransform, Rigidbody, GameAgent (isPlayer=true, startingVehicle=null), VehicleEngines3D, various VehicleInput scripts
- Does NOT have: GameAgentManager (scene singleton), NetworkRigidbody3D
- 4 inactive GameObjects (UI elements only)

---

## ALL BUGS FOUND AND FIXED (Chronological)

### Bug 1: Both ships moving identically (FIXED)
**Cause**: Native SCK input scripts (VehicleInput, GeneralInput, PlayerInput_InputSystem) were active on ALL ships, all reading the same keyboard.
**Fix**: Comprehensive `DisableLocalInput()` in `Spawned()` that disables VehicleInput (VSX.Vehicles), GeneralInput (VSX.Controls), and Unity PlayerInput components on remote ships. Moved from FixedUpdateNetwork to Spawned() for immediate effect.

### Bug 2: Wrong namespace for GeneralInput (FIXED)
**Cause**: Used `VSX.GeneralInput` but actual namespace is `VSX.Controls.GeneralInput`.
**Fix**: Added `using VSX.Controls;`

### Bug 3: Weapons not firing on client (FIXED)
**Cause**: `FixedUpdateNetwork()` had early return at `if (!Object.HasStateAuthority) return;`, so client never processed weapon input.
**Fix**: Restructured FixedUpdateNetwork into separate MOVEMENT section (host-only for remote) and WEAPON section (both host and client). Added `ApplyWeaponInput()` method. Skipped host's own ship to avoid double-firing with native SCK scripts.

### Bug 4: Ship invisible on other screen (FIXED)
**Cause**: SCK's ModuleManagers (which control renderers, weapons, engines) only activate via `Vehicle.OnEntered()`. The prefab's GameAgent has `startingVehicle = null`, so EnterVehicle never runs on remote ships.
**Fix**: In DisableLocalInput(): unregister GameAgent from GameAgentManager singleton, manually call `ga.EnterVehicle(vehicle)` to activate ModuleManagers, then `Destroy(ga)`. Then re-disable input scripts that EnterVehicle may have reactivated.

### Bug 5: Host ship hovering/spinning when GameAgent left enabled (FIXED)
**Cause**: Remote ship's GameAgent registering with the scene-level GameAgentManager singleton caused interference (focus switching, camera conflicts).
**Fix**: Part of Bug 4 fix — unregister + EnterVehicle + Destroy pattern.

### Bug 6: Ships stuck at spawn / position not syncing (FIXED)
**Cause**: Prefab has Rigidbody + NetworkTransform but NO NetworkRigidbody3D. On client, local Rigidbody physics fights NetworkTransform position updates.
**Fix**: Make Rigidbody kinematic when `!HasStateAuthority` in Spawned().

### Bug 7: Engines not producing force on remote ships (FIXED)
**Cause**: `VehicleEngines3D.FixedUpdate()` gates all force application behind `if (enginesActivated)` which defaults to false. Normally activated via `Engines.Start()` with `activateEnginesAtStart`, but the network flow bypasses this.
**Fix**: Added force-activation in FixedUpdateNetwork when processing remote ships: `engines.SetEngineActivation(true)` and `engines.ControlsDisabled = false`.

### Bug 8: ControlsDisabled silently rejecting all input (FIXED - latest)
**Cause**: SCK's `SetMovementInputs()`, `SetSteeringInputs()`, and `SetBoostInputs()` all have `if (controlsDisabled) return;` at the start — they silently reject ALL input when `controlsDisabled` is true. In our code, `engines.ControlsDisabled = false` was INSIDE the `if (!engines.EnginesActivated)` block. But `Engines.Start()` already activates engines via `activateEnginesAtStart = true` (which runs BEFORE our first FixedUpdateNetwork). So our condition `!engines.EnginesActivated` was always false, and `ControlsDisabled = false` **never executed**. Any default or runtime value of `controlsDisabled = true` would silently block all engine input forever.
**Fix**: Moved `engines.ControlsDisabled = false` OUTSIDE the activation check — now runs EVERY tick before setting inputs, ensuring inputs are never silently rejected.

### Bug 9: enableAutoForward code default didn't match user preference (FIXED - latest)
**Cause**: Code default was `enableAutoForward = true` but user wanted it `false` by default.
**Fix**: Changed serialized field default to `false` in NetworkedPlayerInput.cs.

---

## CRITICAL SCK GOTCHA: controlsDisabled
**This is the most subtle bug we found.** SCK's engine input methods (`SetMovementInputs`, `SetSteeringInputs`, `SetBoostInputs`) all silently return if `controlsDisabled` is true. There is NO warning, no log, nothing. The input just gets swallowed. Always ensure `ControlsDisabled = false` before calling any input setter, and do it EVERY tick because other SCK systems could re-enable it.

---

## CURRENT STATUS (Where we left off)

### What's been fixed (all in code, needs rebuild + test):
- Individual ship movement (input isolation per ship)
- Weapon firing on client side via networked input
- Ship visibility on both host and client (GameAgent → EnterVehicle → Destroy pattern)
- Rigidbody/NetworkTransform conflict resolved (kinematic on non-authority)
- Camera follows local player on both host and client
- Engine activation on remote ships (force-activate in FixedUpdateNetwork)
- **ControlsDisabled fix** — now set to false every tick (was stuck inside never-executed code path)
- **enableAutoForward** — code default changed to false

### What needs testing:
- **Rebuild and test**: All fixes are in code. User needs to rebuild and test.
- **Remote ship movement on host**: Press W on client (editor), verify the client's ship moves on host screen.
- **Position sync both directions**: host→client via NetworkTransform+kinematic, client→host via engine forces.
- **Weapons**: Verify weapons still fire correctly on both host and client.

### Debug log evidence from previous tests:
- Client's FIRST TICK log showed: `StateAuth:False, InputAuth:True, engines:SpaceFighter_Light, rb.isKinematic:True, pos:(-1234.50, -10.70, 2011.00)` — position far from origin means NetworkTransform IS syncing position from host
- Only ONE FIRST TICK appeared (client's own ship) — host's ship on client didn't log, needs investigation
- No yellow warnings appeared (good — means engines and input are being found)
- `[NetworkManager] NOT spawning - IsServer: False` is expected on client side

---

## SCK ARCHITECTURE NOTES (Important for understanding)

### Key SCK Classes and Namespaces:
- `VehicleEngines3D` (extends `Engines`) — applies forces via `m_rigidbody.AddRelativeForce()` and `m_rigidbody.AddRelativeTorque()` in `FixedUpdate()`, but ONLY when `enginesActivated = true`
- `Engines` base class — `enginesActivated` defaults to false, has `controlsDisabled` flag, `SetEngineActivation(bool)`, `activateEnginesAtStart` field
- **CRITICAL**: `SetMovementInputs()`, `SetSteeringInputs()`, `SetBoostInputs()` all silently return if `controlsDisabled == true`
- **CRITICAL**: `SetEngineActivation(true)` has early return if `enginesActivated` is already true: `if (setActivated == enginesActivated) return;`
- `GameAgent` — registers with GameAgentManager in Awake(), collects VehicleInput scripts, `EnterVehicle()`/`ExitAllVehicles()`, `startingVehicle` field (null on network prefab). `OnDestroy()` only unregisters from manager (does NOT exit vehicles — safe to Destroy).
- `GameAgentManager` — SCENE-LEVEL SINGLETON (not on prefab), manages focused game agent, camera, HUD
- `Vehicle` — `OnEntered()` activates ModuleManagers, `OnExited()` deactivates them
- `GeneralInput` — namespace `VSX.Controls`
- `VehicleInput` — namespace `VSX.Vehicles`, extends GeneralInput
- SCK Input Hierarchy: `GeneralInput` → `VehicleInput` → `PlayerInput_Base_SpaceshipControls` → `PlayerInput_InputSystem_SpaceshipControls`

### Module System:
ModuleManagers are children of the Vehicle that control subsystems (weapons, engines, visuals). They only activate when `Vehicle.OnEntered()` is called. This is why simply enabling renderers wasn't enough — the entire module system needs to be activated.

### Network Flow (How input gets from client to ship movement):
1. Client: `NetworkedPlayerInput.CollectInput()` reads keyboard → fills `PlayerInputData` struct
2. Client: `NetworkedPlayerInput.OnInput()` sends struct to Fusion
3. Host: `NetworkedSpaceshipBridge.FixedUpdateNetwork()` calls `GetInput<PlayerInputData>()`
4. Host: If HasStateAuthority && !HasInputAuthority (remote ship):
   a. Force-activate engines if needed
   b. Set `ControlsDisabled = false` (EVERY TICK)
   c. Apply steering/movement/boost to SCK engines
5. Host: `VehicleEngines3D.FixedUpdate()` applies Rigidbody forces (IF enginesActivated AND controlsDisabled is false)
6. Host: NetworkTransform syncs position back to client
7. Client: Rigidbody is kinematic, so it accepts NetworkTransform updates without physics fighting

### SCK Lifecycle on Network-Spawned Objects:
1. `Instantiate()` → `Awake()` (GameAgent registers with GameAgentManager)
2. `Fusion.Spawned()` → our code runs (DisableLocalInput, kinematic fix, camera setup)
3. Next frame → `Start()` → `Engines.Start()` → `SetEngineActivation(true)` if `activateEnginesAtStart`
4. `FixedUpdateNetwork()` begins being called each network tick

This lifecycle means `Engines.Start()` activates engines BEFORE our first `FixedUpdateNetwork`. So `!engines.EnginesActivated` is always false in our code, which is why `ControlsDisabled = false` must be set unconditionally.

---

## NEXT STEPS (Recommended)

1. **Rebuild and test**: All code fixes are applied. Rebuild the game and test with W pressed on client.
2. **If ship still doesn't move on host**: Make a **Development Build** to see host-side FixedUpdateNetwork logs. Check if:
   - `GetInput<PlayerInputData>()` returns valid data with non-zero movement
   - The debug log shows `movement=(0.00, 0.00, 1.00)` when W is pressed
   - `enginesActive=True, controlsDisabled=False` in the periodic log
3. **If ship moves**: Success! Clean up debug logging and prepare for two-city multiplayer test.
4. **Two-city test checklist**:
   - Same Photon AppID on both machines
   - Same session name "GV_Race"
   - Same region "us"
   - One person hosts (Start Host), other joins (Start Client)
   - Photon Fusion 2 handles NAT traversal/relay automatically
5. **Eventually remove**: All debug logging once networking is confirmed stable.

---

## TESTING SETUP
- **Host**: Built game (non-development build, so no console logs visible)
- **Client**: Unity Editor (can see logs in Console)
- **How to test**: Host starts game first, Client joins via editor Play button
- **For better debugging**: Make a Development Build so host-side logs are visible too

---

## USER INFO
- Name: Veera
- Has a teammate in another city for multiplayer testing
- Using Unity Editor as client, built game as host
- `enableAutoForward` is OFF by default (intentional game design choice — player must press W to move)

---

## FILE LOCATIONS FOR SCK SOURCE (Read-only reference)
- `Assets/SpaceCombatKit/VSXPackageLibrary/Packages/Engines3DSystem/Runtime/VehicleEngines3D.cs`
- `Assets/SpaceCombatKit/VSXPackageLibrary/Packages/Engines3DSystem/Runtime/Engines.cs`
- `Assets/SpaceCombatKit/VSXPackageLibrary/Packages/VehiclesSystem/GameAgents/GameAgent.cs`
- `Assets/SpaceCombatKit/VSXPackageLibrary/Packages/VehiclesSystem/GameAgents/GameAgentManager.cs`
- `Assets/SpaceCombatKit/VSXPackageLibrary/Packages/VehiclesSystem/Vehicles/Vehicle.cs`
