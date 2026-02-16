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

        // --- MANUAL POSITION SYNC ---
        // NetworkTransform alone doesn't reliably sync the host's own ship to clients.
        // The host's ship moves via Rigidbody physics (native SCK), and NetworkTransform
        // reads Transform.position in FixedUpdateNetwork. But the physics step may run
        // AFTER FUN, so NetworkTransform reads stale positions. For the client's ship this
        // isn't an issue because our bridge drives it via SetSteeringInputs within FUN.
        // Solution: manually sync position/rotation via [Networked] properties, written by
        // the state authority every tick, and applied on proxies in Render().
        [Networked] private Vector3 SyncPosition { get; set; }
        [Networked] private Quaternion SyncRotation { get; set; }
        [Networked] private int SyncTick { get; set; } // Diagnostic: increments every host FUN tick

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
        /// Update handles:
        /// 1. Camera setup retry for local player
        /// 2. Proxy position diagnostics (for host's ship on client where FixedUpdateNetwork doesn't run)
        /// </summary>
        private float _proxyDebugTimer = 0f;
        private bool _proxyFirstLog = false;

        private void Update()
        {
            // --- PROXY DIAGNOSTIC ---
            // In Fusion 2, FixedUpdateNetwork does NOT run on proxy objects (no state or input authority).
            // The host's ship on the client is a proxy. We use Update() to monitor its position.
            if (!Object.HasStateAuthority && !Object.HasInputAuthority)
            {
                if (!_proxyFirstLog)
                {
                    _proxyFirstLog = true;
                    var rb = GetComponentInChildren<Rigidbody>();
                    var nt = GetComponent<Fusion.NetworkTransform>();
                    Debug.Log($"[NetworkedSpaceshipBridge] PROXY FIRST UPDATE {gameObject.name}: " +
                              $"pos={transform.position}, enabled={this.enabled}, " +
                              $"NetworkTransform:{(nt != null ? nt.enabled.ToString() : "MISSING")}, " +
                              $"rb.isKinematic={rb?.isKinematic}");
                }

                _proxyDebugTimer += Time.deltaTime;
                if (_proxyDebugTimer > 3f)
                {
                    _proxyDebugTimer = 0f;
                    Debug.Log($"[NetworkedSpaceshipBridge] PROXY POS {gameObject.name}: " +
                              $"pos={transform.position}, syncPos={SyncPosition}, " +
                              $"syncTick={SyncTick}, rot={transform.rotation.eulerAngles}");
                }
                return; // Proxy doesn't need camera setup
            }

            // --- CAMERA RETRY (local player only) ---
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
            //    SAFETY: Skip this bridge itself (NetworkedSpaceshipBridge) — if accidentally added
            //    to localInputScripts in the Inspector, disabling it would kill FixedUpdateNetwork.
            if (localInputScripts != null)
            {
                foreach (var script in localInputScripts)
                {
                    if (script != null)
                    {
                        if (script == this)
                        {
                            Debug.LogWarning($"[NetworkedSpaceshipBridge] SKIPPING self in localInputScripts! Remove NetworkedSpaceshipBridge from the array in the Inspector.");
                            continue;
                        }
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

            // 5. DESTROY SetStartAtCheckpoint ONLY on PROXY ships (no state authority).
            //    On the HOST, we have state authority over remote ships and WANT the teleport to happen
            //    so the client's ship starts at the correct checkpoint position.
            //    On the CLIENT, proxy ships get their position from [Networked] SyncPosition,
            //    so SetStartAtCheckpoint would fight with our manual sync — destroy it there.
            if (!Object.HasStateAuthority)
            {
                var checkpointScripts = GetComponentsInChildren<SetStartAtCheckpoint>(true);
                foreach (var cs in checkpointScripts)
                {
                    cs.StopAllCoroutines(); // Stop any in-progress coroutine
                    Destroy(cs);
                    Debug.Log($"[NetworkedSpaceshipBridge] Destroyed SetStartAtCheckpoint on PROXY ship (would fight position sync)");
                }
            }
            else
            {
                Debug.Log($"[NetworkedSpaceshipBridge] Keeping SetStartAtCheckpoint on host-controlled remote ship (will teleport to checkpoint)");
            }

            // 6. Handle GameAgent on remote ships:
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

            // CRITICAL SAFETY: Ensure THIS component (NetworkedSpaceshipBridge) is still enabled.
            // EnterVehicle() or other SCK cascades may have disabled us. FixedUpdateNetwork
            // will NOT run if the component is disabled, which breaks all network functionality.
            if (!this.enabled)
            {
                this.enabled = true;
                Debug.LogWarning($"[NetworkedSpaceshipBridge] RE-ENABLED self after DisableLocalInput! Something disabled the bridge.");
            }
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
                var nt = GetComponent<Fusion.NetworkTransform>();
                Debug.Log($"[NetworkedSpaceshipBridge] FIRST TICK on {gameObject.name} - " +
                          $"StateAuth:{Object.HasStateAuthority}, InputAuth:{Object.HasInputAuthority}, " +
                          $"engines:{(engines != null ? engines.name : "NULL")}, " +
                          $"rb.isKinematic:{rb?.isKinematic}, pos:{transform.position}, " +
                          $"NetworkTransform:{(nt != null ? "YES" : "MISSING")}");
            }

            // DEBUG: Log position of remote ships we DON'T control (to diagnose host ship not syncing on client)
            // This logs for the host's ship on the client (no state auth, no input auth)
            if (!Object.HasStateAuthority && !Object.HasInputAuthority)
            {
                if (Time.time - _lastDebugTime > 2f)
                {
                    _lastDebugTime = Time.time;
                    Debug.Log($"[NetworkedSpaceshipBridge] REMOTE SHIP SYNC {gameObject.name}: " +
                              $"pos={transform.position}, rot={transform.rotation.eulerAngles}");
                }
            }

            // Safety: re-check authority on first tick in case Spawned() ran before authority was assigned
            CheckAuthorityAndSetupInput();

            // --- MANUAL POSITION SYNC: Write position on state authority (host) ---
            // This runs for ALL ships the host controls (both host's own and client's).
            // CRITICAL: We sync the RIGIDBODY's position, NOT the root transform.
            // In SCK, the Rigidbody+VehicleEngines3D live on a CHILD object (e.g. SpaceFighter_Light).
            // Physics moves the child via AddRelativeForce, but the ROOT transform stays at spawn.
            // The camera follows the child, so the host sees movement, but transform.position is stuck.
            if (Object.HasStateAuthority)
            {
                SyncTick++;
                var rb = GetComponentInChildren<Rigidbody>();
                if (rb != null)
                {
                    SyncPosition = rb.position;
                    SyncRotation = rb.rotation;
                }
                else
                {
                    SyncPosition = transform.position;
                    SyncRotation = transform.rotation;
                }
            }

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
            
            // Debug input status occasionally
            if (Object.HasStateAuthority && !Object.HasInputAuthority)
            {
                 if (Time.time - _lastDebugTime > 2f)
                 {
                     _lastDebugTime = Time.time;
                     if (!input.HasValue)
                        Debug.LogWarning($"[NetworkedSpaceshipBridge] Waiting for input on {gameObject.name} from Client {Object.InputAuthority}...");
                     else
                        Debug.Log($"[NetworkedSpaceshipBridge] Received Input from Client {Object.InputAuthority}: Move={input.Value.movement}, Steer={input.Value.steering}, Boost={input.Value.boost}");
                 }
            }

            if (!input.HasValue)
            {
                return;
            }

            var data = input.Value;

            // === MOVEMENT ===
            // Only the state authority (host) drives physics for remote players.
            // The local player on host uses native SCK input scripts for movement.
            // The client's own ship movement is synced via NetworkTransform from the host.
            if (Object.HasStateAuthority && !Object.HasInputAuthority)
            {
                // Ensure engines are activated (SCK's Engines.Start() may activate via activateEnginesAtStart,
                // but we force it here as a safety net for network-spawned objects)
                if (!engines.EnginesActivated)
                {
                    engines.SetEngineActivation(true);
                    Debug.Log($"[NetworkedSpaceshipBridge] Force-activated engines on remote ship {gameObject.name}");
                }

                // CRITICAL: Always ensure ControlsDisabled is false BEFORE setting inputs.
                // SCK's SetMovementInputs/SetSteeringInputs/SetBoostInputs all silently return
                // if controlsDisabled is true. This must be set EVERY tick, not just on first activation,
                // because Engines.Start() may activate engines before our first FixedUpdateNetwork,
                // and other SCK systems could re-enable controlsDisabled at any time.
                engines.ControlsDisabled = false;

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

        /// <summary>
        /// LateUpdate applies [Networked] position sync for proxy ships.
        /// We use LateUpdate instead of Render() because:
        /// - LateUpdate is a standard Unity callback, guaranteed to run on all enabled MonoBehaviours
        /// - It runs AFTER all Update/FixedUpdate/physics, so it has the final word on position
        /// - Fusion's Render() may not run on proxy objects in all configurations
        /// </summary>
        private bool _lateUpdateProxyFirstLog = false;

        private void LateUpdate()
        {
            // Only apply on proxies (host's ship on client — no state or input authority)
            if (!Object.HasStateAuthority && !Object.HasInputAuthority)
            {
                // One-time diagnostic to confirm LateUpdate is running and show SyncPosition value
                if (!_lateUpdateProxyFirstLog)
                {
                    _lateUpdateProxyFirstLog = true;
                    var rb = GetComponentInChildren<Rigidbody>();
                    Debug.Log($"[NetworkedSpaceshipBridge] PROXY LATE UPDATE ACTIVE on {gameObject.name}: " +
                              $"SyncPosition={SyncPosition}, rbChild={rb?.gameObject.name ?? "NULL"}, " +
                              $"rootPos={transform.position}, rbPos={rb?.position}");
                }

                // Apply the [Networked] position to the CHILD that has the Rigidbody,
                // matching how the host's physics moves the child, not the root.
                var proxyRb = GetComponentInChildren<Rigidbody>();
                if (proxyRb != null)
                {
                    proxyRb.transform.position = SyncPosition;
                    proxyRb.transform.rotation = SyncRotation;
                }
                else
                {
                    transform.position = SyncPosition;
                    transform.rotation = SyncRotation;
                }
            }
        }
    }
}
