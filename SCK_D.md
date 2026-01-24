# SCK_D

Status: Not started

# **Space Combat Kit**

# **Documentation**

```
Offline, structured manual compiled from VSX Games UVC GitBook
pages

```

```
Compiled: January 21, 2026
Primary source: https://vsxgames.gitbook.io/universal-vehicle-combat

```

ℹ Info

This PDF reorganizes and paraphrases the online documentation into an

offline-friendly format. Component/class names are preserved for accuracy;

explanatory text is original unless otherwise stated.

# **Table of Contents**

# **Table of Contents**

- Table of Contents
- 
    1. Overview
    - 1.1 Typical scene building blocks
- 
    1. Quick Start (Playable Scene)
    - 2.1 Minimal scene recipe
    - 2.2 Recommended scene hierarchy (example)
- 
    1. Installation
    - 3.1 New project install
    - 3.2 Install into an existing project (SCK-safe)
- 
    1. Core Framework
    - 4.1 Game Agents
    - 4.2 Vehicles
    - 4.3 Vehicle Class routing (why it matters)
- 
    1. Module System
    - 5.1 Concepts
    - 5.2 Creating a module prefab
    - 5.3 Creating module mounts (hardpoints)
- 
    1. Input
    - 6.1 General Input (camera & UI)
    - 6.2 Vehicle Input (fighter/capital ship)
- 
    1. Spaceships
    - 7.1 Space fighters (6DOF)
    - 7.2 Building a capital ship (from scratch)
    - 7.3 Speed UI
- 
    1. Camera System
    - 8.1 Drop-in camera
    - 8.2 Adding camera targets and views
    - 8.3 Secondary cameras & death camera
- 
    1. Weapons
    - 9.1 Weapon hardpoints
    - 9.2 Weapon module anatomy
    - 9.3 Suggested integration pattern
    - 9.4 Pooling for projectiles (recommended)
- 
    1. Radar & Targeting
    - 10.1 Setup checklist
    - 10.2 Target selection UX
    - 10.3 Performance guidelines
- 
    1. Health, Damage, and Shields
    - 11.1 Recommended setup
    - 11.2 Shields (if used)
- 
    1. HUD & UI
    - 12.1 Core HUD widgets
    - 12.2 Binding strategy (recommended)
- 
    1. Loadout System
    - 13.1 Data you typically define
    - 13.2 Applying loadout at spawn time
- 
    1. AI (Space Combat Kit)
    - 14.1 Recommended AI architecture
    - 14.2 Quick AI checklist
- 
    1. Game States & Menus
    - 15.1 Common pattern
    - 15.2 Practical uses in SCK
- 
    1. Large Worlds & Performance
    - 16.1 Floating origin
    - 16.2 Object pooling
    - 16.3 Optimization checklist
- 
    1. Troubleshooting
    - 17.1 Setup verification
    - 17.2 Safe workflow for extending a template
- 
    1. Component Reference
- 
    1. Source Links

# **1. Overview**

```
Universal Vehicle Combat (UVC) provides the foundation for the Space Combat Kit
(SCK). Most gameplay features (weapons, sensors, upgrades) are delivered as
Modules that mount onto a Vehicle. Vehicles are controlled by Game Agents (players
or AI).

```

```
✓ Tip

```

```
If you inherited a template project, first identify: (1) where Game Agents are
created, (2) how a Game Agent enters a Vehicle, (3) how Vehicle Class selects
control scripts, and (4) how Modules are mounted. Everything else usually hangs
off these.

```

```
Game Agent controls Vehicle mounts Modules

```

```
Player input / AI decisions Movement, health, radar, mountsWeapons, upgrades, utilities

```

```
Recommended design rule: keep feature logic inside modules or small subsystems.Avoid hard-coding per-ship behavior in many places.

```

# **1.1 Typical scene building blocks**

- Scene objects: managers and shared systems (game state, teams, pooling, etc.).
- Vehicle camera: a drop-in camera rig that can follow vehicles and cycle views.
- Player agent: Game Agent configured for Input System.
- Vehicle(s): ships that have Vehicle + movement engines and optional modulespre-mounted.
- HUD: UI that reads vehicle/radar/weapon state.

# **2. Quick Start (Playable Scene)**

```
Use the kit’s prefabs to get to a controllable ship quickly, then swap assets and tune
settings. The goal of this section is to reach: fly + aim + target + shoot + UI feedback.

```

