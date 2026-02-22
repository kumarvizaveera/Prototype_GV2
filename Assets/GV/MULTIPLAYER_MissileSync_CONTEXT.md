# Multiplayer Missile & Network Syncing — Project Context

**Last Updated:** 2026-02-22  
**Project:** Prototype_GV2  
**Networking:** Photon Fusion 2 (Host Mode)  
**Framework:** SpaceCombatKit (VSX)  

---

## 1. Overview

This document summarizes all multiplayer networking issues encountered, the root causes identified, and all code changes made to resolve them. It covers missile targeting, team syncing, position sync, input sync, and the health/shield bar display problem.

---

## 2. Architecture Summary

| Script | Role |
|--------|------|
| `NetworkedSpaceshipBridge.cs` | Main networked bridge between Fusion and SpaceCombatKit. Handles position sync, input forwarding (via RPC), aim sync, target lock sync, and team sync. |
| `NetworkedPlayerInput.cs` | Collects local player input on the client. Sends input to `NetworkedSpaceshipBridge` via `CurrentInputData`. |
| `NetworkedAimOverride.cs` | Runs on the Host's proxy of the Client's ship. Overrides aim direction and target lock with values sent from the Client. |
| `NetworkedGameManager.cs` | Manages game state (countdown, race, waiting). Simple race loop manager. |
| `MissileWeapon.cs` (modified) | Extended to inject the locked target's `NetworkId` into the `ProjectileWeaponUnit` before firing. |
| `ProjectileWeaponUnit.cs` (modified) | Added `TargetIdForNextSpawn` property, passed into the spawned projectile via `onBeforeSpawned`. |
| `Missile.cs` (modified) | Overrode `Spawned()` to resolve `NetworkedTargetId` into a real `Trackable` and apply it as the missile's lock target. |
| `Projectile.cs` (modified) | Added `[Networked] public NetworkId NetworkedTargetId` to carry target info across the network. |

---

## 3. Known Issues & Status

### 3.1 Client-Fired Missiles Not Hitting Host — **FIXED**
- **Root Cause:** When the client fires a missile, Fusion spawns the real missile on the host. But the host's spawned missile had no target — the target was only known on the client side. 
- **Fix:** Added `TargetIdForNextSpawn` to `ProjectileWeaponUnit`. Before firing, `MissileWeapon` captures the locked target's `NetworkId` and stores it. `ProjectileWeaponUnit` passes it via `onBeforeSpawned` into the spawned `Projectile`. `Missile.Spawned()` resolves the ID back to a `Trackable` and locks it.

### 3.2 Host Not Seeing Client's Team / Health Bar — **FIXED (Red Box + Health Bars)**
- **Root Cause:** On the Host, the client's ship is a "proxy" object. SpaceCombatKit's `GameAgent` system is NOT initialized on proxies (the `Vehicle.OnEntered(GameAgent)` call never happens), so `Trackable.Team` remains `null`. The `Tracker` system requires `Team != null` to pass a trackable through its filter.
- **Team Fix:** `RPC_SendTeam` was added to `NetworkedSpaceshipBridge` and is called from the client. The host now searches existing trackables for a team with a matching name and assigns it to the proxy's `Trackable`. The red box now appears.
- **Health Bar Fix:** `HUDTargetInfo.OnTargetSelected()` searched for `IHealthInfo` starting from `Trackable.Rigidbody.transform.GetComponentInChildren<IHealthInfo>()`. On the proxy ship, `VehicleHealth` (which implements `IHealthInfo`) lives on a parent/sibling of the Rigidbody, not a child — so the search returned null. **Fixed** by broadening the search: now tries Rigidbody children → Trackable children → RootTransform children → Rigidbody parent, in that order.

### 3.3 Missiles Not Working on Host Screen — **FIXED (Setup Chain)**
- **Root Cause:** When ships are spawned via Fusion (not the normal SCK scene flow), `GameAgentVehicleSetupManager.UpdateTargetSelectors()` may not fire with correct timing. This means the host's own ship's `weaponsTargetSelector.SelectableTeams` was not populated, and `MissileWeapon.TargetLocker` may not be connected to the `TargetSelector`.
- **Fix:** Added `SetupHostMissileTargeting()` to `NetworkedSpaceshipBridge`. Runs at 1s and 3s after spawn on the HOST's own ship. It:
  1. Force-enters the GameAgent into the Vehicle if not already in (triggers `UpdateTargetSelectors`)
  2. Verifies `weaponsTargetSelector.SelectableTeams` is populated with `team.HostileTeams`
  3. Pushes the currently selected target to any MissileWeapon TargetLockers that have no target
  4. Logs full diagnostic info (trackables, teams, lock state) to help debug any remaining issues
