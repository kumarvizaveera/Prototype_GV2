using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using Fusion;
using Fusion.Sockets;
using VSX.CameraSystem;
using VSX.VehicleCombatKits;
using VSX.Vehicles;

namespace GV.Network
{
    /// <summary>
    /// Central manager for Photon Fusion 2 networking.
    /// Handles connection, player spawning, and network callbacks.
    /// </summary>
    [RequireComponent(typeof(NetworkedPlayerInput))]
    public class NetworkManager : MonoBehaviour, INetworkRunnerCallbacks
    {
        public static NetworkManager Instance { get; private set; }
        
        [Header("Configuration")]
        [SerializeField] private NetworkRunner runnerPrefab;
        [SerializeField] private NetworkObject playerPrefab;
        [SerializeField] private int maxPlayers = 4;
        [SerializeField] private LevelSynchronizer levelSynchronizerPrefab;
        
        [Header("Spawn Points")]
        [SerializeField] private Transform[] spawnPoints;
        
        [Header("Debug")]
        [SerializeField] private bool showDebugUI = true;
        
        [Header("Auto Start")]
        [SerializeField] private bool autoHostInBuild = true;
        [SerializeField] private bool autoClientInBuild = false; // Set true when editor hosts, build joins
        [SerializeField] private string fixedRegion = "us"; // Force US region by default
        
        public NetworkRunner Runner { get; private set; }
        public bool IsConnected => Runner != null && Runner.IsRunning;
        
        // Events for UI/game logic
        public event Action<NetworkRunner> OnConnectedEvent;
        public event Action<NetworkRunner, PlayerRef> OnPlayerJoinedGame;
        public event Action<NetworkRunner, PlayerRef> OnPlayerLeftGame;
        public event Action<NetworkRunner, ShutdownReason> OnDisconnectedEvent;
        
        private Dictionary<PlayerRef, NetworkObject> _spawnedPlayers = new Dictionary<PlayerRef, NetworkObject>();
        
