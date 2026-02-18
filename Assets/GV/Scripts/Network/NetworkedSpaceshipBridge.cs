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

        // --- SMOOTHDAMP POSITION FOLLOW ---
        // Previous approaches all failed due to Fusion 64Hz ticks vs Unity 50Hz FixedUpdate mismatch:
        //   1. Double-buffer interpolation: t jumped 0→1 in one step (no intermediate values)
        //   2. MoveTowards: stop-start oscillation (speed estimate lags → catches up → stops → repeats)
        //
        // SmoothDamp is critically-damped: it maintains velocity tracking with no overshoot, no stops,
        // and no oscillation. When SyncPosition updates (new tick arrives), SmoothDamp smoothly
        // redirects toward the new target while preserving momentum. No speed estimation needed.
        //
        // VISUAL INTERPOLATION: rb.MovePosition() + rb.interpolation = Interpolate.
        // MovePosition is deferred (doesn't change transform.position until the physics step),
        // but Unity's Rigidbody interpolation smoothly blends the RENDERED position between
        // consecutive MovePosition targets at the display refresh rate (60Hz+).
        // The camera in FixedUpdate reads the PREVIOUS MovePosition result — this is the SAME
        // behavior as the HOST (where camera reads previous physics step position). Both have
        // a one-step camera lag that's absorbed by the camera's own following/damping.
        private Transform _cachedTarget = null; // Cached child Rigidbody transform
        private Rigidbody _cachedRb = null;     // Cached Rigidbody for MovePosition
        private int _prevSyncTick = -1;
        private bool _followInitialized = false;
        private Vector3 _intendedPosition;      // For diagnostics (MovePosition is deferred)
        private Quaternion _intendedRotation;

        // SmoothDamp state
        private Vector3 _smoothVelocity = Vector3.zero;
        // smoothTime = how quickly the ship reaches the target (seconds).
        // 0.1s (~5 Fusion ticks) smooths velocity changes when ship accelerates/decelerates.
        // Too small → velocity changes feel abrupt. Too large → ship lags behind noticeably.
        [Header("Network Position Smoothing")]
        [SerializeField] private float positionSmoothTime = 0.1f;
        [SerializeField] private float rotationSmoothSpeed = 10f; // Slerp speed for rotation

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
                    // Interpolate + MovePosition: MovePosition is deferred (processed in
                    // physics step after all FixedUpdates), but rb.interpolation = Interpolate
                    // makes Unity smoothly blend the RENDERED position between consecutive
                    // MovePosition targets at the display refresh rate. The camera reads the
                    // PREVIOUS MovePosition result in FixedUpdate — this is normal Unity
                    // behavior (HOST camera also reads previous physics step position).
                    rb.interpolation = RigidbodyInterpolation.Interpolate;
                    Debug.Log($"[NetworkedSpaceshipBridge] Made Rigidbody kinematic, interpolation=Interpolate (no state authority)");
                }

                // DISABLE NetworkTransform on non-authority ships.
                // NetworkTransform syncs the ROOT transform position from the host, but in SCK the ROOT
                // is stuck at spawn (physics moves the CHILD). NetworkTransform constantly writes the
                // spawn position to the root, which causes violent shaking when our LateUpdate also
                // moves the child to SyncPosition — the camera oscillates between both positions.
                //
                // [Networked] properties (SyncPosition, SyncRotation, SyncTick) are synced via
                // NetworkObject/NetworkBehaviour's internal state replication, NOT via NetworkTransform.
                // Disabling NetworkTransform does NOT affect [Networked] property sync.
                var nt = GetComponent<Fusion.NetworkTransform>();
                if (nt != null)
                {
                    nt.enabled = false;
                    Debug.Log($"[NetworkedSpaceshipBridge] Disabled NetworkTransform on non-authority ship (prevents shaking)");
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
                // CLIENT's own ship: Disable ALL SCK input/engine scripts safely.
                // The client's ship is a pure visual shell — its position and rotation come
                // ONLY from the HOST via [Networked] SyncPosition/SyncRotation in LateUpdate.
                // Any SCK script that processes local mouse/keyboard or modifies the transform
                // will FIGHT with our network sync and cause violent shaking.
                //
                // We use DisableClientOwnShipScripts() instead of DisableLocalInput() because
                // DisableLocalInput() calls EnterVehicle() and destroys GameAgent, which can
                // cascade and disable THIS component (killing Update() and RPC sending).
                Debug.Log($"[NetworkedSpaceshipBridge] CLIENT local player ship - disabling SCK scripts, position from HOST sync only");
                DisableClientOwnShipScripts();
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
        /// Safely disables SCK scripts on the CLIENT's own ship without the dangerous
        /// cascading side-effects of DisableLocalInput() (which calls EnterVehicle, destroys
        /// GameAgent, etc. and can disable this bridge component).
        ///
        /// The client's own ship is purely visual — position/rotation come from the HOST
        /// via [Networked] SyncPosition/SyncRotation. We just need to stop SCK from:
        /// 1. Processing local mouse/keyboard input (VehicleInput, GeneralInput)
        /// 2. Applying forces or modifying the transform (VehicleEngines3D)
        /// 3. Resetting position to spawn (SetStartAtCheckpoint)
        /// </summary>
        private void DisableClientOwnShipScripts()
        {
            // 1. Disable ALL VehicleInput-derived components (SCK mouse/keyboard input)
            var vehicleInputs = GetComponentsInChildren<VehicleInput>(true);
            foreach (var vi in vehicleInputs)
            {
                ((MonoBehaviour)vi).enabled = false;
                Debug.Log($"[NetworkedSpaceshipBridge] CLIENT: Disabled VehicleInput: {vi.GetType().Name}");
            }

            // 2. Disable ALL GeneralInput components
            var generalInputs = GetComponentsInChildren<VSX.Controls.GeneralInput>(true);
            foreach (var gi in generalInputs)
            {
                ((MonoBehaviour)gi).enabled = false;
                Debug.Log($"[NetworkedSpaceshipBridge] CLIENT: Disabled GeneralInput: {gi.GetType().Name}");
            }

            // 3. Disable Unity PlayerInput (New Input System)
            var unityPlayerInputs = GetComponentsInChildren<UnityEngine.InputSystem.PlayerInput>(true);
            foreach (var upi in unityPlayerInputs)
            {
                upi.enabled = false;
                Debug.Log($"[NetworkedSpaceshipBridge] CLIENT: Disabled Unity PlayerInput on: {upi.gameObject.name}");
            }

            // 4. Disable VehicleEngines3D — no physics forces needed on client
            // (position comes from SyncPosition, Rigidbody is already kinematic)
            if (engines != null)
            {
                engines.enabled = false;
                Debug.Log($"[NetworkedSpaceshipBridge] CLIENT: Disabled VehicleEngines3D: {engines.name}");
            }

            // 5. Destroy SetStartAtCheckpoint — prevents position resets fighting with sync
            var checkpointScripts = GetComponentsInChildren<SetStartAtCheckpoint>(true);
            foreach (var cs in checkpointScripts)
            {
                cs.StopAllCoroutines();
                Destroy(cs);
                Debug.Log($"[NetworkedSpaceshipBridge] CLIENT: Destroyed SetStartAtCheckpoint on own ship");
            }

            // 6. Unregister GameAgent from manager (prevent focus/input routing)
            // but do NOT destroy it or call EnterVehicle (those cause cascading disables)
            var gameAgents = GetComponentsInChildren<GameAgent>(true);
            foreach (var ga in gameAgents)
            {
                if (GameAgentManager.Instance != null)
                {
                    GameAgentManager.Instance.Unregister(ga);
                    Debug.Log($"[NetworkedSpaceshipBridge] CLIENT: Unregistered GameAgent: {ga.name}");
                }
                // Disable but don't destroy — destroying can trigger cascading callbacks
                ga.enabled = false;
            }

            // 7. Cache the child transform (where the Rigidbody lives) for position interpolation.
            // We keep the Rigidbody alive (kinematic) because many SCK scripts depend on it.
            // The camera timing mismatch (camera reads in FixedUpdate, we write in LateUpdate)
            // is solved via [DefaultExecutionOrder(-200)] which makes our FixedUpdate run BEFORE
            // CameraEntity's FixedUpdate, so we set the position before the camera reads it.
            var clientRb = GetComponentInChildren<Rigidbody>();
            if (clientRb != null)
            {
                _cachedTarget = clientRb.transform;
                _cachedRb = clientRb;
                Debug.Log($"[NetworkedSpaceshipBridge] CLIENT: Cached target '{_cachedTarget.name}' + Rigidbody for MovePosition sync");
            }

            // SAFETY: Ensure THIS bridge component is still enabled
            if (!this.enabled)
            {
                this.enabled = true;
                Debug.LogWarning($"[NetworkedSpaceshipBridge] CLIENT: RE-ENABLED self after disabling SCK scripts!");
            }

            Debug.Log($"[NetworkedSpaceshipBridge] CLIENT: All SCK scripts disabled. Ship is now a pure visual shell.");
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

            // Use MovePosition for smooth visual interpolation between FixedUpdate steps.
            // MovePosition is DEFERRED (processed in physics step after all FixedUpdates),
            // but rb.interpolation = Interpolate smoothly blends the rendered position at
            // the display refresh rate (60Hz+), eliminating 50Hz FixedUpdate stutter.
            if (_cachedRb != null)
            {
                _cachedRb.MovePosition(_intendedPosition);
                _cachedRb.MoveRotation(_intendedRotation);
            }
            else
            {
                // Fallback: direct transform writes if no Rigidbody
                _cachedTarget.position = _intendedPosition;
                _cachedTarget.rotation = _intendedRotation;
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
            if (!Object.HasStateAuthority && SyncTick > 0)
            {
                // Check for new ticks (handles initialization on first tick)
                bool tickChanged = CheckNewTick();

                // SmoothDamp toward SyncPosition, then MovePosition for visual interpolation
                ApplyPositionFollow();

                // --- JITTER DIAGNOSTIC ---
                // NOTE: With MovePosition, transform.position is NOT updated until the physics step
                // (after all FixedUpdates). We log _intendedPosition instead (the SmoothDamp result).
                if (_jitterDiagEnabled && Object.HasInputAuthority)
                {
                    if (!_jitterDiagStarted && _followInitialized)
                    {
                        _jitterDiagStarted = true;
                        _jitterDiagStartTime = Time.time;
                        _jitterLastLoggedPos = _intendedPosition;
                        Debug.Log($"[JITTER_DIAG] === STARTING {JITTER_DIAG_DURATION}s DIAGNOSTIC (SmoothDamp+MovePosition+Interpolate) ===");
                        Debug.Log($"[JITTER_DIAG] smoothTime={positionSmoothTime:F3}, rotSpeed={rotationSmoothSpeed:F1}, fixedDt={Time.fixedDeltaTime:F4}, rb.interp=Interpolate");
                    }

                    if (_jitterDiagStarted && (Time.time - _jitterDiagStartTime) < JITTER_DIAG_DURATION)
                    {
                        _jitterDiagFixedCount++;
                        Vector3 delta = _intendedPosition - _jitterLastLoggedPos;

                        // Flag direction reversals (the jitter signature)
                        string dirFlag = "";
                        if (_jitterDiagFixedCount > 1 && delta.magnitude > 0.001f)
                        {
                            Vector3 prevDelta = _intendedPosition - _jitterLastLoggedPos;
                            // Compare with the delta from 2 frames ago
                            if (Vector3.Dot(delta, prevDelta) < 0)
                            {
                                dirFlag = " <<< DIRECTION REVERSAL";
                            }
                        }

                        // Log every 10th frame to reduce spam, but always log reversals
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

        private void LateUpdate()
        {
            // NOTE: With MovePosition + Interpolate, the physics step applies the deferred
            // MovePosition between FixedUpdate and LateUpdate. This means transform.position
            // in LateUpdate WILL differ from FixedUpdate — this is EXPECTED and is how Unity's
            // Rigidbody interpolation works. No LU DRIFT diagnostic needed.

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

            // NOTE: Position is applied via MovePosition in FixedUpdate. The physics step processes
            // it after all FixedUpdates, and rb.interpolation = Interpolate smoothly blends the
            // rendered position between consecutive MovePosition targets at display refresh rate.
            // No additional position writes needed here.
        }
    }
}
