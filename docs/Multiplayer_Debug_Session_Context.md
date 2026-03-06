# Multiplayer Debug Session - Full Context Transfer
## Unity Space Combat Kit + Photon Fusion 2
### Date: February 15-19, 2026

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
    // If remote (host processing client's ship): DisableLocalInput()
    // If client's own ship: DisableClientOwnShipScripts() — makes it a pure visual shell
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

private void DisableClientOwnShipScripts()  // NEW IN SESSION 3
{
    // Client's own ship is a "pure visual shell" — position comes entirely from host sync
    // 1. Disable ALL VehicleInput-derived components
    // 2. Disable ALL GeneralInput components
    // 3. Disable Unity PlayerInput (New Input System)
    // 4. Disable VehicleEngines3D
    // 5. Destroy SetStartAtCheckpoint
    // 6. Unregister GameAgent (disable, don't destroy)
    // SAFETY: Re-enable self if disabled
}

private void Update()
{
    // PROXY DIAGNOSTIC: Logs position every 3s for proxy ships (host's ship on client)
    //   - FixedUpdateNetwork does NOT run on proxies in Fusion 2
    //   - Logs: pos, syncPos, syncTick, rot
    // CAMERA RETRY: For local player if camera setup failed in Spawned()
    //   - Runs repeatedly for 5 seconds (30 checks at 0.2s intervals) — Session 3 improvement
    //   - Uses VehicleCamera.TargetVehicle (not .Vehicle) and SetVehicle()
}

public override void FixedUpdateNetwork()
{
    // FIRST TICK one-time debug log (unconditional)
    // CheckAuthorityAndSetupInput()

    // CONTINUOUS VEHICLEINPUT ENFORCEMENT (Session 3):
    //   - On host for client's ship: force-disable any VehicleInput that reactivated
    //   - SCK's EnterVehicle() and callbacks can re-enable VehicleInput scripts

    // MANUAL POSITION SYNC: Write Rigidbody position (not root transform!) every tick
    //   - Uses rb.position/rb.rotation (child object with physics)
    //   - Root transform stays at spawn; physics moves the CHILD
    //   - Also increments SyncTick for diagnostic

    // engines null check with warning

    // INPUT ROUTING (Session 3 - reworked):
    //   - For CLIENT'S SHIP ON HOST (HasStateAuthority && !HasInputAuthority):
    //       Skip GetInput entirely — it returns HOST's input, not client's!
    //       Use RPC input first, then raw data fallback
    //   - For HOST'S OWN SHIP:
    //       GetInput works correctly — use normally

    // MOVEMENT: Only if HasStateAuthority && !HasInputAuthority (host processing remote ship)
    //   - Force-activate engines if not activated
    //   - ALWAYS set engines.ControlsDisabled = false (EVERY TICK - critical fix)
    //   - SetSteeringInputs, SetMovementInputs, SetBoostInputs
    //   - Debug log every 2 seconds (throttled)

    // WEAPONS: if !isHostLocalShip && (HasStateAuthority || HasInputAuthority)
    //   - ApplyWeaponInput(data) - handles primary, secondary, missile fire
}

private void FixedUpdate()  // Session 5 — position follow for non-authority ships
{
    // Runs at [DefaultExecutionOrder(-200)] — BEFORE camera's FixedUpdate(0)
    // For !HasStateAuthority ships with SyncTick > 0:
    //   1. CheckNewTick() — detects new Fusion ticks, handles first-tick snap
    //   2. ApplyPositionFollow() — SmoothDamp toward SyncPosition + direct transform writes
    //   3. Jitter diagnostic logging (first 5 seconds)
    // CRITICAL: Uses direct transform writes (NOT MovePosition) with rb.interpolation = None
    //   - Camera reads transform.position in FixedUpdate — must match rendered position
    //   - MovePosition + rb.Interpolate caused camera-ship desync (see Bug 20)
}

private void LateUpdate()
{
    // Camera verification (client's own ship): re-locks VehicleCamera every 0.2s for 30 checks
    // One-time diagnostic for non-authority ships
    // NO position writes here — all position follow is in FixedUpdate
}

