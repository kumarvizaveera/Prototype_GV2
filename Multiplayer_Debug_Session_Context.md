# Multiplayer Debug Session - Full Context Transfer
## Unity Space Combat Kit + Photon Fusion 2
### Date: February 15-16, 2026

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
// [Networked] properties for manual position sync
[Networked] private Vector3 SyncPosition { get; set; }
[Networked] private Quaternion SyncRotation { get; set; }
[Networked] private int SyncTick { get; set; } // Diagnostic tick counter

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
    // 1. Disable assigned localInputScripts array (SKIPS self — NetworkedSpaceshipBridge)
    // 2. Disable all VehicleInput components (namespace VSX.Vehicles)
    // 3. Disable all GeneralInput components (namespace VSX.Controls)
    // 4. Disable Unity PlayerInput components
    // 5. DESTROY SetStartAtCheckpoint on remote ships (fights NetworkTransform)
    // 6. Unregister GameAgent from GameAgentManager, call EnterVehicle(vehicle) to activate modules, Destroy(ga)
    // 7. Re-disable input scripts that EnterVehicle may have reactivated
    // 8. SAFETY: Re-enable self if something disabled the bridge
}

private void Update()
{
    // PROXY DIAGNOSTIC: Logs position every 3s for proxy ships (host's ship on client)
    //   - FixedUpdateNetwork does NOT run on proxies in Fusion 2
    //   - Logs: pos, syncPos, syncTick, rot
    // CAMERA RETRY: For local player if camera setup failed in Spawned()
}