- **Note:** The fix also adds heavy diagnostic logging. Check console for `[NetworkedSpaceshipBridge] HOST MISSILE SETUP:` messages to verify the targeting chain. If missiles still don't work, the logs will show exactly where the chain breaks.

### 3.4 Radar Shows Blue Box Instead of Red — **FIXED**
- **Root Cause:** All players shared the same `Team` ScriptableObject but the `Team.HostileTeams` list was empty.
- **Fix (Manual):** Open the `Team` ScriptableObject (e.g., `Player Team`) in the Unity Inspector and add **itself** to the `Hostile Teams` list. This tells the radar that same-team ships should be identified as hostile.

---

## 4. Modified Scripts — Diff Summary

### 4.1 `Assets/GV/Scripts/Network/NetworkedSpaceshipBridge.cs`

**Added RPCs:**
```
RPC_SendInput()        — Client → Host: sends steering/move/weapon input every Update
RPC_SendAimPosition()  — Client → Host: sends world-space aim point so host can override weapon aim
RPC_SendTargetLock()   — Client → Host: sends the locked target's NetworkId for missile targeting
RPC_SendTeam()         — Client → Host: sends the team name so host can assign it to the proxy Trackable
RPC_SendTransform()    — Client → Host: sends local position/rotation for client authority movement
```

**Added Networked Properties:**
```csharp
[Networked] private Vector3 SyncPosition { get; set; }
[Networked] private Quaternion SyncRotation { get; set; }
[Networked] private int SyncTick { get; set; }
```