# **2.1 Minimal scene recipe**

- Create a new scene.
- Add the kit’s scene-setup prefab(s) (commonly includes managers and sharedsystems).
- Drag VehicleCamera_SCK into the scene (replaces your camera).
- Drag Player_InputSystem_SCK (or Player_SCK) into the scene.
- Place a spaceship prefab in the scene and ensure it has a Vehicle component andmovement/engine components.
- Assign the ship instance to the player’s Starting Vehicle field.
- Press Play and confirm: movement, aiming, HUD feedback.

```
⚠ Warning

```

```
Starting Vehicle should reference a ship instance placed in the scene, not the
prefab asset.

```

# **2.2 Recommended scene hierarchy (example)**

```
Scene
├─ SceneObjects_SCK (managers)
├─ VehicleCamera_SCK
├─ Player_InputSystem_SCK
└─ Ships
├─ Fighter_Player (Vehicle)
└─ Fighter_Enemy (Vehicle)

```

# **3. Installation**

# **3.1 New project install**

```
Recommended when evaluating the kit or when you don’t yet have existing project
settings to protect.

```

- Create a new Unity project.
- Open Window → Package Manager → My Assets, download and import the kit.
- Install/upgrade required dependencies when prompted (Input System, PostProcessing, etc.).
- Restart Unity if prompted (common when enabling Input System).

# **3.2 Install into an existing project (SCK-safe)**

```
Recommended when you already have custom layers, physics settings, input
bindings, or render pipeline setup.

```

- Install SCK into an empty Unity project first, then export the SpaceCombatKit folderas a .unitypackage.
- Import that package into your existing project.
- Open Space Combat Kit → Install Manager and run Update Project Settings toappend required settings instead of overwriting yours.

```
✓ Tip

```

```
After installation, verify: Tags/Layers, Input System enabled, required packages
present, and demo scenes load without missing scripts.

```

# **4. Core Framework**

# **4.1 Game Agents**

```
Game Agents represent controlling entities (players or AI). Agents can enter vehicles
at runtime, and you can query their current vehicle through an API property.

```

- Assign Starting Vehicle to have an agent enter a vehicle when the scene starts.
- Use a Game Agent Manager for centralized access (singleton; keep only one in thescene).
- When implementing multiplayer/local co-op, you generally create multiple GameAgents (one per player).

# **4.2 Vehicles**

```
Vehicles are controllable units (ships). A vehicle usually contains: movement engines,
mounts, health, radar, audio, VFX sockets, and camera targets.

```

- Create by adding a Vehicle component to a root GameObject.
- Assign a Vehicle Class to route correct input and camera behavior (fighter vs capitalship).
- Use destroy/restore for death/respawn loops and to cleanly toggle subsystems.

# **4.3 Vehicle Class routing (why it matters)**

- Vehicle classes let one Player prefab support multiple ship types without massiveif/else branching.
- AI can also select behaviors based on vehicle class.
- HUD/camera can change layouts or view presets per vehicle class.

# **5. Module System**

```
Modules are the main extension mechanism. A module provides a capability (weapon,
shield generator, radar upgrade, utility), and it attaches to a vehicle via a Module
Mount.

```

# **5.1 Concepts**

- Module: prefab/object with a Module component (and additional components thatimplement behavior).
- Module Mount: attachment point on the vehicle; can store multiple modules butonly one is mounted/active at a time.
- Module Type: ScriptableObject classification used to restrict compatibility.

# **5.2 Creating a module prefab**

- Create a GameObject and add a Module component.
- Configure module name, type, and any module-specific settings in the inspector.
- Add behavior scripts (e.g., weapon firing controller, shield logic).
- Convert to prefab and store under your project’s module folder.

# **5.3 Creating module mounts (hardpoints)**

- Add a child transform where the module should appear (e.g., wing hardpoint).
- Attach Module Mount to that transform.
- Configure allowed Module Types (e.g., Guns only).
- Optional: assign UI display metadata so HUD/loadout shows correct icon/name.

```
ShipRoot (Vehicle)
├─ Hardpoints
│ ├─ HP_LeftWing (Module Mount)
│ ├─ HP_RightWing (Module Mount)
│ └─ HP_Missile (Module Mount)
└─ Mesh

```

```
✓ Tip

```

```
When adding new mechanics, prefer creating a new Module Type + Module prefab
rather than modifying existing weapon modules. This keeps legacy content stable
and makes loadouts safer.

```

