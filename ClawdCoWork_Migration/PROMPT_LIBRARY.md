# PROMPT_LIBRARY

## Design Constraints

### Tone & Aesthetics
-   **Core Theme**: Mythic Futurism. Ancient Vedic technology realized through Type 2 Civilization engineering.
-   **Keywords**: *Vimana* (Ship), *Astra* (Weapon), *Prana* (Energy/Data), *Yantra* (Circuit/Rune).
-   **Forbidden**:
    -   Avoid direct "108" symbolism unless mathematically relevant.
    -   No "Guru" or standard religious terminology; imply advanced science indistinguishable from magic.
    -   Do NOT use "magic spells"; use "psionic algorithms" or "energy modulation".

### Visual Style
-   **Palette**: Electric Cyan, Neon Magenta, Holographic Green, Dark Chromatic Metal.
-   **Environment**: Non-Euclidean geometry, floating structures, glowing energy tracks.

## Code Generation Patterns

### 1. New Feature Request
```markdown
**Objective**: Implement [Feature Name]
**Context**: Multiplayer environment (Photon Fusion 2).
**Theme**: [Astra/Prana/Vimana] mechanic.
**Constraints**:
-   Must inherit from `NetworkBehaviour`.
-   Use `[Networked]` properties for state sync.
-   Use `RPC`s for one-shot events (explosions, sounds).
-   Handle `FixedUpdateNetwork` for physics.
```

### 2. Debugging Request
```markdown
**Error**: [Error Log / Description]
**Context**: Occurs during [Host/Client] session.
**Suspected System**: [Teleport/Combat/Movement]
**Checklist**:
1.  Is the object spawned via `Runner.Spawn`?
2.  Does it have a `NetworkObject` component?
3.  Are we modifying `NetworkTransform` directly or via input?
```

### 3. Creating a New Power-Up
```markdown
**Task**: Create logic for [New Power Name]
**Integration**:
-   Must read duration/strength from `PowerSphereMasterController`.
-   Must interface with `PowerUpManager`.
-   Visual effects must be local; State changes must be networked.
```

## Reusable Snippets

### Basic Networked Script Template
```csharp
using Fusion;
using UnityEngine;

public class FeatureName : NetworkBehaviour {
    [Networked] private float _networkedVar { get; set; }

    public override void FixedUpdateNetwork() {
        if (GetInput(out NetworkedPlayerInput input)) {
            // Logic
        }
    }
}
```

### Teleport Logic Safety
```csharp
// Use this pattern for teleporting to avoid interpolation glitches
if (Object.HasStateAuthority) {
    _networkTransform.TeleportToPosition(targetPos);
    _networkTransform.TeleportToRotation(targetRot);
}
```
