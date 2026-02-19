using UnityEngine;
using Fusion;
using VSX.Engines3D;
using VSX.CameraSystem;
using VSX.Vehicles;
using VSX.VehicleCombatKits;
using VSX.Controls;
using VSX.Utilities;

namespace GV.Network
{
    /// <summary>
    /// Bridge between Fusion network input and SpaceCombatKit's VehicleEngines3D.
    /// On host/server: reads networked input and applies to engines for remote players.
    /// On clients: disables local input on ALL ships, camera follows local player only.
    ///
    /// EXECUTION ORDER: -200 ensures our FixedUpdate runs BEFORE SCK's CameraEntity (default 0).
    /// This is critical because CameraEntity follows the ship in FixedUpdate when
    /// cameraTarget.Rigidbody != null. We must set the interpolated position BEFORE the
    /// camera reads it, otherwise the camera sees stale position → visible jitter.
    /// </summary>
    [DefaultExecutionOrder(-200)]
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

        // --- RPC-BASED INPUT BYPASS ---
        // Fusion's OnInput/GetInput pipeline silently fails in our setup (GetInput always returns
        // false on the host despite the client correctly calling input.Set()). This is likely a
        // Fusion IL weaver issue or an internal routing bug.
        // WORKAROUND: The client sends its input to the host via RPC every tick.
        // The host stores it and uses it in FixedUpdateNetwork instead of GetInput.
        private PlayerInputData _rpcInput;
        private bool _hasRpcInput = false;
        private int _rpcInputAge = 0; // ticks since last RPC input received