# **6. Input**

```
SCK uses Unity’s Input System. The docs separate General Input (camera/UI) from
Vehicle Input (ship controls), and use vehicle-class-specific PlayerInput components to
handle different control schemes.

```

# **6.1 General Input (camera & UI)**

- Add PlayerInput_InputSystem_CameraControls in the player hierarchy.
- Edit bindings in the General Input Input Actions asset.
- Camera view cycling is typically bound to bracket keys by default (change in theInput Actions asset).

# **6.2 Vehicle Input (fighter/capital ship)**

- Ensure each ship has the correct Vehicle Class.
- Add the matching vehicle control component to the player (e.g., ...FighterControls,...CapitalShipControls).
- If you add a new ship archetype, create a new Vehicle Class and a new controlcomponent rather than overloading existing ones.

```
⚠ Warning

```

```
If a ship does not respond correctly: verify Vehicle Class first. Input components
often route by class.

```

# **7. Spaceships**

# **7.1 Space fighters (6DOF)**

```
Space fighters are typically Rigidbody-driven and support full 6-degrees-of-freedom
flight. Use the kit’s fighter prefab as the baseline, then customize.

```

- Start from the fighter prefab and verify it moves correctly before swappingmesh/FX.
- Keep the Rigidbody and engine components on the vehicle root for stable physics.
- Add Camera Target and camera view targets for good follow-camera results.

# **7.2 Building a capital ship (from scratch)**

- Create an empty root object named for your ship.
- Add Vehicle and Vehicle Engines 3D components to the root.
- Parent your mesh under the root and reset its local position/rotation/scale.
- Assign Vehicle Class = CapitalShip.
- Use the SCK Input System player prefab or addPlayerInput_InputSystem_CapitalShipControls.

# **7.3 Speed UI**

- Add HUD Speed Display Controller to the vehicle.
- Assign the vehicle’s Vehicle Engines 3D and Rigidbody.
- Create a UI Image for the speed bar and set it to Filled type; assign to controller.
- Optional: add numeric speed text using the UVC Text wrapper (TMP/UGUI).

# **8. Camera System**

# **8.1 Drop-in camera**

- Drag VehicleCamera_SCK into the scene to get a working follow camera.
- If using your own camera, add a Vehicle Camera component and configure it tofollow Camera Targets.

# **8.2 Adding camera targets and views**

- Add Camera Target to the vehicle root.
- Add child transforms at desired viewpoints and attach Camera View Target to each.
- Tag views with Camera View ScriptableObjects so different vehicles can share viewcategories (e.g., Cockpit).

# **8.3 Secondary cameras & death camera**

- Use Secondary Camera to create extra camera layers that copy main camerarotation/FOV (e.g., starfield background).
- Use Vehicle Death Camera Controller to orbit the vehicle on death (works best whenvehicle destruction triggers properly).VehicleCamera_SCK├─ MainCamera (Vehicle Camera)├─ SecondaryCamera (Secondary Camera)└─ DeathCameraController (Vehicle Death Camera Controller)

# **9. Weapons**

```
Weapons are usually modules mounted to weapon hardpoints. In practice, a weapon
system includes: a mount point, a weapon module prefab, a firing controller, a
projectile/beam implementation, and HUD/FX bindings.

```

# **9.1 Weapon hardpoints**

- Create one Module Mount per hardpoint (left gun, right gun, missile bay).
- Restrict each mount using Module Types to prevent invalid loadouts.
- For visual alignment, place the mount transform at the muzzle/launcher location.

# **9.2 Weapon module anatomy**

```
GunModule (Module)
├─ WeaponController (fires / cooldown / audio)
├─ MuzzleTransform (spawn point)
└─ VFX/SFX (optional)

```

# **9.3 Suggested integration pattern**

- Ship controls decide when to fire (input/AI).
- Weapon module decides how to fire (projectile spawn, beam tick, missile lock).
- Damage is applied via collision/hit logic and consumed by target health systems.

```
✓ Tip

```

```
Keep weapon-specific parameters in the module prefab (rate of fire, spread,
projectile prefab, damage). Keep ship handling parameters in the ship prefab. This
avoids coupling that becomes painful during balancing.

```

# **9.4 Pooling for projectiles (recommended)**

- Pool your projectile prefabs if you expect rapid-fire weapons.
- Reset projectile state on reuse (velocity, lifetime timers, trail renderers).

# **10. Radar & Targeting**