        private void Awake()
        {
            Debug.Log($"[NetworkManager] Awake() on instance {GetInstanceID()}, " +
                      $"existing Instance={(Instance != null ? Instance.GetInstanceID().ToString() : "NULL")}, " +
                      $"scene={gameObject.scene.name}");

            if (Instance != null && Instance != this)
            {
                Debug.LogWarning($"[NetworkManager] DUPLICATE detected! Destroying {GetInstanceID()}, keeping {Instance.GetInstanceID()}");
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            Debug.Log($"[NetworkManager] Instance SET to {GetInstanceID()}, DontDestroyOnLoad applied");

            // Critical for local testing on same machine (prevents Host from pausing when alt-tabbed)
            Application.runInBackground = true;
        }

        private void Start()
        {
            if (!Application.isEditor && (autoHostInBuild || autoClientInBuild))
            {
                // Use AutoHostOrClient — Fusion automatically decides:
                //   - If session "GV_Race" doesn't exist yet → becomes Host
                //   - If session "GV_Race" already exists → joins as Client
                // This works regardless of which machine starts first, no Inspector changes needed.
                Debug.Log("[NetworkManager] Auto-starting with AutoHostOrClient (Build)...");
                StartAutoHostOrClient();
            }
        }

        public async void StartAutoHostOrClient()
        {
            Debug.Log("[NetworkManager] Starting as AutoHostOrClient...");
            await StartGame(GameMode.AutoHostOrClient);
        }
        
        // --- RAW DATA INPUT TRANSPORT ---
        // Fusion's OnInput/GetInput pipeline and RPCs both silently fail in our setup.
        // This bypasses both entirely using Runner.SendReliableDataToServer(), which:
        // - Doesn't need IL weaving (unlike RPCs and INetworkInput)
        // - Runs from NetworkManager (always active, never disabled by DisableLocalInput)
        // - Uses a simple byte[] transport that Fusion routes to OnReliableDataReceived on the host
        private static readonly ReliableKey INPUT_DATA_KEY = ReliableKey.FromInts(0x47, 0x56, 0x49, 0x4E); // "GVIN"
        private bool _loggedFirstRawSend = false;
        private float _lastRawSendLogTime = 0f;

        // --- PUBLIC DIAGNOSTICS (visible in build OnGUI via NetworkedPlayerInput) ---
        public int RawSendCount { get; private set; } = 0;
        public int RawSendErrorCount { get; private set; } = 0;
        public string RawSendError { get; private set; } = "";
        public string RawSendErrorStack { get; private set; } = "";
        public bool RawSendAttempted { get; private set; } = false;
        public string RawSendBlockReason { get; private set; } = "Not started";

        // HOST-side diagnostics — STATIC so any instance can write and any instance can read.
        // Fixes dual-instance issue where callbacks fire on one instance but bridge reads another.
        public static int RawRecvCount { get; private set; } = 0;
        public static int RawRecvKeyMismatch { get; private set; } = 0;
        public static int RawRecvTooSmall { get; private set; } = 0;
        public static int RawRecvAnyCallback { get; private set; } = 0;
        public int ClientInputDictSize => _clientInputs.Count;

        // Storage for input received from clients — STATIC shared across all instances.
        private static Dictionary<PlayerRef, PlayerInputData> _clientInputs = new Dictionary<PlayerRef, PlayerInputData>();

        /// <summary>
        /// Get the latest input received from a specific client via raw data transport.
        /// Called by NetworkedSpaceshipBridge.FixedUpdateNetwork() on the host.
        /// </summary>
        private static bool _loggedFirstInputSuccess = false;

        public bool TryGetClientInput(PlayerRef player, out PlayerInputData inputData)
        {
            bool found = _clientInputs.TryGetValue(player, out inputData);

            if (found && !_loggedFirstInputSuccess)
            {
                _loggedFirstInputSuccess = true;
                Debug.LogWarning($"[NetworkManager] *** FIRST CLIENT INPUT FOUND *** player={player}, " +
                                 $"magic={inputData.magicNumber}, frame={Time.frameCount}, " +
                                 $"staticCallbacks={RawRecvAnyCallback}");
            }

            // Diagnostic: if callbacks arrived but lookup fails, log WHY
            if (!found && RawRecvAnyCallback > 0)
            {
                // Static data exists but TryGetValue failed — PlayerRef mismatch!
                if (Time.frameCount % 60 == 0)
                {
                    var keys = string.Join(", ", _clientInputs.Keys);
                    Debug.LogError($"[NetworkManager] TryGetClientInput({player}) FAILED despite " +
                                   $"{RawRecvAnyCallback} callbacks! Dict={_clientInputs.Count} keys=[{keys}], " +
                                   $"frame={Time.frameCount}");
                }
            }

            return found;
        }

        private void Update()
        {
            // Simple keyboard controls for prototype
            if (!IsConnected)
            {
                if (Input.GetKeyDown(KeyCode.H))
                {
                    StartHost();
                }
                else if (Input.GetKeyDown(KeyCode.J)) // Changed to J to avoid conflict with Join
                {
                    StartClient();
                }
                return;
            }

            // --- CLIENT: Send input to host via raw reliable data every frame ---
            // This runs on NetworkManager which is NEVER disabled (unlike ship components).
            // No IL weaving needed, no NetworkBehaviour needed, guaranteed to execute.
            if (Runner == null)
            {
                RawSendBlockReason = "Runner is NULL";
            }
            else if (!Runner.IsClient || Runner.IsServer)
            {
                // CRITICAL: In Host mode, IsClient=true AND IsServer=true.
                // The HOST must NOT send raw data — it would send to itself, polluting
                // the _clientInputs dict with Player:1 (host) instead of Player:2 (client).
                // Only PURE clients (IsClient=true, IsServer=false) should send.
                RawSendBlockReason = $"Not a pure client (IsClient={Runner.IsClient}, IsServer={Runner.IsServer})";
            }
            else if (!Runner.IsConnectedToServer)
            {
                // Wait for full connection — Fusion's internal Simulation.SendReliableData throws
                // NullReferenceException if called before the connection is fully established.
                RawSendBlockReason = "Waiting for server connection...";
            }
            else
            {
                RawSendBlockReason = "OK";
                var playerInput = GetComponent<NetworkedPlayerInput>();
                if (playerInput == null)
                {
                    RawSendBlockReason = "PlayerInput component NULL";
                }
                else
                {
                    var data = playerInput.CurrentInputData;
                    // Encode player ID into magic number (same as OnInput)
                    data.magicNumber = Runner.LocalPlayer.PlayerId * 1000 + 42;
                    byte[] bytes = SerializeInput(data);

                    try
                    {
                        Runner.SendReliableDataToServer(INPUT_DATA_KEY, bytes);
                        RawSendAttempted = true;
                        RawSendCount++;

                        if (!_loggedFirstRawSend)
                        {
                            _loggedFirstRawSend = true;
                            Debug.Log($"[NetworkManager] CLIENT FIRST RAW INPUT SEND: " +
                                      $"throttle={data.moveZ:F2}, steer=({data.steerPitch:F2},{data.steerYaw:F2}), " +
                                      $"magic={data.magicNumber}, bytes={bytes.Length}");
                        }
                    }
                    catch (System.Exception ex)
                    {
                        RawSendErrorCount++;
                        RawSendError = ex.GetType().Name + ": " + ex.Message;
                        RawSendErrorStack = ex.StackTrace ?? "no stack";
                        if (RawSendErrorCount <= 3)
                        {
                            Debug.LogError($"[NetworkManager] SendReliableDataToServer FAILED ({RawSendErrorCount}): {ex}");
                        }
                    }
                }
            }
        }

        // Simple serialization: pack floats into byte array
        private static byte[] SerializeInput(PlayerInputData data)
        {
            // 7 floats (4 bytes each) + 1 bool (1 byte) + 1 int (4 bytes) = 33 bytes
            byte[] bytes = new byte[33];
            int offset = 0;
            WriteFloat(bytes, ref offset, data.moveX);
            WriteFloat(bytes, ref offset, data.moveY);
            WriteFloat(bytes, ref offset, data.moveZ);
            WriteFloat(bytes, ref offset, data.steerPitch);
            WriteFloat(bytes, ref offset, data.steerYaw);
            WriteFloat(bytes, ref offset, data.steerRoll);
            bytes[offset++] = (byte)(data.boost ? 1 : 0);
            WriteInt(bytes, ref offset, data.magicNumber);
            return bytes;
        }

        private static PlayerInputData DeserializeInput(byte[] bytes)
        {
            int offset = 0;
            var data = new PlayerInputData();
            data.moveX = ReadFloat(bytes, ref offset);
            data.moveY = ReadFloat(bytes, ref offset);
            data.moveZ = ReadFloat(bytes, ref offset);
            data.steerPitch = ReadFloat(bytes, ref offset);
            data.steerYaw = ReadFloat(bytes, ref offset);
            data.steerRoll = ReadFloat(bytes, ref offset);
            data.boost = bytes[offset++] != 0;
            data.magicNumber = ReadInt(bytes, ref offset);
            return data;
        }

        private static void WriteFloat(byte[] buf, ref int offset, float val)
        {
            byte[] tmp = System.BitConverter.GetBytes(val);
            System.Buffer.BlockCopy(tmp, 0, buf, offset, 4);
            offset += 4;
        }
        private static float ReadFloat(byte[] buf, ref int offset)
        {
            float val = System.BitConverter.ToSingle(buf, offset);
            offset += 4;
            return val;
        }
        private static void WriteInt(byte[] buf, ref int offset, int val)
        {
            byte[] tmp = System.BitConverter.GetBytes(val);
            System.Buffer.BlockCopy(tmp, 0, buf, offset, 4);
            offset += 4;
        }
        private static int ReadInt(byte[] buf, ref int offset)
        {
            int val = System.BitConverter.ToInt32(buf, offset);
            offset += 4;
            return val;
        }
        
        /// <summary>
        /// Start as Host (Server + Client)
        /// </summary>
        public async void StartHost()
        {
            Debug.Log("[NetworkManager] Starting as Host...");
            await StartGame(GameMode.Host);
        }
        
        /// <summary>
        /// Start as Client and join existing session
        /// </summary>
        public async void StartClient()
        {
            Debug.Log("[NetworkManager] Starting as Client...");
            await StartGame(GameMode.Client);
        }

        /// <summary>
        /// Reset ALL static state. Required because Unity 6 may not reload domains
        /// between play sessions, causing stale static data to persist.
        /// </summary>
        private static void ResetStaticState()
        {
            RawRecvCount = 0;
            RawRecvKeyMismatch = 0;
            RawRecvTooSmall = 0;
            RawRecvAnyCallback = 0;
            _clientInputs.Clear();
            _loggedFirstInputSuccess = false;
            Debug.Log("[NetworkManager] Static state RESET (prevents stale data from previous play session)");
        }

        private async Task StartGame(GameMode mode)
        {
            // Force resolution in standalone builds (ensures UI fits and mouse viewport math is correct)
            if (!Application.isEditor)
            {
                Screen.SetResolution(1920, 1080, FullScreenMode.FullScreenWindow);
                Debug.Log($"[NetworkManager] Forced resolution to 1920x1080 FullScreenWindow");
            }

            // CRITICAL: Reset static data from any previous play session
            ResetStaticState();

            // Create runner if needed
            if (Runner == null)
            {
                if (runnerPrefab != null)
                {
                    Runner = Instantiate(runnerPrefab);
                }
                else
                {
                    var runnerGO = new GameObject("NetworkRunner");
                    Runner = runnerGO.AddComponent<NetworkRunner>();
                }
            }
            
            Runner.ProvideInput = true;

            // Register callbacks BEFORE StartGame — Fusion 2 best practice.
            // If registered after, the runner may tick before callbacks are added,
            // causing OnInput to never fire for clients using AutoHostOrClient.
            // IMPORTANT: Only register NetworkManager as the callback handler.
            // Do NOT register NetworkedPlayerInput separately — having two INetworkRunnerCallbacks
            // with OnInput causes Fusion to call both, and the empty NetworkManager.OnInput()
            // can override/clear the valid input set by NetworkedPlayerInput.OnInput().
            // Instead, NetworkManager.OnInput() delegates to NetworkedPlayerInput.OnInput().
            Runner.AddCallbacks(this);

            // Ensure NetworkedPlayerInput exists (needed for OnInput delegation)
            var playerInput = GetComponent<NetworkedPlayerInput>();
            if (playerInput == null)
            {
                playerInput = gameObject.AddComponent<NetworkedPlayerInput>();
                Debug.Log("[NetworkManager] Created NetworkedPlayerInput component");
            }
            Debug.Log("[NetworkManager] Registered SINGLE callback (NetworkManager) BEFORE StartGame — OnInput delegates to NetworkedPlayerInput");

            var sceneInfo = new NetworkSceneInfo();
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            sceneInfo.AddSceneRef(SceneRef.FromIndex(scene.buildIndex));
            
            // Setup AppSettings with fixed region and fallback AppID
            var appSettings = new Fusion.Photon.Realtime.FusionAppSettings();
            
            string appId = "";
            string appVersion = "1.0";
            
            if (Fusion.Photon.Realtime.PhotonAppSettings.Global != null && 
                Fusion.Photon.Realtime.PhotonAppSettings.Global.AppSettings != null)
            {
                appId = Fusion.Photon.Realtime.PhotonAppSettings.Global.AppSettings.AppIdFusion;
                appVersion = Fusion.Photon.Realtime.PhotonAppSettings.Global.AppSettings.AppVersion;
            }
            
            // Fail-safe: Hardcode the known AppID if retrieval failed
            if (string.IsNullOrEmpty(appId))
            {
                Debug.LogWarning("[NetworkManager] Could not find AppID in Global Settings! Using Fallback.");
                appId = "4f22d424-faf1-48c0-a0f6-ef7db60063dc";
            }
            
            appSettings.AppIdFusion = appId;
            appSettings.AppVersion = appVersion;
            appSettings.FixedRegion = fixedRegion;
            appSettings.UseNameServer = true;
            
            Debug.Log($"[NetworkManager] Starting game. Region: {fixedRegion}, AppID: ...{appId.Substring(Math.Max(0, appId.Length - 4))}");
            _lastError = $"Starting... Region: {fixedRegion}"; // Show status in UI

            var result = await Runner.StartGame(new StartGameArgs
            {
                GameMode = mode,
                SessionName = "GV_Race",
                PlayerCount = maxPlayers,
                Scene = sceneInfo,
                SceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>(),
                CustomPhotonAppSettings = appSettings
            });
            
            // Belt-and-suspenders: re-register callbacks and re-confirm ProvideInput AFTER StartGame.
            // Some Fusion versions clear callbacks during StartGame. Adding again is safe (Fusion deduplicates).
            Runner.AddCallbacks(this);
            Runner.ProvideInput = true;
            Debug.Log($"[NetworkManager] Post-StartGame: re-registered callbacks, ProvideInput={Runner.ProvideInput}, " +
                      $"IsClient={Runner.IsClient}, IsServer={Runner.IsServer}, LocalPlayer={Runner.LocalPlayer}");

            if (result.Ok)
            {
                Debug.Log($"[NetworkManager] Started game as {mode}, LocalPlayer={Runner.LocalPlayer}");
                OnConnectedEvent?.Invoke(Runner);

                if (Runner.IsServer && levelSynchronizerPrefab != null)
                {
                    Runner.Spawn(levelSynchronizerPrefab);
                    Debug.Log("[NetworkManager] Spawned LevelSynchronizer");
                }
            }
            else
            {
                Debug.LogError($"[NetworkManager] Failed to start: {result.ShutdownReason}");
                _lastError = $"Failed: {result.ShutdownReason}";
            }
        }
        
        public void Disconnect()
        {
            if (Runner != null)
            {
                Runner.Shutdown();
            }
        }
        
        // ... (spawn logic omitted for brevity, keeping existing methods) ...
        
        private Vector3 GetSpawnPosition(PlayerRef player)
        {
            if (spawnPoints != null && spawnPoints.Length > 0)
            {
                int index = player.PlayerId % spawnPoints.Length;
                return spawnPoints[index].position;
            }
            return new Vector3(player.PlayerId * 10f, 0f, 0f);
        }
        
        private Quaternion GetSpawnRotation(PlayerRef player)
        {
            if (spawnPoints != null && spawnPoints.Length > 0)
            {
                int index = player.PlayerId % spawnPoints.Length;
                return spawnPoints[index].rotation;
            }
            return Quaternion.identity;
        }

        private void SetupCameraFollow(GameObject playerObject)
        {
            Debug.Log($"[NetworkManager] SetupCameraFollow called for: {playerObject.name}");
            
            // Find the Vehicle component on the spawned player
            var vehicle = playerObject.GetComponentInChildren<Vehicle>(true);
            if (vehicle == null)
            {
                Debug.LogWarning("[NetworkManager] No Vehicle found on spawned player!");
                return;
            }
            
            Debug.Log($"[NetworkManager] Found Vehicle: {vehicle.name}");
            
            // Find the VehicleCamera in the scene (SpaceCombatKit's camera system)
            var vehicleCamera = FindFirstObjectByType<VehicleCamera>();
            if (vehicleCamera != null)
            {
                vehicleCamera.SetVehicle(vehicle);
                Debug.Log($"[NetworkManager] VehicleCamera now following: {vehicle.name}");
                return;
            }
            
            // Fallback: try generic CameraEntity
            var cameraEntity = FindFirstObjectByType<CameraEntity>();
            if (cameraEntity != null)
            {
                var cameraTarget = playerObject.GetComponentInChildren<CameraTarget>(true);
                if (cameraTarget != null)
                {
                    cameraEntity.SetCameraTarget(cameraTarget);
                    Debug.Log($"[NetworkManager] CameraEntity now following: {playerObject.name}");
                    return;
                }
            }
            
            Debug.LogWarning("[NetworkManager] No camera system found in scene!");
        }
        
        #region INetworkRunnerCallbacks
        
        public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
        {
            Debug.Log($"[NetworkManager] Player {player.PlayerId} joined. IsServer: {runner.IsServer}, playerPrefab: {(playerPrefab != null ? playerPrefab.name : "NULL")}");
            
            if (runner.IsServer && playerPrefab != null)
            {
                var spawnPos = GetSpawnPosition(player);
                var spawnRot = GetSpawnRotation(player);

                // Use checkpoint 1 position if available — this matches what SetStartAtCheckpoint does.
                // Without this, the ship spawns at the raw spawnPoints position which may be far from
                // the track. SetStartAtCheckpoint will also teleport, but this ensures the initial
                // Fusion spawn position is already correct (avoids 1-frame flash at wrong position).
                if (CheckpointNetwork.Instance != null && CheckpointNetwork.Instance.Count > 0)
                {
                    var cp = CheckpointNetwork.Instance.GetCheckpoint(1); // 1-based: first checkpoint
                    if (cp != null)
                    {
                        spawnPos = cp.position;
                        spawnRot = cp.rotation;
                        Debug.Log($"[NetworkManager] Using Checkpoint 1 position for spawn: {spawnPos}");
                    }
                }

                Debug.Log($"[NetworkManager] About to spawn player {player.PlayerId} at {spawnPos}");
                
                // Spawn player with explicit input authority assignment
                var playerObject = runner.Spawn(playerPrefab, spawnPos, spawnRot, inputAuthority: player);
                
                Debug.Log($"[NetworkManager] Spawn returned: {(playerObject != null ? playerObject.name : "NULL")}");
                
                // Double-check: explicitly assign input authority after spawn
                if (playerObject != null)
                {
                    playerObject.AssignInputAuthority(player);
                    Debug.Log($"[NetworkManager] Assigned InputAuthority to {player}, now authority is: {playerObject.InputAuthority}");
                    
                    // Setup camera to follow the local player
                    if (player == runner.LocalPlayer)
                    {
                        SetupCameraFollow(playerObject.gameObject);
                    }
                }
                
                _spawnedPlayers[player] = playerObject;
            }
            else
            {
                Debug.LogWarning($"[NetworkManager] NOT spawning - IsServer: {runner.IsServer}, playerPrefab: {(playerPrefab != null ? "assigned" : "NULL")}");
            }
            
            OnPlayerJoinedGame?.Invoke(runner, player);
        }
        
        public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
        {
            Debug.Log($"[NetworkManager] Player {player.PlayerId} left");
            
            if (_spawnedPlayers.TryGetValue(player, out var playerObject))
            {
                if (playerObject != null) runner.Despawn(playerObject);
                _spawnedPlayers.Remove(player);
            }
            
            OnPlayerLeftGame?.Invoke(runner, player);
        }
        
        public void OnInput(NetworkRunner runner, NetworkInput input)
        {
            // CRITICAL: We must call input.Set() DIRECTLY in this registered callback.
            // NetworkInput is a STRUCT — if we delegate to playerInput.OnInput(runner, input),
            // that method receives a COPY of the struct. Calling Set() on the copy modifies
            // nothing that Fusion can see. The input data silently vanishes.
            //
            // Instead, NetworkedPlayerInput collects input in Update() and exposes it via
            // CurrentInputData. We read it here and call Set() directly.
            var playerInput = GetComponent<NetworkedPlayerInput>();
            if (playerInput != null)
            {
                var data = playerInput.CurrentInputData;
                // Encode player ID into magic number so we can distinguish
                // host input (magic=1042) from client input (magic=2042) on the receiving end.
                data.magicNumber = Runner.LocalPlayer.PlayerId * 1000 + 42;
                input.Set(data);
                playerInput.NotifyInputConsumed(); // Reset one-shot buttons & update diagnostics

                if (Time.frameCount % 300 == 0)
                {
                    Debug.Log($"[NetworkManager] OnInput SET: throttle={data.moveZ:F2}, " +
                              $"steer=({data.steerPitch:F2},{data.steerYaw:F2}), magic={data.magicNumber}, " +
                              $"LocalPlayer={Runner.LocalPlayer}");
                }
            }
        }
        public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
        
        public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
        {
            Debug.Log($"[NetworkManager] Shutdown: {shutdownReason}");
            _lastError = $"Shutdown: {shutdownReason}";
            OnDisconnectedEvent?.Invoke(runner, shutdownReason);
            _spawnedPlayers.Clear();
            Runner = null;
        }

        public void OnConnectedToServer(NetworkRunner runner) 
        {
            Debug.Log($"[NetworkManager] Connected to server. Region: {runner.SessionInfo.Region}");
            _currentRegion = runner.SessionInfo.Region;
        }
        public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) { }
        public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { request.Accept(); }
        public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) 
        {
            _lastError = $"Connect Failed: {reason}";
        }
        public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
        public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
        public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
        public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
        public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data)
        {
            RawRecvAnyCallback++;

            // Log ALL reliable data received (first few times)
            if (RawRecvAnyCallback <= 5)
            {
                Debug.Log($"[NetworkManager] OnReliableDataReceived #{RawRecvAnyCallback}: " +
                          $"player={player}, dataLen={data.Count}, key={key}, " +
                          $"thisID={GetInstanceID()}, Instance.ID={(Instance != null ? Instance.GetInstanceID().ToString() : "NULL")}, " +
                          $"sameInstance={Instance == this}");
            }

            // Accept any reliable data with the right size (skip key comparison — ReliableKey == may not work)
            if (data.Count >= 33)
            {
                byte[] bytes = new byte[data.Count];
                System.Buffer.BlockCopy(data.Array, data.Offset, bytes, 0, data.Count);
                var inputData = DeserializeInput(bytes);

                // Validate: magic number is now playerID*1000+42 (e.g., 1042, 2042)
                // Accept any value ending in 42 to confirm it's our input data
                if (inputData.magicNumber % 1000 == 42)
                {
                    _clientInputs[player] = inputData;
                    RawRecvCount++;

                    // Log dual-instance detection (informational — static dict handles it)
                    if (Instance != null && Instance != this && RawRecvCount <= 3)
                    {
                        Debug.LogWarning($"[NetworkManager] DUAL INSTANCE detected: Callback on {GetInstanceID()} " +
                                         $"but Instance is {Instance.GetInstanceID()}. Static dict handles this.");
                    }

                    if (Time.time - _lastRawSendLogTime > 3f)
                    {
                        _lastRawSendLogTime = Time.time;
                        Debug.Log($"[NetworkManager] HOST RECEIVED RAW INPUT from {player}: " +
                                  $"throttle={inputData.moveZ:F2}, steer=({inputData.steerPitch:F2},{inputData.steerYaw:F2}), " +
                                  $"magic={inputData.magicNumber}, recvTotal={RawRecvCount}");
                    }
                }
                else
                {
                    RawRecvKeyMismatch++;
                }
            }
            else
            {
                RawRecvTooSmall++;
            }
        }
        public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
        public void OnSceneLoadDone(NetworkRunner runner) { }
        public void OnSceneLoadStart(NetworkRunner runner) { }
        public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
        public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
        
        #endregion
        
        private string _lastError = "";
        private string _currentRegion = "Unknown";
        
        private void OnGUI()
        {
            if (!showDebugUI) return;
            
            GUILayout.BeginArea(new Rect(10, 10, 300, 250));
            GUILayout.BeginVertical("box");
            
            GUILayout.Label($"Region: {_currentRegion}");
            
            if (Runner != null)
            {
                 GUILayout.Label($"State: {Runner.State}");
                 var globalSettings = Fusion.Photon.Realtime.PhotonAppSettings.Global.AppSettings;
                 if (globalSettings != null && !string.IsNullOrEmpty(globalSettings.AppIdFusion))
                    GUILayout.Label($"AppID: ...{(globalSettings.AppIdFusion.Length > 4 ? globalSettings.AppIdFusion.Substring(globalSettings.AppIdFusion.Length - 4) : "???")}");
            }

            if (!string.IsNullOrEmpty(_lastError))
            {
                GUI.color = Color.yellow;
                GUILayout.Label($"Status: {_lastError}");
                GUI.color = Color.white;
            }

            if (!IsConnected)
            {
                GUILayout.Label("Photon Fusion 2");
                GUILayout.Label("Press H to Host");
                GUILayout.Label("Press J to Join");

                if (GUILayout.Button("Host Game")) StartHost();
                if (GUILayout.Button("Join Game")) StartClient();
            }
            else
            {
                GUILayout.Label($"Connected as {(Runner.IsServer ? "Host" : "Client")}");
                GUILayout.Label($"Players: {Runner.ActivePlayers.Count()}");
                GUILayout.Label($"Session: {Runner.SessionInfo.Name}");

                // HOST-side: show raw data receive stats
                if (Runner.IsServer)
                {
                    GUI.color = RawRecvCount > 0 ? Color.green : Color.red;
                    GUILayout.Label($"RAW RECV: {RawRecvCount} (callbacks={RawRecvAnyCallback})");
                    GUILayout.Label($"ClientInputDict: {ClientInputDictSize} entries");
                    if (RawRecvKeyMismatch > 0) GUILayout.Label($"KeyMismatch: {RawRecvKeyMismatch}");
                    if (RawRecvTooSmall > 0) GUILayout.Label($"TooSmall: {RawRecvTooSmall}");
                    GUI.color = Color.white;
                }

                // CLIENT-side: show raw send stats
                if (Runner.IsClient)
                {
                    GUI.color = RawSendCount > 0 ? Color.green : Color.red;
                    GUILayout.Label($"RAW SEND: {RawSendCount} (errs={RawSendErrorCount})");
                    GUI.color = Color.white;
                }

                if (GUILayout.Button("Disconnect")) Disconnect();
            }
            
            GUILayout.EndVertical();
            GUILayout.EndArea();
        }
    }
}