public override void Render()
{
    // Visual feedback placeholder (boost effects, engine sounds)
}
```

### 2. NetworkManager.cs (MODIFIED - Session 3)
**Path**: `Assets/GV/Scripts/Network/NetworkManager.cs`
- Handles Photon Fusion connection, player spawning in OnPlayerJoined
- Spawns with `runner.Spawn(playerPrefab, spawnPos, spawnRot, inputAuthority: player)`
- Camera setup via `SetupCameraFollow()` for local player on host
- `autoHostInBuild = true` — build auto-hosts, editor must press J to join
- **Session 3 changes:**
  - Per-player magic number encoding in OnInput: `data.magicNumber = Runner.LocalPlayer.PlayerId * 1000 + 42` (host=1042, client=2042)
  - Same encoding in raw data send block
  - Updated validation: `magicNumber % 1000 == 42`
  - `Screen.SetResolution(1920, 1080, FullScreenMode.FullScreenWindow)` in StartGame() for standalone builds
  - Exposes `RawSendCount`, `RawSendErrorCount`, `RawSendBlockReason`, `RawSendError`, `RawSendErrorStack` for diagnostic display

### 3. NetworkedPlayerInput.cs (HEAVILY MODIFIED - Session 3)
**Path**: `Assets/GV/Scripts/Network/NetworkedPlayerInput.cs`
- Collects keyboard/mouse input in Update(), provides via `CurrentInputData` property
- `enableAutoForward` field — **code default changed to `false`** (was `true`)
- When autoForward is off: W = forward, S = reverse (no automatic throttle)
- When autoForward is off: Shift = boost (instead of W)
- **Session 3 major rewrite — Mouse steering changed from absolute position to delta-based accumulated reticle:**
  - SCK locks cursor (`CursorLockMode.Locked`) — `Mouse.current.position` is always screen center, useless
  - Now reads `Mouse.current.delta.ReadValue()` (pixels moved per frame)
  - Accumulates delta into virtual reticle position (viewport coords, 0.5=center)
  - Auto-centering: reticle drifts back to center when mouse stops moving (`Lerp` at `centerSpeed = 2.0f * Time.deltaTime`)
  - Clamp with aspect ratio correction (`maxReticleDistance = 0.4f`)
  - Steer computed from reticle offset: pitch = -Y, yaw = X (SCK convention)
  - Dead radius: 0.02f (reduced from original 0.1)
  - `Application.isFocused` gate — prevents reading mouse when window unfocused (critical for same-machine testing)
  - Unfocused: reticle resets to center (no steer)
  - Keyboard Q/E for roll, arrow keys as fallback when mouse not providing steering
- **Session 3 input architecture change:**
  - No longer registered via `AddCallbacks` — OnInput is vestigial
  - `CurrentInputData` property exposes latest input data
  - `NotifyInputConsumed()` called by NetworkManager after `input.Set()` to reset one-shot buttons
- **OnGUI diagnostic display** (non-editor builds only):
  - Application.isFocused (color-coded green/red)
  - Mouse delta and reticle position
  - Steer values (color-coded: green=non-zero, yellow=zero)
  - Magic number
  - Runner state (IsClient, IsServer, LocalPlayer)
  - Raw send diagnostics from NetworkManager

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

### 6. NetworkProjectConfig.fusion (MODIFIED - Session 2)
**Path**: `Assets/Photon/Fusion/Resources/NetworkProjectConfig.fusion`
- **InputDataWordCount changed from 0 to 16** — Root cause of early "InputDataWordCount: 0" bug. Without this, Fusion silently drops all player input.

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

### Bug 13: CS1061 — VehicleCamera.Vehicle doesn't exist (FIXED - Session 3)
**Cause**: Camera setup code referenced `VehicleCamera.Vehicle` property which doesn't exist.
**Fix**: Changed to `VehicleCamera.TargetVehicle` (the correct property name). Uses `SetVehicle()` method to assign.

### Bug 14: Client screen violent shaking (FIXED - Session 3)
**Cause**: On CLIENT's own ship, ALL SCK scripts were still running (VehicleInput, VehicleEngines3D, GameAgent, etc.) because `DisableLocalInput()` was intentionally skipped for the local player's ship. These scripts fought with LateUpdate position sync, causing violent jitter.
**Fix**: New `DisableClientOwnShipScripts()` method makes client's own ship a "pure visual shell" — disables VehicleInput, GeneralInput, PlayerInput, VehicleEngines3D, destroys SetStartAtCheckpoint, and unregisters GameAgent. Position/rotation come entirely from host via [Networked] SyncPosition/SyncRotation.

### Bug 15: Client ship mirrors host's mouse steer on host screen (FIXED - Session 3)
**Cause**: Three layers:
1. **GetInput() returns HOST's input for ALL ships** in Fusion 2 Host Mode. Even when calling `GetInput()` for the client's ship, it returns the host's locally-collected `PlayerInputData` — NOT the client's.
2. **Same-machine shared mouse**: Both apps (editor + build) on same PC share `Mouse.current.position`, so the host's mouse position was being sent to both ships.
3. **VehicleInput reactivation**: SCK's `EnterVehicle()` and callbacks can re-enable VehicleInput scripts after initial disable.

**Fix**:
- Skip `GetInput()` entirely for client's ship — use RPC/raw data input only
- `Application.isFocused` gate prevents unfocused app from reading the other app's mouse
- Continuous VehicleInput enforcement in FixedUpdateNetwork (re-disable every tick)
- Per-player magic number (`playerID * 1000 + 42`) to distinguish host (1042) vs client (2042) input

### Bug 16: Client mouse not responding after focus gate fix (FIXED - Session 3)
**Cause**: After adding `Application.isFocused` check, client mouse input stopped working even when focused. Initial fix attempt added a "mouse-in-bounds fallback" that bypassed the focus check — but this defeated the focus gate and re-introduced mirroring.
**Fix**: Removed the mouse-in-bounds fallback. The real problem was Bug 17 (cursor locked).

### Bug 17: Mouse coordinates stuck at screen center — cursor locked (FIXED - Session 3)
**Cause**: SCK's `GameStateManager` sets `Cursor.lockState = CursorLockMode.Locked` (at lines 191/213 of GameStateManager.cs). This locks/hides the cursor at screen center. `Mouse.current.position.ReadValue()` always returns ~(960, 540) on 1920×1080 — making absolute position-based steering completely useless. The deadzone check always saw near-zero offset.
**Fix**: Complete rewrite of mouse steering from absolute position to **delta-based accumulated reticle** (matching SCK's actual input approach):
- Read `Mouse.current.delta.ReadValue()` instead of `.position`
- Accumulate delta into virtual reticle position (viewport coords)
- Reticle auto-centers when mouse stops
- Steer computed from reticle offset from center
- This approach works regardless of cursor lock state

### Bug 18: OnGUI deadzone display mismatch (FIXED - Session 3)
**Cause**: OnGUI checked `mag < 0.1f` for deadzone display but actual code used 0.02f after adjustment.
**Fix**: Updated OnGUI threshold to match actual code.

### Bug 19: Build resolution / UI cutoff (PARTIAL - Session 3)
**Symptom**: Standalone build UI elements cut off at corners, not rendering at expected resolution.
**Attempted fix**: Added `Screen.SetResolution(1920, 1080, FullScreenMode.FullScreenWindow)` in NetworkManager.StartGame(). Also tried Unity Project Settings. Did not fully resolve — user moved on since it wasn't the core issue. May need further investigation.

### Bug 20: Client ship jitter/wobble/shake — ship shakes against smooth background (IN PROGRESS - Sessions 4-5)
**Symptom**: On the client screen, the client's own ship visibly shakes/wobbles/jitters. The background and camera are SMOOTH — only the ship model itself shakes relative to the camera. The host's ship is smooth. This started after Fix 1 (mouse steering) and Fix 2 (position sync for client's own ship) from Session 4.

**Root cause analysis**: The SCK camera reads `currentViewTarget.transform.position` in its FixedUpdate (confirmed by reading SpaceFighterCameraController source). With `rb.interpolation = Interpolate`, Unity overrides the ship's RENDERED `transform.position` to an interpolated value between consecutive physics positions. But the camera reads the NON-interpolated physics position. This creates a VARYING offset between where the camera positions itself and where the ship actually renders. The offset varies because SmoothDamp produces varying step sizes (Fusion 64Hz ticks vs Unity 50Hz FixedUpdate — some FixedUpdates see 0 ticks, some 1, some 2). Result: ship shakes against smooth camera background.

**Approaches tried (chronological):**
1. **Double-buffer interpolation** (Session 4): Failed — Fusion 64Hz > Unity 50Hz meant t jumped 0→1 in one step
2. **MoveTowards** (Session 4): Failed — stop-start oscillation (speed estimate lags → catches up → stops → repeats)
3. **SmoothDamp + direct transform writes** (Session 5): SmoothDamp data was smooth but 50Hz FixedUpdate rate caused visual stutter
4. **SmoothDamp + MovePosition + rb.Interpolate** (Session 5): Fixed 50Hz stutter but introduced camera-ship desync — the root cause of the visible shake
5. **SmoothDamp + direct transform writes + rb.interpolation = None** (Session 5 — CURRENT): Eliminates camera-ship desync. Camera and rendered ship agree on exact same position. Some residual jitter still reported.

**Current fix (in code):**
- `rb.interpolation = RigidbodyInterpolation.None` (was Interpolate) in Spawned()
- Direct `_cachedTarget.position = _intendedPosition` writes (was `_cachedRb.MovePosition()`)
- SmoothDamp still used for smoothing (positionSmoothTime=0.1, rotationSmoothSpeed=10)
- `[DefaultExecutionOrder(-200)]` ensures our FixedUpdate runs before camera's

**Still has some residual jitter — needs further investigation. Possible causes:**
- SmoothDamp itself producing variable velocities from 64Hz/50Hz tick mismatch
- May need to move position following to Render() (display rate) instead of FixedUpdate (50Hz)
- May need to investigate if any other SCK script is modifying the ship transform
- The client ship also feels "not responsive" — may be related to SmoothDamp lag (0.1s smoothTime)

### Bug 20 sub-fixes applied in Session 4 (before Session 5 continued):
- **Fix 1 — Mouse steering** (`NetworkedPlayerInput.cs`): Switched to `Mouse.current.position.ReadValue()` (same API as SCK's native `GetMouseViewportPosition()`). The previous `Input.mousePosition` (legacy API) returns `Vector3.zero` when only New Input System is active. Also removed strict `mouseInsideWindow` bounds check, replaced with simple clamping.
- **Fix 2 — Position mismatch** (`NetworkedSpaceshipBridge.cs`): Changed LateUpdate from `!HasStateAuthority && !HasInputAuthority` (proxies only) to `!HasStateAuthority` (all non-host ships). This enabled the client's own ship to receive SyncPosition updates.
- **Fix 3 — HUD cursor auto-centering removed** (`NetworkedPlayerInput.cs`): Removed `Lerp` auto-centering that was pulling cursor back to center. Updated `maxReticleDistance` from 0.4 to 0.475.
- **19 additional bug fixes** applied in Session 4 (various networking, input, camera issues).

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

### SCK Gotcha #3: CursorLockMode.Locked (Session 3)
SCK's `GameStateManager` sets `Cursor.lockState = CursorLockMode.Locked` during gameplay. This:
- Hides and pins the cursor to screen center
- Makes `Mouse.current.position.ReadValue()` useless (always returns center pixel)
- Requires using `Mouse.current.delta.ReadValue()` (movement per frame) instead

SCK's own input (in `PlayerInput_Base_SpaceshipControls`) uses mouse delta accumulated into a virtual reticle — NOT absolute mouse position. Any network input collection must match this approach.

### SCK Gotcha #4: VehicleInput Reactivation (Session 3)
SCK's `EnterVehicle()` and various callbacks can re-enable VehicleInput scripts after they've been disabled. This requires **continuous enforcement** — checking and re-disabling every tick in FixedUpdateNetwork, not just once in Spawned().

### Fusion 2 Gotcha #1: FixedUpdateNetwork Doesn't Run on Proxies
In Fusion 2 Host mode, `FixedUpdateNetwork()` only runs on:
- **State Authority objects** (host has this for all objects)
- **Input Authority objects** (client's own ship for prediction)

It does NOT run on **proxy objects** (host's ship on client — no state or input authority). Use `Update()`, `LateUpdate()`, or `Render()` for proxy-side logic. `[Networked]` properties and `Render()` still work on proxies for reading synced state.

### Fusion 2 Gotcha #2: GetInput() Returns Host's Input in Host Mode (Session 3)
**Critical discovery.** In Fusion 2 Host Mode, `GetInput<PlayerInputData>()` returns the HOST's locally-collected input for ALL ships — including the client's ship. It does NOT return the InputAuthority player's actual input data. This caused client ships to mirror host's mouse movements.

**Solution**: For the client's ship on the host, skip `GetInput()` entirely. Use RPC or raw data channel to receive the client's actual input.

### Fusion 2: Rebuild Prefab Table After Adding [Networked] Properties
When adding new `[Networked]` properties to a NetworkBehaviour, you MUST go to **Fusion > Rebuild Prefab Table** in the Unity Editor. This rebakes the NetworkObject state layout to include the new properties. Without this, [Networked] properties may not sync properly.

### SCK Gotcha #5: Camera Reads transform.position, NOT rb.position (Session 5)
The SCK camera system (`SpaceFighterCameraController.CameraControllerFixedUpdate()` and `CameraEntity.CameraControlFixedUpdate()`) reads `currentViewTarget.transform.position` — NOT `rb.position`. This is critical because `rb.interpolation = Interpolate` causes `transform.position` during rendering to differ from `transform.position` during FixedUpdate. The camera positions itself based on the FixedUpdate value, but the ship renders at the interpolated value → varying offset → visible shake. The fix is `rb.interpolation = None` on kinematic client ships so camera and rendered positions always agree.

**SpaceFighterCameraController key code:**
```csharp
// Uses transform.TransformPoint for position (NOT rb.position)
Vector3 targetPosition = cameraEntity.CurrentViewTarget.transform.TransformPoint(offset);
// Lerp with PositionFollowStrength (camera has its own smoothing)
cameraEntity.transform.position = (1 - PositionFollowStrength) * cameraEntity.transform.position + PositionFollowStrength * targetPosition;
// Slerp for rotation with RotationFollowStrength
cameraEntity.transform.rotation = Quaternion.Slerp(transform.rotation, target.transform.rotation, RotationFollowStrength);
```

### SCK Gotcha #6: PlayerInput_Base_SpaceshipControls has linkYawAndRoll (Session 5)
SCK's `PlayerInput_Base_SpaceshipControls` has two features that are DISABLED on remote/client ships:
- `linkYawAndRoll` — auto-banks the ship into turns (roll proportional to yaw input, `yawRollRatio = 0.5`)
- `auto-level` — returns ship to level flight when no steer input (`autoLevelStrength = 0.04`, `maxAutoLevelInput = 0.2`)
These are part of SCK's native input processing, not VehicleEngines3D. Since the client ship disables all VehicleInput/PlayerInput scripts, these features are lost. If banking is needed, it must be re-implemented in the bridge.

### Same-Machine Testing Artifacts (Session 3)
When running both host (build) and client (editor) on the **same PC**, several artifacts appear that do NOT exist on separate machines:
- **Shared mouse**: `Mouse.current` returns OS-level data visible to BOTH apps. Mouse position/delta from one app affects the other.
- **Application.isFocused**: Only one app can have focus at a time. The unfocused app reads stale/other-app mouse data.
- **These issues disappear on separate PCs**: Each machine has its own mouse, and `Application.isFocused` is always true.

The `Application.isFocused` gate in NetworkedPlayerInput is essential for same-machine development testing but harmless on separate PCs.

---

## CURRENT STATUS (End of Session 5)

### What's working:
- Individual ship movement (input isolation per ship)
- Client's ship moves on host screen (WASD + mouse steering synced via RPC/raw data)
- **Mouse steering works** — delta-based accumulated reticle matching SCK's approach
- Host's ship position syncs to client via manual [Networked] property sync
- **Client's own ship receives position sync** (Fix 2 from Session 4)
- **Position following via SmoothDamp in FixedUpdate** with direct transform writes
- **rb.interpolation = None** to prevent camera-ship desync
- Weapon firing on client side via networked input
- Ship visibility on both host and client (GameAgent → EnterVehicle → Destroy pattern)
- Rigidbody/NetworkTransform conflict resolved (kinematic on non-authority)
- Camera follows local player on both host and client (VehicleCamera.TargetVehicle + SetVehicle)
- Engine activation on remote ships (force-activate in FixedUpdateNetwork)
- ControlsDisabled fix — set to false every tick
- Ships can be controlled individually and separately on host and client
- **Client ship no longer mirrors host's steer** (GetInput bypass + Application.isFocused gate)
- **Per-player magic number diagnostics** (1042=host, 2042=client) for input source tracking
- **[DefaultExecutionOrder(-200)]** ensures bridge FixedUpdate runs before camera

### Known remaining issues (for next session — Bug 20 still active):
- **Client ship still has some residual jitter/shake** — reduced but not eliminated by rb.interpolation=None fix
- **Client ship feels "not responsive"** — may be SmoothDamp lag (0.1s smoothTime) or Slerp rotation dampening
- **SCK banking features missing on client** — linkYawAndRoll and auto-level are disabled with VehicleInput scripts. Ship doesn't bank into turns.
- Build resolution/UI cutoff not fully resolved
- Connection timeout on first J press in editor (works on second attempt) — intermittent Photon issue
- Debug logging is extensive — should be cleaned up once stable

### Approaches to try next for jitter:
1. **Move position follow to Render()** (Fusion callback, runs every displayed frame at 60Hz+) instead of FixedUpdate (50Hz) — eliminates FixedUpdate rate mismatch entirely
2. **Reduce positionSmoothTime** (currently 0.1) for more responsiveness, or **remove SmoothDamp entirely** and use simple exponential lerp
3. **Check for other scripts modifying the transform** — any active SCK component on the client ship that writes to transform would fight with our bridge
4. **Use Fusion's built-in snapshot interpolation** for [Networked] properties via InterpolationDataSource / GetInterpolationData()
5. **Dead reckoning with correction** — estimate velocity from network data, extrapolate between ticks, blend corrections

### Debug displays currently active:
- **OnGUI (non-editor builds)**: Application.isFocused, mouse delta/reticle, steer values, magic number, runner state, raw send diagnostics
- **Console logs**: FIRST TICK, PROXY POS (3s), HOST driving remote ship (2s), JITTER_DIAG (first 5s, every 10th FixedUpdate)

---

## SCK ARCHITECTURE NOTES (Important for understanding)

### Key SCK Classes and Namespaces:
- `VehicleEngines3D` (extends `Engines`) — applies forces via `m_rigidbody.AddRelativeForce()` and `m_rigidbody.AddRelativeTorque()` in `FixedUpdate()`, but ONLY when `enginesActivated = true`
- `Engines` base class — `enginesActivated` defaults to false, has `controlsDisabled` flag, `SetEngineActivation(bool)`, `activateEnginesAtStart` field
- **CRITICAL**: `SetMovementInputs()`, `SetSteeringInputs()`, `SetBoostInputs()` all silently return if `controlsDisabled == true`
- **CRITICAL**: `SetEngineActivation(true)` has early return if `enginesActivated` is already true: `if (setActivated == enginesActivated) return;`
- `GameAgent` — registers with GameAgentManager in Awake(), collects VehicleInput scripts, `EnterVehicle()`/`ExitAllVehicles()`, `startingVehicle` field (null on network prefab). `OnDestroy()` only unregisters from manager (does NOT exit vehicles — safe to Destroy).
- `GameAgentManager` — SCENE-LEVEL SINGLETON (not on prefab), manages focused game agent, camera, HUD
- `GameStateManager` — Sets `Cursor.lockState = CursorLockMode.Locked` during gameplay (lines 191/213)
- `Vehicle` — `OnEntered()` activates ModuleManagers, `OnExited()` deactivates them
- `VehicleCamera` — `TargetVehicle` property (public getter), `SetVehicle(Vehicle)` method
- `GeneralInput` — namespace `VSX.Controls`
- `VehicleInput` — namespace `VSX.Vehicles`, extends GeneralInput
- SCK Input Hierarchy: `GeneralInput` → `VehicleInput` → `PlayerInput_Base_SpaceshipControls` → `PlayerInput_InputSystem_SpaceshipControls`
- SCK Mouse Steering: Uses mouse DELTA accumulated into `reticleViewportPosition`, NOT absolute mouse position. Works with CursorLockMode.Locked.

### Module System:
ModuleManagers are children of the Vehicle that control subsystems (weapons, engines, visuals). They only activate when `Vehicle.OnEntered()` is called. This is why simply enabling renderers wasn't enough — the entire module system needs to be activated.

### Network Flow (How input gets from client to ship movement):
1. Client: `NetworkedPlayerInput.CollectInput()` reads keyboard + mouse delta → fills `PlayerInputData` struct
2. Client: `NetworkManager.OnInput()` reads `CurrentInputData` from NetworkedPlayerInput, calls `input.Set()`, then `NotifyInputConsumed()`
3. Client: Also sends input via RPC and/or raw data channel (redundant paths for reliability)
4. Host: `NetworkedSpaceshipBridge.FixedUpdateNetwork()` — for client's ship, **skips GetInput**, uses RPC/raw data instead
5. Host: Writes `SyncPosition = rb.position` and `SyncRotation = rb.rotation` for ALL ships (manual sync)
6. Host: If HasStateAuthority && !HasInputAuthority (remote ship):
   a. Force-disable any reactivated VehicleInput (continuous enforcement)
   b. Force-activate engines if needed
   c. Set `ControlsDisabled = false` (EVERY TICK)
   d. Apply steering/movement/boost to SCK engines
7. Host: `VehicleEngines3D.FixedUpdate()` applies Rigidbody forces on CHILD object (IF enginesActivated AND controlsDisabled is false)
8. Client: `FixedUpdate()` [execution order -200] reads [Networked] SyncPosition/SyncRotation:
   a. `CheckNewTick()` — detects new Fusion ticks, first-tick initialization snap
   b. `ApplyPositionFollow()` — SmoothDamp toward SyncPosition (smoothTime=0.1), Slerp rotation (speed=10)
   c. Direct `_cachedTarget.position = _intendedPosition` writes (NOT MovePosition)
   d. `rb.interpolation = None` — camera and rendered position always agree
9. Camera FixedUpdate(0): SCK camera reads `transform.position` (same value we just set), positions camera with own PositionFollowStrength lerp
10. Client: Rigidbody is kinematic, accepts position updates without physics fighting

### SCK Lifecycle on Network-Spawned Objects:
1. `Instantiate()` → `Awake()` (GameAgent registers with GameAgentManager)
2. `Start()` → `SetStartAtCheckpoint` coroutine may teleport to checkpoint, `Engines.Start()` → `SetEngineActivation(true)` if `activateEnginesAtStart`
3. `Fusion.Spawned()` → our code runs (DisableLocalInput for remote, DisableClientOwnShipScripts for client's own ship, kinematic fix, camera setup, Destroy SetStartAtCheckpoint on remotes)
4. `FixedUpdateNetwork()` begins being called each network tick (NOT on proxies)
5. `FixedUpdate()` [order -200] runs on non-authority ships, applying SmoothDamp position follow via direct transform writes
6. Camera `FixedUpdate()` [order 0] reads the position we just set, applies its own follow damping

---

## NEXT STEPS (Recommended — Priority Order)

1. **FIX CLIENT SHIP JITTER (Bug 20)** — the PRIMARY remaining issue:
   - Try moving position follow from FixedUpdate to Render() (display-rate updates)
   - Or try removing SmoothDamp entirely and using simple exponential lerp
   - Or try reducing positionSmoothTime from 0.1 to 0.05 for more responsiveness
   - Check if any SCK component is still modifying the client ship's transform
   - Consider Fusion's built-in snapshot interpolation (InterpolationDataSource)
2. **Fix client ship responsiveness** — ship feels sluggish, possibly due to SmoothDamp lag
3. **Re-implement SCK banking** — linkYawAndRoll and auto-level features are lost when VehicleInput is disabled on client. May need manual implementation in the bridge's FixedUpdateNetwork.
4. **Test on separate PCs** with teammate — some jitter may be same-machine testing artifact
5. **Fix build resolution/UI cutoff** — may need canvas scaler investigation
6. **Clean up debug logging** once networking is confirmed stable
7. **Make a Development Build** for host-side debugging
8. **Mouse sensitivity tuning** — `reticleSpeed = 1.0f`, `maxReticleDistance = 0.475f`, `mouseDeadRadius = 0.02f`

---

## TESTING SETUP

### Same-Machine Testing (current):
- **Host**: Built game (standalone, auto-hosts)
- **Client**: Unity Editor (press J to join)
- **Limitation**: Both apps share same mouse, causing artifacts that don't exist on separate PCs
- **Workaround**: `Application.isFocused` gate ensures only the focused app reads mouse input

### Separate-PC Testing (recommended):
- Same Photon AppID on both machines
- Same session name "GV_Race"
- Same region "us"
- One person hosts (Start Host), other joins (Start Client)
- Photon Fusion 2 handles NAT traversal/relay automatically
- Most same-machine artifacts (mouse sharing, focus issues) will not exist

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
- `Assets/SpaceCombatKit/SpaceCombatKit/Scripts/Input/PlayerInput_Base_SpaceshipControls.cs` — has linkYawAndRoll, auto-level
- `Assets/SpaceCombatKit/VSXPackageLibrary/Packages/GameStateSystem/GameStateManager.cs`
- `Assets/SpaceCombatKit/VSXPackageLibrary/Packages/Cameras/VehicleCamera.cs`
- `Assets/SpaceCombatKit/SpaceCombatKit/Scripts/CameraControllers/SpaceFighterCameraController.cs` — camera follow logic (Session 5)
- `Assets/SpaceCombatKit/VSXPackageLibrary/Packages/CameraSystem/Runtime/Core/CameraEntity.cs` — base camera entity (Session 5)

## CURRENT STATE OF KEY FILES (Session 5)

### NetworkedSpaceshipBridge.cs — Key position follow parameters:
```csharp
// Position smoothing
[SerializeField] private float positionSmoothTime = 0.1f;  // SmoothDamp time (seconds)
[SerializeField] private float rotationSmoothSpeed = 10f;   // Slerp speed for rotation

// In Spawned() for non-authority:
rb.isKinematic = true;
rb.interpolation = RigidbodyInterpolation.None;  // CRITICAL — prevents camera-ship desync

// In ApplyPositionFollow():
_intendedPosition = Vector3.SmoothDamp(_cachedTarget.position, SyncPosition, ref _smoothVelocity, positionSmoothTime, Mathf.Infinity, Time.fixedDeltaTime);
float rotT = Mathf.Clamp01(rotationSmoothSpeed * Time.fixedDeltaTime);
_intendedRotation = Quaternion.Slerp(_cachedTarget.rotation, SyncRotation, rotT);
_cachedTarget.position = _intendedPosition;  // Direct write, NOT MovePosition
_cachedTarget.rotation = _intendedRotation;
```

### NetworkedPlayerInput.cs — Key input parameters:
```csharp
private float reticleSpeed = 1.0f;           // Mouse delta sensitivity
private float maxReticleDistance = 0.475f;    // Max reticle offset from center (viewport coords)
private float mouseDeadRadius = 0.02f;       // Dead zone radius
// No auto-centering (removed in Session 4)
// Uses Mouse.current.delta.ReadValue() for locked cursor compatibility
```