```
Radar discovers and tracks targets. Target selection is often separated into selectors
(nearest, next, crosshair, etc.) so both player and AI can use the same selection rules.

```

# **10.1 Setup checklist**

- Add radar component(s) to the ship or an appropriate subsystem object.
- Mark target objects as trackable (vehicles, missiles, objectives).
- Configure which trackable types are valid for this radar instance.
- Add target selector components to enable cycling/acquisition.
- Bind radar outputs to HUD elements (radar scope, target boxes).

# **10.2 Target selection UX**

- Provide at least two selectors: Next target (cycling) and Nearest (quick reacquire).
- For missiles/lock-on weapons, require a valid selected target to fire.
- Use target notification cues (audio + UI) when a new target is acquired or lost.

# **10.3 Performance guidelines**

- Filter trackables by layer and type to reduce candidate sets.
- Use scan intervals instead of continuous per-frame scanning when possible.
- Pool targets that frequently spawn/despawn (missiles, projectiles) to reduceoverhead.

# **11. Health, Damage, and Shields**

```
Health systems control survivability and destruction. Tie health depletion into Vehicle
Destroy/Restore so cameras, UI, and game states stay consistent.

```

# **11.1 Recommended setup**

- Attach a primary health/damageable component to the vehicle root.
- Optional: add secondary damageable components to sub-parts if you want localizeddamage.
- Trigger vehicle destruction events on death (explosion, disable controls, etc.).
- On respawn, use vehicle restore to re-enable systems cleanly.

# **11.2 Shields (if used)**

- Keep shields as a separate resource from hull health.
- Regeneration should be time-based and should stop when taking damage (commonpattern).
- Expose shield state to HUD for clear feedback (bar, hit flash, audio).

```
⚠ Warning

```

```
When using pooling/respawn, ensure all health event subscriptions are unhooked
on disable and re-hooked on enable to avoid duplicate callbacks.

```

# **12. HUD & UI**

```
HUD is assembled from modular widgets that read state from the player’s current
vehicle (speed, weapons, radar, health). Avoid hard-coding references to one ship;
always bind to the active vehicle via the player agent.

```

# **12.1 Core HUD widgets**

- Speed: bar + numeric display (HUD Speed Display Controller).
- Weapon status: cooldown/ammo/heat + reticle.
- Radar display: scope/minimap + blips.
- Target feedback: target boxes/holograms and distance indicators.

# **12.2 Binding strategy (recommended)**

- HUD Manager listens for player vehicle changes (enter/exit) and rebinds widgets.
- Widgets read values from interfaces/resources rather than directly from unrelatedobjects.
- Update UI at a sensible frequency; not every value needs per-frame refresh.

# **13. Loadout System**

```
Loadout lets you configure which modules are placed onto a vehicle (via menus or
presets). Use loadout to avoid creating many duplicate ship prefabs for each weapon
combination.

```

# **13.1 Data you typically define**

- Which vehicles are available for selection.
- Which module mounts each vehicle exposes and what Module Types they allow.
- Which modules are unlocked/available for the player.
- How loadouts are saved/loaded (commonly JSON).

# **13.2 Applying loadout at spawn time**

- Spawn or enable a vehicle instance.
- Clear its mounts (optional, depends on design).
- Instantiate selected module prefabs and attach them to corresponding mounts.
- Mount/activate the selected modules (one per mount) and refresh HUD bindings.

```
✓ Tip

```

```
Apply loadout to a freshly spawned/restored vehicle to avoid stale module state
from previous sessions.

```

# **14. AI (Space Combat Kit)**

```
AI should reuse the exact same vehicle movement and weapon modules as the
player. AI scripts should decide: where to fly, who to target, and when to fire.

```

# **14.1 Recommended AI architecture**

- AI Game Agent enters a vehicle just like a player agent.
- AI selects targets through radar selectors.
- AI triggers weapon modules through the same trigger interfaces used by playerinput.
- AI movement outputs feed into the same control inputs (pitch/yaw/roll/throttle).

# **14.2 Quick AI checklist**

- AI ship has Vehicle Class set correctly (so control scripts are consistent).
- AI has radar configured and can see trackables.
- AI has weapon modules mounted and can trigger them.
- AI respects team/faction filtering to avoid friendly fire (if teams are used).

# **15. Game States & Menus**

```
Game state management separates flows like Main Menu → Playing → Paused → Game
Over. Menus often drive state transitions, loadout selection, and scene loading.

```

