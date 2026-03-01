# Plan: Spline-Based Random Spawn

## What We're Doing
Replace the fixed `spawnPoints` array in `NetworkManager` with a new system that picks random positions along the track spline, keeping players spaced apart. The spawn is deterministic (same on all clients) using `LevelSynchronizer`'s shared seed.

## Changes

### 1. Modify `NetworkManager.cs`
- **Remove** the `Transform[] spawnPoints` field
- **Add** new fields (all tweakable in Inspector):
  - `SplineContainer spawnSpline` — drag in the track spline
  - `float minSpawnSpacing = 50f` — minimum distance between players along the spline (normalized 0–1 converted to world units)
  - `float spawnOffsetFromSpline = 0f` — optional lateral offset (if ships shouldn't be dead-center on the spline)
- **Replace** `GetSpawnPosition()` / `GetSpawnRotation()` with a new method `CalculateSplineSpawnPoints()` that:
  1. Uses `LevelSynchronizer.Instance.LevelSeed` as the RNG seed (deterministic across all clients)
  2. Picks a random starting `t` value (0–1 along the spline)
  3. Places each subsequent player at `t + spacing`, wrapping around if the spline is a loop
  4. Evaluates position and tangent at each `t` using `SplineUtility`
  5. Sets rotation so ships face along the spline tangent (forward direction)
- **Update** `OnPlayerJoined()` to call the new method

### 2. Files NOT Changed
- `CheckpointNetwork.cs` — untouched, still manages race checkpoints
- `LevelSynchronizer.cs` — untouched, we just read its seed
- `NetworkedSpaceshipBridge.cs` — no changes needed (SetStartAtCheckpoint cleanup already done)
- `SplineTether.cs` / `SplineSpeedPenalty.cs` — untouched
- All other spawn systems (Astra refills, mass spawner, etc.) — untouched

### 3. In Unity (Veera does manually)
- On the NetworkManager GameObject: drag the track's `SplineContainer` into the new `Spawn Spline` field
- Tweak `Min Spawn Spacing` to taste
- Remove old spawn point GameObjects if they're no longer needed
- Remove `SetStartAtCheckpoint` component from `Player_ForMultiplayer` prefab (from previous change)

## How It Works (Simple Version)
- When the game starts, the host generates a random seed (already happening via LevelSynchronizer)
- When players join, the server picks random spots along the track spline using that seed
- Each player is guaranteed to be at least X units apart (configurable)
- Ships face the direction the track goes at that point
- All clients compute the same positions because they share the same seed
