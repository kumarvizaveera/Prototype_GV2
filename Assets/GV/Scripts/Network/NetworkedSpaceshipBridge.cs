using UnityEngine;
using Fusion;
using VSX.Engines3D;
using VSX.CameraSystem;
using VSX.Vehicles;
using VSX.VehicleCombatKits;
using VSX.Controls;

namespace GV.Network
{
    /// <summary>
    /// Bridge between Fusion network input and SpaceCombatKit's VehicleEngines3D.
    /// On host/server: reads networked input and applies to engines for remote players.
    /// On clients: disables local input on ALL ships, camera follows local player only.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class NetworkedSpaceshipBridge : NetworkBehaviour
    {
        [Header("References")]
        [SerializeField] private VehicleEngines3D engines;
        [SerializeField] private VSX.Weapons.TriggerablesManager triggerablesManager;

        [Header("Input Scripts to Disable on Remote")]
        [SerializeField] private MonoBehaviour[] localInputScripts;

        // Local state for visual feedback (not networked for now)
        private bool _isBoosting;
        public bool IsBoosting => _isBoosting;

        // Flag to track if we've done the initial authority check
        private bool _hasCheckedAuthority = false;

        // Flag to track if camera setup has been retried
        private bool _cameraSetupDone = false;
        private float _cameraRetryTimer = 0f;
        private const float CAMERA_RETRY_INTERVAL = 0.5f;
        private const float CAMERA_RETRY_MAX_TIME = 5f;

        public override void Spawned()
        {
            // Find engines if not assigned
            if (engines == null)
            {
                engines = GetComponentInChildren<VehicleEngines3D>();
            }

            if (triggerablesManager == null)
            {
                triggerablesManager = GetComponentInChildren<VSX.Weapons.TriggerablesManager>();
            }

            Debug.Log($"[NetworkedSpaceshipBridge] Spawned on {gameObject.name} - " +
                      $"HasInputAuthority: {Object.HasInputAuthority}, " +
                      $"HasStateAuthority: {Object.HasStateAuthority}, " +
                      $"InputAuthority: {Object.InputAuthority}");

            // --- CRITICAL: Make Rigidbody kinematic on non-authority instances ---
            // The prefab uses Rigidbody for physics movement but only has NetworkTransform (not NetworkRigidbody3D).
            // Without this, the local Rigidbody fights NetworkTransform and the ship stays stuck at spawn.
            // Only the state authority (host) should run physics; everyone else lets NetworkTransform drive position.
            if (!Object.HasStateAuthority)
            {
                var rb = GetComponentInChildren<Rigidbody>(true);
                if (rb != null)
                {
                    rb.isKinematic = true;
                    rb.interpolation = RigidbodyInterpolation.None; // Let NetworkTransform handle interpolation
                    Debug.Log($"[NetworkedSpaceshipBridge] Made Rigidbody kinematic (no state authority)");
                }
            }

            // --- Immediately disable input on remote player ships ---
            // This MUST happen right away to prevent both ships responding to same keyboard
            bool isLocalPlayer = Object.HasInputAuthority;
            if (Runner != null && Object.InputAuthority != PlayerRef.None)
            {
                isLocalPlayer = Object.InputAuthority == Runner.LocalPlayer;
            }

            if (!isLocalPlayer)
            {
                Debug.Log($"[NetworkedSpaceshipBridge] Remote player ship - disabling ALL local input immediately");
                DisableLocalInput();
                _hasCheckedAuthority = true;
            }
            else
            {
                Debug.Log($"[NetworkedSpaceshipBridge] Local player ship - keeping input enabled");
                _hasCheckedAuthority = true;

                // Setup camera for local player
                SetupCamera();
            }
        }

        private void SetupCamera()
        {
            // Find the Vehicle component on the spawned player
            var vehicle = GetComponentInChildren<Vehicle>(true);
            if (vehicle == null)
            {
                Debug.LogWarning("[NetworkedSpaceshipBridge] No Vehicle found on spawned player!");
                return;
            }

            // Find the VehicleCamera in the scene (SpaceCombatKit's camera system)
            var vehicleCamera = FindFirstObjectByType<VehicleCamera>();
            if (vehicleCamera != null)
            {
                vehicleCamera.SetVehicle(vehicle);
                _cameraSetupDone = true;
                Debug.Log($"[NetworkedSpaceshipBridge] VehicleCamera now following: {vehicle.name}");
                return;
            }

            // Fallback: try generic CameraEntity
            var cameraEntity = FindFirstObjectByType<CameraEntity>();
            if (cameraEntity != null)
            {
                var cameraTarget = GetComponentInChildren<CameraTarget>(true);
                if (cameraTarget != null)
                {
                    cameraEntity.SetCameraTarget(cameraTarget);
                    _cameraSetupDone = true;
                    Debug.Log($"[NetworkedSpaceshipBridge] CameraEntity now following: {name}");
                    return;
                }
            }

            Debug.LogWarning("[NetworkedSpaceshipBridge] No camera system found yet - will retry in Update");
        }

        /// <summary>
        /// Retry camera setup in Update for cases where the camera isn't ready at Spawned() time.
        /// This is critical for clients where the scene camera may initialize after the network object.
        /// </summary>
        private void Update()
        {
            if (_cameraSetupDone) return;
            if (!Object.HasInputAuthority) return;

            _cameraRetryTimer += Time.deltaTime;
            if (_cameraRetryTimer >= CAMERA_RETRY_MAX_TIME)
            {
                Debug.LogError("[NetworkedSpaceshipBridge] Camera setup failed after max retries!");
                _cameraSetupDone = true; // Stop trying
                return;
            }

            // Retry every CAMERA_RETRY_INTERVAL seconds
            if (Time.frameCount % (int)(CAMERA_RETRY_INTERVAL / Time.deltaTime + 1) == 0)
            {
                Debug.Log("[NetworkedSpaceshipBridge] Retrying camera setup...");
                SetupCamera();
            }
        }

        private void CheckAuthorityAndSetupInput()
        {
            if (_hasCheckedAuthority) return;
            _hasCheckedAuthority = true;

            // Check if this is the local player
            bool isLocalPlayer = Object.HasInputAuthority;

            // Alternative check: compare the input authority player to the local player
            if (Runner != null && Object.InputAuthority != PlayerRef.None)
            {
                isLocalPlayer = Object.InputAuthority == Runner.LocalPlayer;
            }

            Debug.Log($"[NetworkedSpaceshipBridge] Authority Check - HasInputAuthority: {Object.HasInputAuthority}, " +
                      $"InputAuthority: {Object.InputAuthority}, LocalPlayer: {Runner?.LocalPlayer}, " +
                      $"HasStateAuthority: {Object.HasStateAuthority}");

            // Disable local input scripts on remote players
            if (!isLocalPlayer)
            {
                Debug.Log($"[NetworkedSpaceshipBridge] Remote player - disabling local input");
                DisableLocalInput();
            }
            else
            {
                Debug.Log($"[NetworkedSpaceshipBridge] Local player - keeping input enabled");
            }
        }

        /// <summary>
        /// Comprehensively disables ALL input-related components on this ship.
        /// This prevents remote player ships from responding to local keyboard/mouse input.
        /// </summary>
        private void DisableLocalInput()
        {
            // 1. Disable explicitly assigned input scripts
            if (localInputScripts != null)
            {
                foreach (var script in localInputScripts)
                {
                    if (script != null)
                    {
                        script.enabled = false;
                        Debug.Log($"[NetworkedSpaceshipBridge] Disabled assigned script: {script.GetType().Name}");
                    }
                }
            }

            // 2. Disable ALL VehicleInput-derived components (covers the entire SCK input hierarchy)
            // Hierarchy: GeneralInput -> VehicleInput -> PlayerInput_Base_* -> PlayerInput_InputSystem_*
            var vehicleInputs = GetComponentsInChildren<VehicleInput>(true);
            foreach (var vi in vehicleInputs)
            {
                ((MonoBehaviour)vi).enabled = false;
                Debug.Log($"[NetworkedSpaceshipBridge] Disabled VehicleInput: {vi.GetType().Name}");
            }

            // 3. Also disable any GeneralInput at the base level
            var generalInputs = GetComponentsInChildren<VSX.Controls.GeneralInput>(true);
            foreach (var gi in generalInputs)
            {
                ((MonoBehaviour)gi).enabled = false;
                Debug.Log($"[NetworkedSpaceshipBridge] Disabled GeneralInput: {gi.GetType().Name}");
            }

            // 4. Disable any Unity PlayerInput component (New Input System)
            var unityPlayerInputs = GetComponentsInChildren<UnityEngine.InputSystem.PlayerInput>(true);
            foreach (var upi in unityPlayerInputs)
            {
                upi.enabled = false;
                Debug.Log($"[NetworkedSpaceshipBridge] Disabled Unity PlayerInput on: {upi.gameObject.name}");
            }

            // 5. Handle GameAgent on remote ships:
            //    - Unregister from GameAgentManager (prevents interference with host's focused agent)
            //    - Manually enter the vehicle so SCK's ModuleManagers activate (renders weapons, effects, etc.)
            //    - Then destroy the GameAgent component so it can't interfere further
            //    Note: GameAgent.Awake() already registered before Spawned(), so we must unregister.
            //    Note: startingVehicle is null on the prefab, so EnterVehicle never runs in Start().
            //    We need to call it ourselves to activate the vehicle's module system.
            var vehicle = GetComponentInChildren<Vehicle>(true);
            var gameAgents = GetComponentsInChildren<GameAgent>(true);
            foreach (var ga in gameAgents)
            {
                // Unregister from the singleton to prevent any scene-level reactions
                if (GameAgentManager.Instance != null)
                {
                    GameAgentManager.Instance.Unregister(ga);
                    Debug.Log($"[NetworkedSpaceshipBridge] Unregistered remote GameAgent: {ga.name}");
                }

                // Manually enter the vehicle to activate SCK's ModuleManagers
                // This makes weapons, effects, and visual modules visible
                if (vehicle != null && !ga.IsInVehicle)
                {
                    ga.EnterVehicle(vehicle);
                    Debug.Log($"[NetworkedSpaceshipBridge] Entered vehicle for remote ship activation: {vehicle.name}");
                }

                // Now destroy the GameAgent component entirely
                // (the vehicle stays entered, modules stay active, but no further interference)
                Destroy(ga);
                Debug.Log($"[NetworkedSpaceshipBridge] Destroyed GameAgent on remote ship: {ga.gameObject.name}");
            }

            // Re-disable any VehicleInput scripts that EnterVehicle() may have re-initialized
            var reactivatedInputs = GetComponentsInChildren<VehicleInput>(true);
            foreach (var vi in reactivatedInputs)
            {
                ((MonoBehaviour)vi).enabled = false;
            }
            var reactivatedGeneralInputs = GetComponentsInChildren<VSX.Controls.GeneralInput>(true);
            foreach (var gi in reactivatedGeneralInputs)
            {
                ((MonoBehaviour)gi).enabled = false;
            }
            Debug.Log($"[NetworkedSpaceshipBridge] Re-disabled {reactivatedInputs.Length} input scripts after vehicle activation");
        }

        // Debug flags
        private float _lastDebugTime = 0f;
        private bool _loggedFirstTick = false;

        public override void FixedUpdateNetwork()
        {
            // ONE-TIME unconditional log to confirm FixedUpdateNetwork is running
            if (!_loggedFirstTick)
            {
                _loggedFirstTick = true;
                var rb = GetComponentInChildren<Rigidbody>();
                Debug.Log($"[NetworkedSpaceshipBridge] FIRST TICK on {gameObject.name} - " +
                          $"StateAuth:{Object.HasStateAuthority}, InputAuth:{Object.HasInputAuthority}, " +
                          $"engines:{(engines != null ? engines.name : "NULL")}, " +
                          $"rb.isKinematic:{rb?.isKinematic}, pos:{transform.position}");
            }

            // Safety: re-check authority on first tick in case Spawned() ran before authority was assigned
            CheckAuthorityAndSetupInput();

            if (engines == null)
            {
                if (Time.time - _lastDebugTime > 2f)
                {
                    _lastDebugTime = Time.time;
                    Debug.LogWarning($"[NetworkedSpaceshipBridge] engines is NULL on {gameObject.name}!");
                }
                return;
            }

            // Try to get networked input for this player
            var input = GetInput<PlayerInputData>();
            if (!input.HasValue)
            {
                if (Time.time - _lastDebugTime > 2f)
                {
                    _lastDebugTime = Time.time;
                    Debug.LogWarning($"[NetworkedSpaceshipBridge] No input for {gameObject.name} - " +
                                    $"StateAuth:{Object.HasStateAuthority}, InputAuth:{Object.HasInputAuthority}");
                }
                return;
            }

            var data = input.Value;

            // === MOVEMENT ===
            // Only the state authority (host) drives physics for remote players.
            // The local player on host uses native SCK input scripts for movement.
            // The client's own ship movement is synced via NetworkTransform from the host.
            if (Object.HasStateAuthority && !Object.HasInputAuthority)
            {
                // Ensure engines are activated (SCK defaults to enginesActivated=false,
                // and the normal activation via Start()/ModuleManagers may not work on network-spawned remote ships)
                if (!engines.EnginesActivated)
                {
                    engines.SetEngineActivation(true);
                    engines.ControlsDisabled = false;
                    Debug.Log($"[NetworkedSpaceshipBridge] Force-activated engines on remote ship {gameObject.name}");
                }

                // Host processing REMOTE player's movement
                engines.SetSteeringInputs(data.steering);
                engines.SetMovementInputs(data.movement);

                _isBoosting = data.boost;
                engines.SetBoostInputs(data.boost ? new Vector3(0f, 0f, 1f) : Vector3.zero);

                // Debug: log every 2 seconds
                if (Time.time - _lastDebugTime > 2f)
                {
                    _lastDebugTime = Time.time;
                    var rb = GetComponentInChildren<Rigidbody>();
                    Debug.Log($"[NetworkedSpaceshipBridge] HOST driving remote ship {gameObject.name}: " +
                              $"movement={data.movement}, steering={data.steering}, boost={data.boost}, " +
                              $"enginesActive={engines.EnginesActivated}, controlsDisabled={engines.ControlsDisabled}, " +
                              $"pos={transform.position}, rb.isKinematic={rb?.isKinematic}, rb.velocity={rb?.linearVelocity}");
                }
            }

            // === WEAPONS ===
            // Three cases for weapon firing:
            // 1. HOST's own ship: HasStateAuthority=true, HasInputAuthority=true
            //    → Native SCK scripts handle weapons. Skip here to avoid double-firing.
            // 2. HOST processing REMOTE player: HasStateAuthority=true, HasInputAuthority=false
            //    → We trigger weapons here (authoritative fire).
            // 3. CLIENT's own ship: HasStateAuthority=false, HasInputAuthority=true
            //    → Native SCK weapon scripts can't fire properly on network-spawned ships.
            //    → We trigger weapons here for local visual/audio feedback.
            bool isHostLocalShip = Object.HasStateAuthority && Object.HasInputAuthority;
            if (!isHostLocalShip && (Object.HasStateAuthority || Object.HasInputAuthority))
            {
                ApplyWeaponInput(data);
            }
        }

        /// <summary>
        /// Applies weapon input from networked data to the TriggerablesManager.
        /// Called on both host (for remote players) and client (for local player visual feedback).
        /// </summary>
        private void ApplyWeaponInput(PlayerInputData data)
        {
            if (triggerablesManager == null) return;

            // Primary Fire (Button 0)
            if (data.buttons.IsSet(PlayerInputData.BUTTON_FIRE_PRIMARY))
            {
                triggerablesManager.StartTriggeringAtIndex(0);
            }
            else
            {
                triggerablesManager.StopTriggeringAtIndex(0);
            }

            // Secondary Fire (Button 1)
            if (data.buttons.IsSet(PlayerInputData.BUTTON_FIRE_SECONDARY))
            {
                triggerablesManager.StartTriggeringAtIndex(1);
            }
            else
            {
                triggerablesManager.StopTriggeringAtIndex(1);
            }

            // Missile Fire (Button 2)
            if (data.buttons.IsSet(PlayerInputData.BUTTON_FIRE_MISSILE))
            {
                triggerablesManager.StartTriggeringAtIndex(2);
            }
            else
            {
                triggerablesManager.StopTriggeringAtIndex(2);
            }
        }

        public override void Render()
        {
            // Visual feedback can be done here for all clients
            // e.g., boost effects, engine sounds based on IsBoosting
        }
    }
}