# **15.1 Common pattern**

- A Game State Manager owns current state and transitions.
- Systems/UI are enabled/disabled by state via Game State Enablers.
- Menu buttons trigger state changes or scene loads.

# **15.2 Practical uses in SCK**

- Disable player input while in menus.
- Trigger respawn flow on Game Over or Mission Failed.
- Swap HUD layouts when entering different gameplay states.

# **16. Large Worlds & Performance**

# **16.1 Floating origin**

```
Floating origin reduces floating-point precision issues in large space scenes by
re-centering the world periodically around the camera/player.

```

- Add Floating Origin Manager (singleton).
- Attach Floating Origin Object to objects that should shift with the origin.
- Use pre/post shift hooks for systems sensitive to position jumps.

# **16.2 Object pooling**

- Add Pool Manager (singleton).
- Pool frequently spawned objects (projectiles/explosions/debris).
- Prefer pool-friendly effects (e.g., stop+clear trails on disable).

# **16.3 Optimization checklist**

- Radar: limit scan frequency and trackables.
- UI: avoid rebuilding whole layouts every frame.
- Physics: keep colliders simple for projectiles; avoid expensive mesh colliders for fastmovers.

# **17. Troubleshooting**

# **17.1 Setup verification**

- Input not working: Input System enabled, player has camera + vehicle inputcomponents, ship instance assigned as Starting Vehicle, Vehicle Class matchescontrol script.
- Camera not following: ship has Camera Target, VehicleCamera is present, player iscontrolling the expected vehicle.
- Weapons not damaging: target has health/damageable, collision layers are correct,projectile state resets correctly when pooled.
- HUD wrong after swapping ship: ensure HUD rebinds when Game Agent enters anew vehicle.

# **17.2 Safe workflow for extending a template**

- Duplicate a demo scene and modify the copy (keep originals intact for reference).
- Add new features as modules or isolated controllers; avoid editing core scriptsunless necessary.
- When something breaks, validate in this order: Vehicle Class → PlayerInputcomponent → Starting Vehicle → Camera Target → Modules mounted.

# **18. Component Reference**

Area Key components / assets (examples) Where used

Scene SceneObjects_SCK

Managers

```
Core systems &
services

```

Core Game Agent

Game Agent Manager

Vehicle

Vehicle Class

```
Ownership & routing

```

Modules Module

Module Mount

Module Type

```
Weapons/upgrades +
swapping

```

Ships Vehicle Engines 3D

Fighter/Capital controllers

```
Movement & handling

```

Camera VehicleCamera_SCK

Vehicle Camera

Camera Target

Camera View Target

Secondary Camera

Vehicle Death Camera Controller

```
Follow + views + death
orbit

```

HUD HUD Speed Display Controller

UVC Text (TMP/UGUI)

```
Speed + widgets

```

Scale Floating Origin Manager/Object

Pool Manager

```
Large worlds +
performance

```

# **19. Source Links**

- Universal Vehicle Combat GitBook:https://vsxgames.gitbook.io/universal-vehicle-combat
- Space Combat Kit Tour:https://vsxgames.gitbook.io/universal-vehicle-combat/space-combat-kit-tour
- Installation (new project): https://vsxgames.gitbook.io/universal-vehicle-combat/installation/installing-in-new-project
- Installation (existing project - SCK): https://vsxgames.gitbook.io/universal-vehicle-combat/installation/installing-in-existing-project-sck
- Player Setup: https://vsxgames.gitbook.io/universal-vehicle-combat/player-setup
- Core framework (game agents):https://vsxgames.gitbook.io/universal-vehicle-combat/core-framework/game-agents
- Core framework (vehicles):https://vsxgames.gitbook.io/universal-vehicle-combat/core-framework/vehicles
- Core framework (modules):https://vsxgames.gitbook.io/universal-vehicle-combat/core-framework/modules
- Camera setup:https://vsxgames.gitbook.io/universal-vehicle-combat/camera/camera-setup
- Spaceships (speed UI):https://vsxgames.gitbook.io/universal-vehicle-combat/spaceships/speed-ui
- Spaceships (capital ships):https://vsxgames.gitbook.io/universal-vehicle-combat/spaceships/capital-ships
- Floating origin:https://vsxgames.gitbook.io/universal-vehicle-combat/floating-origin-system
- Object pooling:https://vsxgames.gitbook.io/universal-vehicle-combat/pooling-system