        /// <summary>
        /// RPC: Client (InputAuthority) sends input to Host (StateAuthority) every tick.
        /// This bypasses Fusion's broken OnInput/GetInput pipeline entirely.
        /// Using individual parameters instead of struct for maximum RPC compatibility.
        /// </summary>
        [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
        public void RPC_SendInput(float moveX, float moveY, float moveZ,
                                   float steerPitch, float steerYaw, float steerRoll,
                                   NetworkBool boost, int magic)
        {
            _rpcInput = new PlayerInputData
            {
                moveX = moveX,
                moveY = moveY,
                moveZ = moveZ,
                steerPitch = steerPitch,
                steerYaw = steerYaw,
                steerRoll = steerRoll,
                boost = boost,
                magicNumber = magic
            };
            _hasRpcInput = true;
            _rpcInputAge = 0;
        }

        // Local state for visual feedback (not networked for now)
        private bool _isBoosting;
        public bool IsBoosting => _isBoosting;

        // --- HUD CURSOR (client's own ship) ---
        // SCK's PlayerInput_Base_SpaceshipControls normally drives the CustomCursor via
        // MouseSteeringUpdate(), but that script is disabled on the client (pure visual shell).
        // We drive it from NetworkedPlayerInput's reticle position instead.
        private CustomCursor _hudCursor;
        private bool _hudCursorSearched = false;

        // --- PROXY SHIP POSITION FOLLOW (SmoothDamp) ---
        // Used for proxy ships (host's ship on client) which are kinematic.
        // Client's OWN ship uses real physics instead (client-side prediction).
        private Transform _cachedTarget = null;
        private Rigidbody _cachedRb = null;
        private int _prevSyncTick = -1;
        private bool _followInitialized = false;
        private Vector3 _intendedPosition;
        private Quaternion _intendedRotation;
        private Vector3 _smoothVelocity = Vector3.zero;

        [Header("Proxy Ship Smoothing (host's ship on client)")]
        [SerializeField] private float positionSmoothTime = 0.1f;
        [SerializeField] private float rotationSmoothSpeed = 10f;

        // --- CLIENT-SIDE PREDICTION ---
        // The client's own ship runs REAL physics locally (VehicleEngines3D + dynamic Rigidbody).
        // Input is applied locally every FixedUpdate for instant response.
        // The host's authoritative SyncPosition/SyncRotation gently corrects the client to prevent drift.
        [Header("Client-Side Prediction")]
        [Tooltip("How fast position corrects toward server (per second). Higher = more accurate, lower = smoother.")]
        [SerializeField] private float positionCorrectionRate = 8f;
        [Tooltip("How fast rotation corrects toward server (per second).")]
        [SerializeField] private float rotationCorrectionRate = 8f;
        [Tooltip("Snap to server position if gap exceeds this (meters). Handles respawn/teleport.")]
        [SerializeField] private float positionSnapThreshold = 5f;
        [Tooltip("Snap to server rotation if gap exceeds this (degrees).")]
        [SerializeField] private float rotationSnapThreshold = 45f;
        [Tooltip("Auto-bank into turns: roll = -yaw * yawRollRatio (re-implements SCK's linkYawAndRoll)")]
        [SerializeField] private bool enableLinkYawAndRoll = true;
        [SerializeField] private float yawRollRatio = 0.5f;

        private bool _clientPredictionActive = false;
        private bool _loggedPredictionActive = false;

        // --- JITTER DIAGNOSTIC LOGGING ---
        private bool _jitterDiagEnabled = true;
        private float _jitterDiagStartTime = -1f;
        private const float JITTER_DIAG_DURATION = 5f;
        private int _jitterDiagFixedCount = 0;
        private Vector3 _jitterLastLoggedPos;
        private bool _jitterDiagStarted = false;

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

            // --- RIGIDBODY SETUP FOR NON-AUTHORITY SHIPS ---
            // Two cases:
            // 1. CLIENT'S OWN SHIP (HasInputAuthority): Keep Rigidbody DYNAMIC for client-side prediction.
            //    The client runs its own physics locally for instant response.
            // 2. PROXY SHIPS (no authority): Make Rigidbody kinematic. Position comes from SmoothDamp.
            if (!Object.HasStateAuthority)
            {
                bool isOwnShip = Object.HasInputAuthority;
                // Also check via runner in case HasInputAuthority isn't set yet
                if (Runner != null && Object.InputAuthority != PlayerRef.None)
                {
                    isOwnShip = Object.InputAuthority == Runner.LocalPlayer;
                }

                var rb = GetComponentInChildren<Rigidbody>(true);
                if (rb != null)
                {
                    if (isOwnShip)
                    {
                        // CLIENT'S OWN SHIP: Keep dynamic for client-side prediction.
                        // VehicleEngines3D will apply real forces in its FixedUpdate.
                        // rb.interpolation stays as prefab default (None / m_Interpolate=0).
                        Debug.Log($"[NetworkedSpaceshipBridge] CLIENT OWN SHIP: Rigidbody stays DYNAMIC for client-side prediction. " +
                                  $"isKinematic={rb.isKinematic}, interpolation={rb.interpolation}");
                    }
                    else
                    {
                        // PROXY SHIP: Make kinematic — position from SmoothDamp.
                        rb.isKinematic = true;
                        rb.interpolation = RigidbodyInterpolation.None;
                        Debug.Log($"[NetworkedSpaceshipBridge] PROXY: Made Rigidbody kinematic, interpolation=None");
                    }
                }

                // Disable NetworkTransform on ALL non-authority ships.
                // Our [Networked] properties handle sync. NetworkTransform syncs the ROOT
                // transform (stuck at spawn in SCK), not the CHILD where physics lives.
                var nt = GetComponent<Fusion.NetworkTransform>();
                if (nt != null)
                {
                    nt.enabled = false;
                    Debug.Log($"[NetworkedSpaceshipBridge] Disabled NetworkTransform on non-authority ship");
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
            else if (Object.HasStateAuthority)
            {
                // HOST's own ship: keep SCK input enabled — host drives its own ship locally
                Debug.Log($"[NetworkedSpaceshipBridge] HOST local player ship - keeping SCK input enabled");
                _hasCheckedAuthority = true;
                SetupCamera();
            }
            else
            {
                // CLIENT's own ship: Set up CLIENT-SIDE PREDICTION.
                // The client runs REAL physics locally (VehicleEngines3D + dynamic Rigidbody).
                // Only SCK input scripts are disabled — our bridge feeds the engines directly.
                // The host's authoritative state gently corrects the client to prevent drift.
                Debug.Log($"[NetworkedSpaceshipBridge] CLIENT local player ship - setting up CLIENT-SIDE PREDICTION");
                SetupClientOwnShip();
                _hasCheckedAuthority = true;
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
                return; // Proxy doesn't need camera setup or RPC sending
            }

            // --- CLIENT: Send input to host via RPC every frame ---
            // CRITICAL: This MUST be in Update(), NOT FixedUpdateNetwork()!
            // In Fusion 2 Host Mode (without client-side prediction), FixedUpdateNetwork
            // only runs on the State Authority (host). The client has InputAuthority but NOT
            // StateAuthority, so FUN never executes on the client's own ship.
            // Update() runs on all enabled MonoBehaviours regardless of Fusion authority.
            if (Object.HasInputAuthority && !Object.HasStateAuthority)
            {
                var nm = NetworkManager.Instance;
                if (nm != null)
                {
                    var playerInput = nm.GetComponent<NetworkedPlayerInput>();
                    if (playerInput != null)
                    {
                        var clientData = playerInput.CurrentInputData;
                        // Encode player ID in magic: Player:2 → magic=2042
                        int playerMagic = nm.Runner.LocalPlayer.PlayerId * 1000 + 42;
                        RPC_SendInput(clientData.moveX, clientData.moveY, clientData.moveZ,
                                      clientData.steerPitch, clientData.steerYaw, clientData.steerRoll,
                                      clientData.boost, playerMagic);

                        // One-time log to confirm RPC sending is active
                        if (!_loggedFirstRpcSend)
                        {
                            _loggedFirstRpcSend = true;
                            Debug.Log($"[NetworkedSpaceshipBridge] CLIENT FIRST RPC SEND (from Update) on {gameObject.name}: " +
                                      $"throttle={clientData.moveZ:F2}, steer=({clientData.steerPitch:F2},{clientData.steerYaw:F2}), " +
                                      $"magic={playerMagic}, LocalPlayer={nm.Runner.LocalPlayer}");
                        }

                        // --- HUD CURSOR UPDATE (client's own ship) ---
                        // SCK's PlayerInput_Base_SpaceshipControls.MouseSteeringUpdate() normally
                        // calls hudCursor.SetViewportPosition(), but that script is disabled on the
                        // client (pure visual shell). We drive the HUD cursor from NetworkedPlayerInput's
                        // virtual reticle position instead.
                        if (!_hudCursorSearched)
                        {
                            _hudCursorSearched = true;
                            _hudCursor = GetComponentInChildren<CustomCursor>(true);
                            if (_hudCursor != null)
                            {
                                Debug.Log($"[NetworkedSpaceshipBridge] CLIENT: Found HUD cursor '{_hudCursor.name}' for reticle sync");
                            }
                            else
                            {
                                Debug.LogWarning($"[NetworkedSpaceshipBridge] CLIENT: No CustomCursor found on ship — HUD cursor won't move");
                            }
                        }
                        if (_hudCursor != null)
                        {
                            Vector2 reticle = playerInput.ReticlePosition;
                            _hudCursor.SetViewportPosition(new Vector3(reticle.x, reticle.y, 0f));
                        }
                    }
                }
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
        /// Sets up the CLIENT's own ship for CLIENT-SIDE PREDICTION.
        /// Unlike the old "visual shell" approach, the client now runs REAL physics:
        /// - VehicleEngines3D stays ENABLED (local physics simulation)
        /// - Rigidbody stays DYNAMIC (not kinematic — real forces apply)
        /// - Only SCK INPUT scripts are disabled (our bridge feeds engines directly)
        /// - The host's authoritative state gently corrects the client via server reconciliation
        /// </summary>
        private void SetupClientOwnShip()
        {
            // 1. Disable SCK's VehicleInput scripts (mouse/keyboard input)
            // Our bridge reads from NetworkedPlayerInput and feeds the engines directly.
            var vehicleInputs = GetComponentsInChildren<VehicleInput>(true);
            foreach (var vi in vehicleInputs)
            {
                ((MonoBehaviour)vi).enabled = false;
                Debug.Log($"[NetworkedSpaceshipBridge] CLIENT PREDICT: Disabled VehicleInput: {vi.GetType().Name}");
            }

            // 2. Disable GeneralInput components
            var generalInputs = GetComponentsInChildren<VSX.Controls.GeneralInput>(true);
            foreach (var gi in generalInputs)
            {
                ((MonoBehaviour)gi).enabled = false;
                Debug.Log($"[NetworkedSpaceshipBridge] CLIENT PREDICT: Disabled GeneralInput: {gi.GetType().Name}");
            }

            // 3. Disable Unity PlayerInput (New Input System)
            var unityPlayerInputs = GetComponentsInChildren<UnityEngine.InputSystem.PlayerInput>(true);
            foreach (var upi in unityPlayerInputs)
            {
                upi.enabled = false;
                Debug.Log($"[NetworkedSpaceshipBridge] CLIENT PREDICT: Disabled Unity PlayerInput: {upi.gameObject.name}");
            }

            // 4. KEEP VehicleEngines3D ENABLED — this is the core of client-side prediction!
            // The engines will process our inputs in their FixedUpdate and apply forces/torques.
            if (engines != null)
            {
                Debug.Log($"[NetworkedSpaceshipBridge] CLIENT PREDICT: KEEPING VehicleEngines3D ENABLED: {engines.name}");
            }

            // 5. Destroy SetStartAtCheckpoint — fights with network position sync
            var checkpointScripts = GetComponentsInChildren<SetStartAtCheckpoint>(true);
            foreach (var cs in checkpointScripts)
            {
                cs.StopAllCoroutines();
                Destroy(cs);
                Debug.Log($"[NetworkedSpaceshipBridge] CLIENT PREDICT: Destroyed SetStartAtCheckpoint");
            }

            // 6. Unregister GameAgent (prevent focus/input routing interference)
            var gameAgents = GetComponentsInChildren<GameAgent>(true);
            foreach (var ga in gameAgents)
            {
                if (GameAgentManager.Instance != null)
                {
                    GameAgentManager.Instance.Unregister(ga);
                    Debug.Log($"[NetworkedSpaceshipBridge] CLIENT PREDICT: Unregistered GameAgent: {ga.name}");
                }
                ga.enabled = false;
            }

            // 7. Cache the Rigidbody (stays DYNAMIC for real physics)
            var clientRb = GetComponentInChildren<Rigidbody>();
            if (clientRb != null)
            {
                _cachedTarget = clientRb.transform;
                _cachedRb = clientRb;
                // Rigidbody stays dynamic — real forces will be applied by VehicleEngines3D
                Debug.Log($"[NetworkedSpaceshipBridge] CLIENT PREDICT: Rigidbody DYNAMIC on '{_cachedTarget.name}', " +
                          $"isKinematic={clientRb.isKinematic}, mass={clientRb.mass}, " +
                          $"linearDamping={clientRb.linearDamping}, angularDamping={clientRb.angularDamping}");
            }

            // 8. Disable NetworkTransform (our [Networked] properties handle sync)
            var nt = GetComponent<Fusion.NetworkTransform>();
            if (nt != null)
            {
                nt.enabled = false;
                Debug.Log($"[NetworkedSpaceshipBridge] CLIENT PREDICT: Disabled NetworkTransform");
            }

            _clientPredictionActive = true;

            // SAFETY: Ensure THIS bridge component is still enabled
            if (!this.enabled)
            {
                this.enabled = true;
                Debug.LogWarning($"[NetworkedSpaceshipBridge] CLIENT PREDICT: RE-ENABLED self!");
            }

            Debug.Log($"[NetworkedSpaceshipBridge] CLIENT PREDICT: Setup complete. " +
                      $"Engines={engines != null && engines.enabled}, Rigidbody=DYNAMIC, " +
                      $"linkYawRoll={enableLinkYawAndRoll}, correctionRate=pos:{positionCorrectionRate}/rot:{rotationCorrectionRate}");
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
                // This makes weapons, effects, and visual modules visible.
                // IMPORTANT: Save and restore the camera target — EnterVehicle may hijack it.
                var savedCameraVehicle = FindFirstObjectByType<VehicleCamera>()?.TargetVehicle;
                if (vehicle != null && !ga.IsInVehicle)
                {
                    ga.EnterVehicle(vehicle);
                    Debug.Log($"[NetworkedSpaceshipBridge] Entered vehicle for remote ship activation: {vehicle.name}");
                }
                // Restore camera to whatever it was following before (might be null on first ship spawn)
                if (savedCameraVehicle != null)
                {
                    var vc = FindFirstObjectByType<VehicleCamera>();
                    if (vc != null) vc.SetVehicle(savedCameraVehicle);
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

            // Cache the child transform for position interpolation on proxy ships.
            // Rigidbody is kept alive (kinematic) because SCK scripts depend on it.
            if (!Object.HasStateAuthority)
            {
                var proxyRb = GetComponentInChildren<Rigidbody>();
                if (proxyRb != null)
                {
                    _cachedTarget = proxyRb.transform;
                    _cachedRb = proxyRb;
                    Debug.Log($"[NetworkedSpaceshipBridge] PROXY: Cached target '{_cachedTarget.name}' + Rigidbody for MovePosition sync");
                }
            }

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
        private float _lastInputDebugTime = 0f;
        private float _lastMovementDebugTime = 0f;
        private float _lastRemoteSyncDebugTime = 0f;
        private bool _loggedFirstTick = false;
        private bool _loggedFirstRpcSend = false;
        private bool _loggedHostGetInput = false;

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

            // === CRITICAL DIAGNOSTIC: Test GetInput on HOST's OWN ship ===
            // If GetInput returns TRUE here, IL weaving works and InputDataWordCount is sufficient.
            // If FALSE, the Fusion input pipeline is fundamentally broken.
            if (Object.HasStateAuthority && Object.HasInputAuthority && !_loggedHostGetInput)
            {
                bool hostGI = GetInput(out PlayerInputData hostData);
                if (hostGI)
                {
                    _loggedHostGetInput = true;
                    Debug.Log($"[NetworkedSpaceshipBridge] *** HOST OWN SHIP GetInput=TRUE *** " +
                              $"magic={hostData.magicNumber}, throttle={hostData.moveZ:F2}, " +
                              $"steer=({hostData.steerPitch:F2},{hostData.steerYaw:F2}) — IL WEAVING WORKS!");
                }
                else if (Time.time - _lastInputDebugTime > 2f)
                {
                    Debug.LogError($"[NetworkedSpaceshipBridge] *** HOST OWN SHIP GetInput=FALSE *** " +
                                   $"— input.Set() may be a no-op! IL weaving or InputDataWordCount issue!");
                }
            }

            // DEBUG: Log position of remote ships we DON'T control (to diagnose host ship not syncing on client)
            // This logs for the host's ship on the client (no state auth, no input auth)
            if (!Object.HasStateAuthority && !Object.HasInputAuthority)
            {
                if (Time.time - _lastRemoteSyncDebugTime > 2f)
                {
                    _lastRemoteSyncDebugTime = Time.time;
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

            // NOTE: Client RPC input sending is now in Update() — see above.
            // FixedUpdateNetwork does NOT run on InputAuthority-only objects in Host Mode
            // (no client-side prediction). Only the State Authority (host) executes FUN.

            if (engines == null)
            {
                if (Time.time - _lastMovementDebugTime > 2f)
                {
                    _lastMovementDebugTime = Time.time;
                    Debug.LogWarning($"[NetworkedSpaceshipBridge] engines is NULL on {gameObject.name}!");
                }
                return;
            }

            // --- HOST: Enforce VehicleInput stays disabled on remote ships ---
            // SCK's EnterVehicle(), Start(), or coroutines can re-enable VehicleInput scripts
            // after our initial DisableLocalInput(). If re-enabled, they read the HOST's mouse
            // and call SetSteeringInputs, causing the client's ship to mirror the host's steering.
            // We enforce this EVERY TICK to guarantee our bridge has exclusive steering control.
            if (Object.HasStateAuthority && !Object.HasInputAuthority)
            {
                var activeInputs = GetComponentsInChildren<VehicleInput>(true);
                foreach (var vi in activeInputs)
                {
                    if (((MonoBehaviour)vi).enabled)
                    {
                        ((MonoBehaviour)vi).enabled = false;
                        Debug.LogWarning($"[NetworkedSpaceshipBridge] HOST: Force-disabled reactivated VehicleInput '{vi.GetType().Name}' on {gameObject.name}!");
                    }
                }
            }

            // --- INPUT ACQUISITION ---
            // CRITICAL: For the client's ship on the HOST, DO NOT use GetInput().
            // In Fusion 2 Host Mode, GetInput() returns the HOST's locally-collected input
            // for ALL ships, not the InputAuthority player's input. This means the host's
            // mouse steer gets applied to the client's ship, causing mirroring.
            //
            // Evidence: When host's mouse is active, client ship mirrors host steer.
            // When host's mouse is stale/centered, client ship stays still.
            // Both players have magicNumber=42, so the earlier "GOT INPUT" was a false positive.
            //
            // For host's OWN ship: GetInput works correctly (host IS the input authority).
            // For client's ship: Use RPC or raw data which carry the ACTUAL client's input.
            PlayerInputData inputData = default;
            bool hasInput = false;
            string inputSource = "none";

            if (Object.HasStateAuthority && !Object.HasInputAuthority)
            {
                // CLIENT'S SHIP ON HOST: Skip GetInput, use RPC/raw data only.
                // Layer 1: RPC-delivered input (sent by client's Update every frame)
                if (_hasRpcInput)
                {
                    inputData = _rpcInput;
                    hasInput = true;
                    inputSource = "RPC";
                }

                // Layer 2: Raw data transport via NetworkManager
                if (!hasInput)
                {
                    var nm = NetworkManager.Instance;
                    if (nm != null && nm.TryGetClientInput(Object.InputAuthority, out var rawInput))
                    {
                        inputData = rawInput;
                        hasInput = true;
                        inputSource = "RAW_DATA";
                    }
                }
            }
            else
            {
                // HOST'S OWN SHIP: GetInput works correctly (host is the input authority)
                hasInput = GetInput(out inputData);
                inputSource = "GetInput";
            }

            // Fallback: host's own ship can also use RPC if GetInput fails
            if (!hasInput && _hasRpcInput)
            {
                inputData = _rpcInput;
                hasInput = true;
                inputSource = "RPC_fallback";
            }

            // Debug input status occasionally
            if (Object.HasStateAuthority && !Object.HasInputAuthority)
            {
                 if (Time.time - _lastInputDebugTime > 2f)
                 {
                     _lastInputDebugTime = Time.time;
                     if (!hasInput)
                     {
                        var nmDiag = NetworkManager.Instance;
                        Debug.LogWarning($"[NetworkedSpaceshipBridge] NO INPUT on {gameObject.name} " +
                                         $"(GetInput=false, RPC={_hasRpcInput}, RAW={nmDiag?.TryGetClientInput(Object.InputAuthority, out _)}), " +
                                         $"InputAuthority={Object.InputAuthority}, frame={Time.frameCount}, " +
                                         $"HOST_RECV: callbacks={NetworkManager.RawRecvAnyCallback}, valid={NetworkManager.RawRecvCount}, " +
                                         $"dictSize={nmDiag?.ClientInputDictSize}");
                     }
                     else
                        Debug.LogWarning($"[NetworkedSpaceshipBridge] *** GOT INPUT *** on {gameObject.name} " +
                                  $"(via {inputSource}), frame={Time.frameCount}: " +
                                  $"Move=({inputData.moveX:F2}, {inputData.moveY:F2}, {inputData.moveZ:F2}), " +
                                  $"Steer=({inputData.steerPitch:F2}, {inputData.steerYaw:F2}, {inputData.steerRoll:F2}), " +
                                  $"Boost={inputData.boost}, Magic={inputData.magicNumber}");
                 }
            }

            if (!hasInput)
            {
                return;
            }

            var data = inputData;

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

                // Reconstruct Vector3s from floats
                Vector3 steering = new Vector3(data.steerPitch, data.steerYaw, data.steerRoll);
                Vector3 movement = new Vector3(data.moveX, data.moveY, data.moveZ);

                // Host processing REMOTE player's movement
                engines.SetSteeringInputs(steering);
                engines.SetMovementInputs(movement);

                _isBoosting = data.boost;
                engines.SetBoostInputs(data.boost ? new Vector3(0f, 0f, 1f) : Vector3.zero);

                // Ensure Rigidbody is dynamic (physics-driven) on the Host
                var rb = GetComponentInChildren<Rigidbody>();
                if (rb != null)
                {
                    if (rb.isKinematic)
                    {
                        rb.isKinematic = false;
                        Debug.LogWarning($"[NetworkedSpaceshipBridge] Forced Rigidbody.isKinematic = false on Host for {gameObject.name}");
                    }
                    
                    // Optional: Check constraints if needed, usually default is fine but just in case
                    // rb.constraints = RigidbodyConstraints.None; 
                }

                // Debug: log every 2 seconds
                if (Time.time - _lastMovementDebugTime > 2f)
                {
                    _lastMovementDebugTime = Time.time;
                    Debug.Log($"[NetworkedSpaceshipBridge] HOST driving remote ship {gameObject.name} (via {inputSource}): " +
                              $"movement={movement}, steering={steering}, boost={data.boost}, " +
                              $"enginesID={engines.GetInstanceID()}, enginesActive={engines.EnginesActivated}, " +
                              $"pos={transform.position}, rb.isKinematic={rb?.isKinematic}, rb.velocity={rb?.linearVelocity}, " +
                              $"magic={data.magicNumber}");
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

        // --- POSITION FOLLOW METHODS ---
        // Position is applied in FixedUpdate using SmoothDamp (critically-damped spring).
        // [DefaultExecutionOrder(-200)] guarantees our FixedUpdate runs before the camera's.
        private bool _lateUpdateProxyFirstLog = false;
        private bool _cameraVerified = false;
        private int _cameraVerifyCount = 0;
        private float _cameraVerifyTimer = 0f;

        /// <summary>
        /// Checks for new ticks and handles first-tick initialization (snap to position).
        /// Returns true if a new tick was received this frame.
        /// </summary>
        private bool CheckNewTick()
        {
            bool tickChanged = (SyncTick != _prevSyncTick);

            if (!_followInitialized && SyncTick > 0)
            {
                // First tick — snap to position, initialize SmoothDamp
                if (_cachedTarget != null)
                {
                    _cachedTarget.position = SyncPosition;
                    _cachedTarget.rotation = SyncRotation;
                    if (_cachedRb != null) _cachedRb.position = SyncPosition;
                }
                _smoothVelocity = Vector3.zero;
                _intendedPosition = SyncPosition;
                _intendedRotation = SyncRotation;
                _followInitialized = true;
                _prevSyncTick = SyncTick;

                if (Runner != null)
                {
                    Debug.Log($"[NetworkedSpaceshipBridge] SmoothDamp follow initialized. Fusion tickRate={Runner.DeltaTime:F4}s ({1f / Runner.DeltaTime:F0}Hz), FixedUpdate={Time.fixedDeltaTime:F4}s, smoothTime={positionSmoothTime:F3}s");
                }
                return false;
            }

            if (tickChanged)
            {
                _prevSyncTick = SyncTick;
            }

            return tickChanged;
        }

        /// <summary>
        /// Moves the ship toward SyncPosition using Vector3.SmoothDamp (critically-damped spring).
        /// SmoothDamp automatically:
        /// - Maintains velocity between frames (no stop-start oscillation)
        /// - Smoothly redirects when the target changes (new tick arrives)
        /// - Never overshoots (critically damped, not underdamped)
        /// - Needs no speed estimation (tracks velocity internally via _smoothVelocity)
        ///
        /// Rotation uses Quaternion.Slerp which gives smooth, constant-rate rotation.
        /// </summary>
        private void ApplyPositionFollow()
        {
            if (!_followInitialized) return;

            // Cache the target transform and Rigidbody on first use
            if (_cachedTarget == null)
            {
                var rb = GetComponentInChildren<Rigidbody>();
                _cachedTarget = rb != null ? rb.transform : transform;
                _cachedRb = rb;
            }

            // Teleport if too far (respawn/initial placement)
            float dist = Vector3.Distance(_cachedTarget.position, SyncPosition);
            if (dist > 50f)
            {
                // Direct write for teleport (MovePosition would interpolate, causing visual slide)
                _cachedTarget.position = SyncPosition;
                _cachedTarget.rotation = SyncRotation;
                if (_cachedRb != null) _cachedRb.position = SyncPosition;
                _smoothVelocity = Vector3.zero;
                _intendedPosition = SyncPosition;
                _intendedRotation = SyncRotation;
                return;
            }

            // SmoothDamp: critically-damped spring toward SyncPosition.
            // Uses _cachedTarget.position as current pos (updated by previous physics step).
            _intendedPosition = Vector3.SmoothDamp(
                _cachedTarget.position,
                SyncPosition,
                ref _smoothVelocity,
                positionSmoothTime,
                Mathf.Infinity,
                Time.fixedDeltaTime
            );

            // Slerp rotation toward SyncRotation
            float rotT = Mathf.Clamp01(rotationSmoothSpeed * Time.fixedDeltaTime);
            _intendedRotation = Quaternion.Slerp(_cachedTarget.rotation, SyncRotation, rotT);

            // Direct transform writes — NOT MovePosition.
            // MovePosition + rb.interpolation caused ship-camera desync: the camera reads
            // transform.position in FixedUpdate (physics pos), but rb.interpolation overrides
            // the rendered position to an interpolated value. With rb.interpolation = None
            // and direct writes, camera and rendering agree on the exact same position.
            _cachedTarget.position = _intendedPosition;
            _cachedTarget.rotation = _intendedRotation;
        }

        // =======================================================================
        // CLIENT-SIDE PREDICTION METHODS
        // =======================================================================

        /// <summary>
        /// Reads local input and feeds it to VehicleEngines3D on the CLIENT's own ship.
        /// This runs at Unity FixedUpdate rate (50Hz) at order -200, BEFORE VehicleEngines3D's
        /// FixedUpdate (order 0). The flow matches the host exactly:
        ///   Bridge sets inputs → VehicleEngines3D.FixedUpdate applies forces → Physics step
        /// </summary>
        private void ApplyLocalInput()
        {
            if (engines == null) return;

            // Ensure engines are active
            if (!engines.EnginesActivated)
            {
                engines.SetEngineActivation(true);
                Debug.Log($"[NetworkedSpaceshipBridge] CLIENT PREDICT: Force-activated engines");
            }
            engines.ControlsDisabled = false;

            // Read local input (same data being sent to host via RPC)
            var nm = NetworkManager.Instance;
            if (nm == null) return;
            var playerInput = nm.GetComponent<NetworkedPlayerInput>();
            if (playerInput == null) return;

            var data = playerInput.CurrentInputData;

            // Build steering vector with linkYawAndRoll
            Vector3 steering = new Vector3(data.steerPitch, data.steerYaw, data.steerRoll);
            if (enableLinkYawAndRoll)
            {
                float linkedRoll = Mathf.Clamp(-steering.y * yawRollRatio, -1f, 1f);
                if (Mathf.Abs(linkedRoll) > Mathf.Abs(steering.z))
                {
                    steering.z = linkedRoll;
                }
            }

            Vector3 movement = new Vector3(data.moveX, data.moveY, data.moveZ);

            // Feed engines — VehicleEngines3D.FixedUpdate (order 0) will apply the forces
            engines.SetSteeringInputs(steering);
            engines.SetMovementInputs(movement);
            engines.SetBoostInputs(data.boost ? new Vector3(0f, 0f, 1f) : Vector3.zero);

            _isBoosting = data.boost;

            // One-time log
            if (!_loggedPredictionActive)
            {
                _loggedPredictionActive = true;
                Debug.Log($"[NetworkedSpaceshipBridge] CLIENT-SIDE PREDICTION ACTIVE: " +
                          $"engines={engines.name}, enginesActivated={engines.EnginesActivated}, " +
                          $"linkYawRoll={enableLinkYawAndRoll}, yawRollRatio={yawRollRatio}, " +
                          $"correctionRate=pos:{positionCorrectionRate}/rot:{rotationCorrectionRate}");
            }
        }

        /// <summary>
        /// Gently corrects the client's physics state toward the host's authoritative
        /// SyncPosition/SyncRotation. This prevents drift while keeping local physics responsive.
        ///
        /// The correction is applied directly to the Rigidbody before VehicleEngines3D
        /// adds its forces, so the physics step integrates everything together.
        /// </summary>
        private void ApplyServerCorrection()
        {
            if (_cachedRb == null || SyncTick <= 0) return;

            float dt = Time.fixedDeltaTime;

            // --- POSITION CORRECTION ---
            float posDist = Vector3.Distance(_cachedRb.position, SyncPosition);

            if (posDist > positionSnapThreshold)
            {
                // Snap — too far away (respawn, teleport, or severe desync)
                _cachedRb.position = SyncPosition;
                _cachedRb.linearVelocity = Vector3.zero;
                Debug.Log($"[NetworkedSpaceshipBridge] CLIENT PREDICT: Position SNAP (gap={posDist:F1}m)");
            }
            else if (posDist > 0.01f)
            {
                // Gentle Lerp toward server position
                float correctionT = Mathf.Clamp01(positionCorrectionRate * dt);
                _cachedRb.position = Vector3.Lerp(_cachedRb.position, SyncPosition, correctionT);
            }

            // --- ROTATION CORRECTION ---
            float rotAngle = Quaternion.Angle(_cachedRb.rotation, SyncRotation);

            if (rotAngle > rotationSnapThreshold)
            {
                // Snap
                _cachedRb.rotation = SyncRotation;
                _cachedRb.angularVelocity = Vector3.zero;
                Debug.Log($"[NetworkedSpaceshipBridge] CLIENT PREDICT: Rotation SNAP (gap={rotAngle:F1}°)");
            }
            else if (rotAngle > 0.1f)
            {
                // Gentle Slerp toward server rotation
                float correctionT = Mathf.Clamp01(rotationCorrectionRate * dt);
                _cachedRb.rotation = Quaternion.Slerp(_cachedRb.rotation, SyncRotation, correctionT);
            }
        }

        /// <summary>
        /// FixedUpdate: Apply position follow at the SAME rate as the camera.
        /// [DefaultExecutionOrder(-200)] on this class guarantees this runs BEFORE:
        ///   - CameraController.FixedUpdate (0) → SpaceFighterCameraController.CameraControllerFixedUpdate
        ///   - CameraEntity.FixedUpdate (0) → CameraControlFixedUpdate (if no controller override)
        /// </summary>
        private void FixedUpdate()
        {
            // Skip if we ARE the host (host physics handled by FUN)
            if (Object.HasStateAuthority) return;

            // === CLIENT'S OWN SHIP: Client-Side Prediction ===
            if (_clientPredictionActive && Object.HasInputAuthority)
            {
                // Feed local input to engines (VehicleEngines3D.FixedUpdate at order 0 applies forces)
                ApplyLocalInput();

                // Gentle correction toward host's authoritative state
                ApplyServerCorrection();

                // Update HUD cursor from reticle position
                UpdateHUDCursorFromReticle();

                return; // Don't run proxy SmoothDamp logic
            }

            // === PROXY SHIPS: SmoothDamp position follow (kinematic) ===
            if (SyncTick > 0)
            {
                // Check for new ticks (handles initialization on first tick)
                bool tickChanged = CheckNewTick();

                // SmoothDamp toward SyncPosition, direct transform writes for camera sync
                ApplyPositionFollow();

                // --- JITTER DIAGNOSTIC ---
                if (_jitterDiagEnabled && Object.HasInputAuthority)
                {
                    if (!_jitterDiagStarted && _followInitialized)
                    {
                        _jitterDiagStarted = true;
                        _jitterDiagStartTime = Time.time;
                        _jitterLastLoggedPos = _intendedPosition;
                        Debug.Log($"[JITTER_DIAG] === STARTING {JITTER_DIAG_DURATION}s DIAGNOSTIC (SmoothDamp+DirectWrite+NoInterp) ===");
                        Debug.Log($"[JITTER_DIAG] smoothTime={positionSmoothTime:F3}, rotSpeed={rotationSmoothSpeed:F1}, fixedDt={Time.fixedDeltaTime:F4}, rb.interp=Interpolate");
                    }

                    if (_jitterDiagStarted && (Time.time - _jitterDiagStartTime) < JITTER_DIAG_DURATION)
                    {
                        _jitterDiagFixedCount++;
                        Vector3 delta = _intendedPosition - _jitterLastLoggedPos;

                        string dirFlag = "";
                        if (_jitterDiagFixedCount > 1 && delta.magnitude > 0.001f)
                        {
                            Vector3 prevDelta = _intendedPosition - _jitterLastLoggedPos;
                            if (Vector3.Dot(delta, prevDelta) < 0)
                            {
                                dirFlag = " <<< DIRECTION REVERSAL";
                            }
                        }

                        if (_jitterDiagFixedCount % 10 == 0 || dirFlag.Length > 0)
                        {
                            float gap = Vector3.Distance(_intendedPosition, SyncPosition);
                            Debug.Log($"[JITTER_DIAG] FU#{_jitterDiagFixedCount} tick={SyncTick} " +
                                      $"tickChanged={tickChanged} velMag={_smoothVelocity.magnitude:F3} " +
                                      $"intended={_intendedPosition:F3} " +
                                      $"delta={delta:F4} mag={delta.magnitude:F4} " +
                                      $"syncPos={SyncPosition:F3} gap={gap:F4}{dirFlag}");
                        }

                        _jitterLastLoggedPos = _intendedPosition;
                    }
                    else if (_jitterDiagStarted && (Time.time - _jitterDiagStartTime) >= JITTER_DIAG_DURATION)
                    {
                        Debug.Log($"[JITTER_DIAG] === DIAGNOSTIC COMPLETE ({_jitterDiagFixedCount} FixedUpdates) ===");
                        _jitterDiagEnabled = false;
                    }
                }
            }
        }

        /// <summary>
        /// Updates the HUD cursor on the client's own ship to match the reticle position
        /// from NetworkedPlayerInput. SCK's PlayerInput normally drives this, but that script
        /// is disabled on the client (our bridge feeds the engines instead).
        /// </summary>
        private void UpdateHUDCursorFromReticle()
        {
            var nm = NetworkManager.Instance;
            if (nm == null) return;
            var playerInput = nm.GetComponent<NetworkedPlayerInput>();
            if (playerInput == null) return;

            var hudCursor = GetComponentInChildren<VSX.Utilities.CustomCursor>(true);
            if (hudCursor != null)
            {
                Vector2 reticle = playerInput.ReticlePosition;
                hudCursor.SetViewportPosition(new Vector3(reticle.x, reticle.y, 0));
            }
        }

        private void LateUpdate()
        {
            // With direct transform writes and rb.interpolation = None, transform.position
            // in LateUpdate is the same as in FixedUpdate — no deferred physics step involved.

            // --- CAMERA VERIFICATION (client's own ship only) ---
            if (Object.HasInputAuthority && !Object.HasStateAuthority)
            {
                _cameraVerifyTimer += Time.deltaTime;
                if (_cameraVerifyCount < 30 && _cameraVerifyTimer > 0.2f)
                {
                    _cameraVerifyTimer = 0f;
                    _cameraVerifyCount++;
                    var vehicleCamera = FindFirstObjectByType<VehicleCamera>();
                    var myVehicle = GetComponentInChildren<Vehicle>(true);
                    if (vehicleCamera != null && myVehicle != null)
                    {
                        if (vehicleCamera.TargetVehicle != myVehicle)
                        {
                            vehicleCamera.SetVehicle(myVehicle);
                            Debug.Log($"[NetworkedSpaceshipBridge] Camera RE-LOCKED to client's own ship: {myVehicle.name} (verify #{_cameraVerifyCount})");
                        }
                        else if (!_cameraVerified)
                        {
                            _cameraVerified = true;
                            Debug.Log($"[NetworkedSpaceshipBridge] Camera confirmed on client's own ship: {myVehicle.name}");
                        }
                    }
                }
            }

            // --- ONE-TIME DIAGNOSTIC for non-authority ships ---
            if (!Object.HasStateAuthority && SyncTick > 0 && !_lateUpdateProxyFirstLog)
            {
                _lateUpdateProxyFirstLog = true;
                var proxyRb = GetComponentInChildren<Rigidbody>();
                string shipType = Object.HasInputAuthority ? "CLIENT OWN SHIP" : "PROXY";
                Debug.Log($"[NetworkedSpaceshipBridge] {shipType} POSITION SYNC ACTIVE on {gameObject.name}: " +
                          $"SyncPosition={SyncPosition}, SyncTick={SyncTick}, " +
                          $"rbChild={proxyRb?.gameObject.name ?? "NULL"}, " +
                          $"rootPos={transform.position}, rbPos={proxyRb?.position}, " +
                          $"smoothVel={_smoothVelocity.magnitude:F2}");
            }

            // NOTE: Position is applied via direct transform writes in FixedUpdate (not MovePosition).
            // rb.interpolation = None ensures the rendered position matches what the camera reads.
        }
    }
}