public override void FixedUpdateNetwork()
{
    // FIRST TICK one-time debug log (unconditional)
    // CheckAuthorityAndSetupInput()

    // MANUAL POSITION SYNC: Write Rigidbody position (not root transform!) every tick
    //   - Uses rb.position/rb.rotation (child object with physics)
    //   - Root transform stays at spawn; physics moves the CHILD
    //   - Also increments SyncTick for diagnostic

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

private void LateUpdate()
{
    // PROXY POSITION APPLY: For proxy ships (host's ship on client)
    //   - Reads [Networked] SyncPosition/SyncRotation
    //   - Applies to the CHILD Rigidbody transform (not root)
    //   - Matches host's physics hierarchy where child moves, root stays at spawn
}

public override void Render()
{
    // Visual feedback placeholder (boost effects, engine sounds)
}
```

### 2. NetworkManager.cs (NOT MODIFIED - Read only)
**Path**: `Assets/GV/Scripts/Network/NetworkManager.cs`
- Handles Photon Fusion connection, player spawning in OnPlayerJoined
- Spawns with `runner.Spawn(playerPrefab, spawnPos, spawnRot, inputAuthority: player)`
- Camera setup via `SetupCameraFollow()` for local player on host
- `autoHostInBuild = true` — build auto-hosts, editor must press J to join

### 3. NetworkedPlayerInput.cs (MODIFIED)
**Path**: `Assets/GV/Scripts/Network/NetworkedPlayerInput.cs`
- Collects keyboard/mouse input in Update(), sends via OnInput() callback
- `enableAutoForward` field — **code default changed to `false`** (was `true`)
- When autoForward is off: W = forward, S = reverse (no automatic throttle)
- When autoForward is off: Shift = boost (instead of W)
- **Mouse steering**: Screen-position mode matching SCK's ScreenPosition behavior
  - Mouse position relative to screen center → pitch (vertical) and yaw (horizontal)
  - Dead zone (0.1) and max distance (0.475) matching SCK defaults
  - Only active when mouse is inside game window AND Application.isFocused
  - Keyboard Q/E for roll, arrow keys as fallback when mouse is centered/outside window
- Input struct: `PlayerInputData` with steering (Vector3), movement (Vector3), boost (NetworkBool), buttons (NetworkButtons)

### 4. Player_ForMultiplayer.prefab
**Path**: Prefab used for network spawning
- Has: NetworkObject, NetworkTransform, Rigidbody, GameAgent (isPlayer=true, startingVehicle=null), VehicleEngines3D, various VehicleInput scripts, SetStartAtCheckpoint
- Does NOT have: GameAgentManager (scene singleton), NetworkRigidbody3D
- 4 inactive GameObjects (UI elements only)
- **CRITICAL HIERARCHY**: Root (Player_ForMultiplayer) → Child (SpaceFighter_Light has Rigidbody + VehicleEngines3D). Physics moves the CHILD, not the root. NetworkTransform on root is useless for syncing actual ship position.

### 5. SetStartAtCheckpoint.cs (NOT MODIFIED - Read only, destroyed on remote ships)
**Path**: `Assets/GV/Scripts/SetStartAtCheckpoint.cs`
- Coroutine in Start() that teleports object to a checkpoint position
- Waits for CheckpointNetwork singleton, then sets transform.position
- Resets Rigidbody velocity and does Sleep/WakeUp
- **On remote ships**: Destroyed in DisableLocalInput() to prevent fighting NetworkTransform

---

## ALL BUGS FOUND AND FIXED (Chronological)

### Bug 1: Both ships moving identically (FIXED - Session 1)
**Cause**: Native SCK input scripts (VehicleInput, GeneralInput, PlayerInput_InputSystem) were active on ALL ships, all reading the same keyboard.
**Fix**: Comprehensive `DisableLocalInput()` in `Spawned()` that disables VehicleInput (VSX.Vehicles), GeneralInput (VSX.Controls), and Unity PlayerInput components on remote ships. Moved from FixedUpdateNetwork to Spawned() for immediate effect.

### Bug 2: Wrong namespace for GeneralInput (FIXED - Session 1)
**Cause**: Used `VSX.GeneralInput` but actual namespace is `VSX.Controls.GeneralInput`.
**Fix**: Added `using VSX.Controls;`

### Bug 3: Weapons not firing on client (FIXED - Session 1)
**Cause**: `FixedUpdateNetwork()` had early return at `if (!Object.HasStateAuthority) return;`, so client never processed weapon input.
**Fix**: Restructured FixedUpdateNetwork into separate MOVEMENT section (host-only for remote) and WEAPON section (both host and client). Added `ApplyWeaponInput()` method. Skipped host's own ship to avoid double-firing with native SCK scripts.

### Bug 4: Ship invisible on other screen (FIXED - Session 1)
**Cause**: SCK's ModuleManagers (which control renderers, weapons, engines) only activate via `Vehicle.OnEntered()`. The prefab's GameAgent has `startingVehicle = null`, so EnterVehicle never runs on remote ships.
**Fix**: In DisableLocalInput(): unregister GameAgent from GameAgentManager singleton, manually call `ga.EnterVehicle(vehicle)` to activate ModuleManagers, then `Destroy(ga)`. Then re-disable input scripts that EnterVehicle may have reactivated.

### Bug 5: Host ship hovering/spinning when GameAgent left enabled (FIXED - Session 1)
**Cause**: Remote ship's GameAgent registering with the scene-level GameAgentManager singleton caused interference (focus switching, camera conflicts).
**Fix**: Part of Bug 4 fix — unregister + EnterVehicle + Destroy pattern.

### Bug 6: Ships stuck at spawn / position not syncing (FIXED - Session 1)
**Cause**: Prefab has Rigidbody + NetworkTransform but NO NetworkRigidbody3D. On client, local Rigidbody physics fights NetworkTransform position updates.
**Fix**: Make Rigidbody kinematic when `!HasStateAuthority` in Spawned().

### Bug 7: Engines not producing force on remote ships (FIXED - Session 1)
**Cause**: `VehicleEngines3D.FixedUpdate()` gates all force application behind `if (enginesActivated)` which defaults to false. Normally activated via `Engines.Start()` with `activateEnginesAtStart`, but the network flow bypasses this.
**Fix**: Added force-activation in FixedUpdateNetwork when processing remote ships: `engines.SetEngineActivation(true)` and `engines.ControlsDisabled = false`.

### Bug 8: ControlsDisabled silently rejecting all input (FIXED - Session 1)
**Cause**: SCK's `SetMovementInputs()`, `SetSteeringInputs()`, and `SetBoostInputs()` all have `if (controlsDisabled) return;` at the start — they silently reject ALL input when `controlsDisabled` is true. In our code, `engines.ControlsDisabled = false` was INSIDE the `if (!engines.EnginesActivated)` block. But `Engines.Start()` already activates engines via `activateEnginesAtStart = true` (which runs BEFORE our first FixedUpdateNetwork). So our condition `!engines.EnginesActivated` was always false, and `ControlsDisabled = false` **never executed**.
**Fix**: Moved `engines.ControlsDisabled = false` OUTSIDE the activation check — now runs EVERY tick before setting inputs.

### Bug 9: enableAutoForward code default didn't match user preference (FIXED - Session 1)
**Cause**: Code default was `enableAutoForward = true` but user wanted it `false` by default.
**Fix**: Changed serialized field default to `false` in NetworkedPlayerInput.cs.

### Bug 10: Mouse steering not sent over network (FIXED - Session 2)
**Cause**: `CollectInput()` in NetworkedPlayerInput.cs had an EMPTY mouse section — just a comment placeholder. Only keyboard arrow keys set the steering values. Mouse pitch/yaw was never collected or sent.
**Fix**: Implemented SCK's ScreenPosition steering mode in CollectInput(). Mouse position relative to screen center is converted to pitch/yaw with dead zone (0.1) and max distance (0.475). Includes bounds checking: only processes mouse when `Application.isFocused` and mouse is inside the game window (prevents random spinning when mouse is over Unity Editor panels). Q/E for roll, arrow keys as fallback.

### Bug 11: Mouse steering causing random spinning on host (FIXED - Session 2)
**Cause**: Initial mouse steering implementation read `Mouse.current.position` without checking if the mouse was inside the game window. In the Unity Editor, the mouse can be over the Console/Inspector, producing coordinates outside the game view and wild steering values.
**Fix**: Added `Application.isFocused` check, `Screen.width > 100` sanity check, and bounds validation (`mousePos.x >= 0 && mousePos.x <= Screen.width` etc.). Mouse steering only activates when all checks pass; otherwise falls back to keyboard arrows.

### Bug 12: Host ship not syncing position to client (FIXED - Session 2)
**Root Cause Discovery Process** (multi-step investigation):
1. **Initial symptom**: Host ship visible on client but stuck at spawn/checkpoint position
2. **Investigation**: Only 1 FIRST TICK log appeared — host's ship never ran FixedUpdateNetwork on client
3. **Key discovery**: In Fusion 2, **FixedUpdateNetwork does NOT run on proxy objects** (no state or input authority). The host's ship on the client is a pure proxy. This is expected Fusion behavior, not a bug.
4. **Added Update()-based diagnostics** since FUN doesn't run on proxies
5. **Tried [Networked] properties** for manual position sync — SyncPosition stayed stuck
6. **Added SyncTick counter** — tick counter DID increase, proving [Networked] sync works, but SyncPosition value never changed
7. **ROOT CAUSE FOUND**: SCK's prefab hierarchy has Rigidbody+VehicleEngines3D on a **CHILD** object (SpaceFighter_Light), not the root. Physics moves the child via AddRelativeForce, but `transform.position` on the root (where our bridge reads it) stays at spawn forever. The camera follows the child, so the host sees movement, but the root transform is stuck.

**Fix**:
- **Host side (FixedUpdateNetwork)**: Write `rb.position`/`rb.rotation` (the child Rigidbody's position) instead of `transform.position` (the stuck root)
- **Client side (LateUpdate)**: Apply `SyncPosition`/`SyncRotation` to `proxyRb.transform` (the child with the Rigidbody), not the root transform
- **Also**: Destroy SetStartAtCheckpoint on remote ships (its Start() coroutine teleports to checkpoint, fighting any position sync)
- **Also**: Self-protection in DisableLocalInput() — skips disabling NetworkedSpaceshipBridge if accidentally in localInputScripts array, and re-enables self at the end if something disabled it

---

## CRITICAL DISCOVERIES

### SCK Gotcha #1: controlsDisabled
SCK's engine input methods (`SetMovementInputs`, `SetSteeringInputs`, `SetBoostInputs`) all silently return if `controlsDisabled` is true. There is NO warning, no log, nothing. The input just gets swallowed. Always ensure `ControlsDisabled = false` before calling any input setter, and do it EVERY tick because other SCK systems could re-enable it.

### SCK Gotcha #2: Prefab Hierarchy — Physics on Child, Not Root
**The most important architectural discovery.** In the Player_ForMultiplayer prefab:
- **Root**: `Player_ForMultiplayer` — has NetworkObject, NetworkTransform, NetworkedSpaceshipBridge
- **Child**: `SpaceFighter_Light` — has Rigidbody, VehicleEngines3D, weapons, visuals

Physics (AddRelativeForce/Torque) moves the **child**. The root transform NEVER moves. The camera follows the child, so locally the ship appears to move. But any code reading `transform.position` on the root (including NetworkTransform) gets the spawn position forever.

**Rule**: Always use `GetComponentInChildren<Rigidbody>()` and read `rb.position`/`rb.rotation` for the actual ship position.

### Fusion 2 Gotcha: FixedUpdateNetwork Doesn't Run on Proxies
In Fusion 2 Host mode, `FixedUpdateNetwork()` only runs on:
- **State Authority objects** (host has this for all objects)
- **Input Authority objects** (client's own ship for prediction)

It does NOT run on **proxy objects** (host's ship on client — no state or input authority). Use `Update()`, `LateUpdate()`, or `Render()` for proxy-side logic. `[Networked]` properties and `Render()` still work on proxies for reading synced state.

### Fusion 2: Rebuild Prefab Table After Adding [Networked] Properties
When adding new `[Networked]` properties to a NetworkBehaviour, you MUST go to **Fusion > Rebuild Prefab Table** in the Unity Editor. This rebakes the NetworkObject state layout to include the new properties. Without this, [Networked] properties may not sync properly.

---

## CURRENT STATUS (End of Session 2)

### What's working:
- Individual ship movement (input isolation per ship)
- Client's ship moves on host screen (WASD + mouse steering synced over network)
- Mouse steering works via network (screen-position mode, with bounds checking)
- Host's ship position syncs to client via manual [Networked] property sync
- Weapon firing on client side via networked input
- Ship visibility on both host and client (GameAgent → EnterVehicle → Destroy pattern)
- Rigidbody/NetworkTransform conflict resolved (kinematic on non-authority)
- Camera follows local player on both host and client
- Engine activation on remote ships (force-activate in FixedUpdateNetwork)
- ControlsDisabled fix — set to false every tick
- Ships can be controlled individually and separately on host and client

### Known remaining issues (for next session):
- User mentioned "other problems" after confirming individual ship control works — details TBD
- Host ship position sync may be slightly jerky (no interpolation on [Networked] properties yet)
- Connection timeout on first J press in editor (works on second attempt) — intermittent Photon issue
- Debug logging is extensive — should be cleaned up once stable
- Development build recommended for host-side debugging

### Debug logs currently active:
- `FIRST TICK` — one-time per ship on FUN start
- `PROXY FIRST UPDATE` — one-time for proxy ships in Update()
- `PROXY POS` — every 3s for proxy ships (pos, syncPos, syncTick)
- `PROXY LATE UPDATE ACTIVE` — one-time confirming LateUpdate works on proxy
- `HOST driving remote ship` — every 2s on host for client's ship
- `Received Input from Client` — every 2s showing input data with steering
- Various one-time logs in Spawned() and DisableLocalInput()

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
- SCK Mouse Steering: `PlayerInput_Base_SpaceshipControls.MouseSteeringUpdate()` — ScreenPosition mode uses viewport coords, dead radius 0.1, max distance 0.475, animation curve mapping

### Module System:
ModuleManagers are children of the Vehicle that control subsystems (weapons, engines, visuals). They only activate when `Vehicle.OnEntered()` is called. This is why simply enabling renderers wasn't enough — the entire module system needs to be activated.

### Network Flow (How input gets from client to ship movement):
1. Client: `NetworkedPlayerInput.CollectInput()` reads keyboard + mouse → fills `PlayerInputData` struct
2. Client: `NetworkedPlayerInput.OnInput()` sends struct to Fusion
3. Host: `NetworkedSpaceshipBridge.FixedUpdateNetwork()` calls `GetInput<PlayerInputData>()`
4. Host: Writes `SyncPosition = rb.position` and `SyncRotation = rb.rotation` for ALL ships (manual sync)
5. Host: If HasStateAuthority && !HasInputAuthority (remote ship):
   a. Force-activate engines if needed
   b. Set `ControlsDisabled = false` (EVERY TICK)
   c. Apply steering/movement/boost to SCK engines
6. Host: `VehicleEngines3D.FixedUpdate()` applies Rigidbody forces on CHILD object (IF enginesActivated AND controlsDisabled is false)
7. Client: `LateUpdate()` reads [Networked] SyncPosition/SyncRotation and applies to child Rigidbody transform
8. Client: Rigidbody is kinematic, so it accepts position updates without physics fighting

### SCK Lifecycle on Network-Spawned Objects:
1. `Instantiate()` → `Awake()` (GameAgent registers with GameAgentManager)
2. `Start()` → `SetStartAtCheckpoint` coroutine may teleport to checkpoint, `Engines.Start()` → `SetEngineActivation(true)` if `activateEnginesAtStart`
3. `Fusion.Spawned()` → our code runs (DisableLocalInput, kinematic fix, camera setup, Destroy SetStartAtCheckpoint on remotes)
4. `FixedUpdateNetwork()` begins being called each network tick (NOT on proxies)
5. `LateUpdate()` runs on proxies, applying synced position to child transform

---

## NEXT STEPS (Recommended)

1. **Address remaining issues** reported by Veera (details TBD in next session)
2. **Add interpolation** to manual position sync (currently snaps to latest server value, may be jerky)
3. **Make a Development Build** for host-side debugging (currently blind to host logs)
4. **Clean up debug logging** once networking is confirmed stable
5. **Two-city test checklist**:
   - Same Photon AppID on both machines
   - Same session name "GV_Race"
   - Same region "us"
   - One person hosts (Start Host), other joins (Start Client)
   - Photon Fusion 2 handles NAT traversal/relay automatically
6. **Consider adding NetworkRigidbody3D** to the prefab as a proper long-term fix instead of manual [Networked] position sync
7. **Investigate connection timeout** on first J press (intermittent)

---

## TESTING SETUP
- **Host**: Built game (non-development build, so no console logs visible)
- **Client**: Unity Editor (can see logs in Console)
- **How to test**: Host starts game first (auto-hosts in build), Client presses J in editor to join
- **For better debugging**: Make a Development Build so host-side logs are visible too
- **Note**: First J press may timeout; second attempt usually works

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
- `Assets/SpaceCombatKit/SpaceCombatKit/Scripts/Input/InputSystem/PlayerInput_InputSystem_SpaceshipControls.cs`
- `Assets/SpaceCombatKit/SpaceCombatKit/Scripts/Input/PlayerInput_Base_SpaceshipControls.cs`