**Key Logic:**
- In `Spawned()`: Proxy ships become kinematic. Input is disabled on all remote ships. `NetworkedAimOverride` is added dynamically on the Host's proxy of the Client.
- In `Update()` (client only): Sends RPCs every frame for input, aim, target lock, transform, and team.
- In `Update()` (host's own ship): Runs `SetupHostMissileTargeting()` at 1s and 3s after spawn to ensure missile targeting chain is connected.
- In `Render()`: Proxies follow `SyncPosition`/`SyncRotation` via SmoothDamp.

**Added Method: `SetupHostMissileTargeting()`**
- Force-enters GameAgent into Vehicle (triggers `UpdateTargetSelectors`)
- Populates `weaponsTargetSelector.SelectableTeams` from `team.HostileTeams` if empty
- Pushes selected target to MissileWeapon TargetLockers if they have no target
- Logs full diagnostic chain (trackables, teams, lock states)

---

### 4.2 `Assets/SpaceCombatKit/.../MissileWeapons/Missiles/MissileWeapon.cs`

**Added/Modified:**
```csharp
protected override void TriggerWeaponUnitOnce(int index)
{
    // Inject target NetworkId into weapon unit before firing
    if (weaponUnits[index] is ProjectileWeaponUnit projUnit)
    {
        projUnit.TargetIdForNextSpawn = default(Fusion.NetworkId);
        if (targetLocker != null && targetLocker.Target != null)
        {
            var netObj = targetLocker.Target.GetComponentInParent<Fusion.NetworkObject>();
            if (netObj != null)
                projUnit.TargetIdForNextSpawn = netObj.Id;
        }
    }
    base.TriggerWeaponUnitOnce(index);
}
```

---

### 4.3 `Assets/SpaceCombatKit/.../GunWeapons/ProjectileWeapons/ProjectileWeaponUnit.cs`

**Added property (line ~68):**
```csharp
public NetworkId TargetIdForNextSpawn { get; set; }
```

**Modified spawn callback** to set `NetworkedTargetId` on the spawned projectile:
```csharp
onBeforeSpawned: (runner, obj) => {
    var proj = obj.GetComponent<Projectile>();
    if (proj != null)
        proj.NetworkedTargetId = TargetIdForNextSpawn;
    // ... (other existing setup)
}
```

---

### 4.4 `Assets/SpaceCombatKit/.../GunWeapons/ProjectileWeapons/Projectile.cs`

**Added networked property:**
```csharp
[Networked] public NetworkId NetworkedTargetId { get; set; }
```

---

### 4.5 `Assets/SpaceCombatKit/.../MissileWeapons/Missiles/Missile.cs`

**Added override:**
```csharp
public override void Spawned()
{
    base.Spawned();
    if (NetworkedTargetId.IsValid && targetLocker != null)
    {
        if (Runner.TryFindObject(NetworkedTargetId, out NetworkObject targetNetObj))
        {
            var trackable = targetNetObj.GetComponentInChildren<Trackable>(true);
            if (trackable == null)
                trackable = targetNetObj.GetComponentInParent<Trackable>();
            if (trackable != null)
            {
                targetLocker.SetTarget(trackable);
                SetLockState(LockState.Locked);
            }
        }
    }
}
```

---

### 4.6 `Assets/SpaceCombatKit/VehicleCombatKits/Scripts/HUDTargetInfo.cs`

**Modified `OnTargetSelected()`** to broaden the `IHealthInfo` search on proxy ships:
```csharp
// Was: only searched from Rigidbody.transform downward (missed VehicleHealth on parent/sibling)
// Now: cascading search — Rigidbody children → Trackable children → RootTransform children → Rigidbody parent
healthInfo = null;
if (newTarget.Rigidbody != null)
    healthInfo = newTarget.Rigidbody.transform.GetComponentInChildren<IHealthInfo>();
if (healthInfo == null)
    healthInfo = newTarget.GetComponentInChildren<IHealthInfo>();
if (healthInfo == null && newTarget.RootTransform != null)
    healthInfo = newTarget.RootTransform.GetComponentInChildren<IHealthInfo>();
if (healthInfo == null && newTarget.Rigidbody != null)
    healthInfo = newTarget.Rigidbody.GetComponentInParent<IHealthInfo>();
```

---

## 5. Key SCK Components Relevant to Multiplayer

| Component | Purpose |
|-----------|---------|
| `Trackable` | Makes an object visible to the radar system. Has `Team` and `TrackableType`. |
| `Tracker` | Scans for `Trackable`s in range. Filters by team and type. |
| `TargetSelector` | Selects one `Trackable` from the `Tracker`'s list. Drives HUD target box. |
| `TargetLocker` | Steadily locks onto a specific `Trackable` over time. Required for missile lock. |
| `GameAgentVehicleSetupManager` | Sets `Trackable.Team` and `TargetSelector.SelectableTeams` when a `GameAgent` enters the vehicle. Does NOT run on network proxies. |
| `HUDTargetInfo` | Displays target health bars by reading `IHealthInfo` from `target.Rigidbody`. |
| `TrackableSceneManager` | Global registry of all active `Trackable`s. Feedsthe `Tracker` system. |

---

## 6. Remaining Work

1. ~~**Health/Shield bars on Host for Client ship**~~ — **DONE** (broadened IHealthInfo search in HUDTargetInfo).
2. ~~**Missile locking on Host screen**~~ — **DONE** (added SetupHostMissileTargeting with diagnostic logging).
3. **Detonation visuals on both screens**: Ensure the explosion FX prefab is also spawned on the client (may need a networked spawn or a `ClientRpc` for the visual-only explosion).
4. **Verify missile fix in-game**: Run host+client and check console for `HOST MISSILE SETUP` logs. If missiles still don't lock, the logs will show which part of the chain fails (selectableTeams, TargetLocker state, etc.).

---

## 7. Fusion 2 Host Mode Gotchas

- `FixedUpdateNetwork()` does NOT run on the client for its own ship (only host has state authority). All client-side logic must go in `Update()`.
- `GetInput()` silently fails. Use RPC-based input workaround (`RPC_SendInput`).
- Proxy ships (remote players on your screen) are **purely visual** — no simulation runs on them locally. All game logic runs on the **State Authority (host)**.
- `NetworkTransform` only syncs the root transform, not child physics objects. Manual `[Networked]` position sync is used instead.
- RPCs are not guaranteed to arrive every frame. Stale input is intentionally tolerated (using `_rpcInputAge` counter).

---

*End of document.*
