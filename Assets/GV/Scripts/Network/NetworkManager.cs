using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Fusion;
using Fusion.Sockets;
using VSX.CameraSystem;
using VSX.VehicleCombatKits;
using VSX.Vehicles;
using UnityEngine.Splines;
using Unity.Mathematics;

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

        [Header("Spline Spawn")]
        [Tooltip("The track spline to spawn players along. Same one used by SplineTether.")]
        [SerializeField] private SplineContainer spawnSpline;

        [Tooltip("Name of the spline GameObject to auto-find in gameplay scene (if spawnSpline is null).")]
        [SerializeField] private string spawnSplineName = "Spline_2";

        [Tooltip("Minimum world-space distance between any two players along the spline.")]
        [SerializeField] private float minSpawnSpacing = 50f;

        [Header("Room UI — Lobby (before connecting)")]
        [SerializeField] private GameObject lobbyPanel;          // Parent panel: show before connected, hide after
        [SerializeField] private Button createRoomButton;        // "Create Room" button
        [SerializeField] private Button joinRoomButton;          // "Join Room" button
        [SerializeField] private TMP_InputField roomCodeInput;   // Text field where joiner types the room code
        [SerializeField] private TMP_Text statusText;            // "Creating...", "Joining...", errors

        [Header("Room UI — Connected (after connecting)")]
        [SerializeField] private GameObject connectedPanel;      // Parent panel: show after connected, hide before
        [SerializeField] private TMP_Text roomCodeDisplayText;   // "Room: A3K9"
        [SerializeField] private TMP_Text playerCountText;       // "Players: 2/4"
        [SerializeField] private TMP_Text clientJoinedText;      // "Client has joined" notification
        [SerializeField] private Button disconnectButton;        // Disconnect button
        [SerializeField] private Button enterBattleButton;       // "Enter Battle" — loads gameplay scene

        [Header("Debug")]
        [SerializeField] private bool showDebugUI = true;

        [Header("Server Mode")]
        [Tooltip("Auto = normal game client (detects -server flag for VPS).\nDedicatedServer = headless server, no local player.")]
        [SerializeField] private ServerMode serverMode = ServerMode.Auto;

        [Header("Auto Start")]
        [SerializeField] private bool autoHostInBuild = true;
        [SerializeField] private bool autoClientInBuild = false; // Set true when editor hosts, build joins
        [SerializeField] private string fixedRegion = "us"; // Force US region by default

        [Header("Scene Loading")]
        [Tooltip("The gameplay scene to load after creating/joining a room. Leave empty to stay in current scene.")]
        [SerializeField] private string gameplaySceneName = "";

        /// <summary>
        /// Auto = normal game client. If the -server command-line flag or UNITY_SERVER define is detected, runs as dedicated server.
        /// DedicatedServer = forces headless server mode (no camera, no UI, no local player).
        /// </summary>
        public enum ServerMode { Auto, DedicatedServer }

        /// <summary>
        /// True when running as a dedicated server (no local player, no rendering).
        /// Other scripts check this to skip camera/UI/input code.
        /// </summary>
        public bool IsDedicatedServer { get; private set; }

        /// <summary>
        /// The room code for the current session (e.g. "A3K9").
        /// Set when creating a room, or when joining one.
        /// </summary>
        public string CurrentRoomCode { get; private set; } = "";

        public NetworkRunner Runner { get; private set; }
        public bool IsConnected => Runner != null && Runner.IsRunning;
        
        // Events for UI/game logic
        public event Action<NetworkRunner> OnConnectedEvent;
        public event Action<NetworkRunner, PlayerRef> OnPlayerJoinedGame;
        public event Action<NetworkRunner, PlayerRef> OnPlayerLeftGame;
        public event Action<NetworkRunner, ShutdownReason> OnDisconnectedEvent;
        
        private Dictionary<PlayerRef, NetworkObject> _spawnedPlayers = new Dictionary<PlayerRef, NetworkObject>();
        private List<PlayerRef> _pendingSpawns = new List<PlayerRef>();

        /// <summary>
        /// True once we're in the gameplay scene and ready to spawn players.
        /// Prevents ships from spawning in the menu/lobby scene.
        /// </summary>
        private bool _inGameplayScene = false;
        
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

            // Subscribe to Unity's scene loaded event as backup
            // (Fusion's OnSceneLoadDone may not fire when we load scenes manually)
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnUnitySceneLoaded;

            // --- Determine if this instance should run as a dedicated server ---
            // ParrelSync clones share the same scene data, so they'd inherit the
            // DedicatedServer Inspector setting. Clones must always run as regular clients.
            bool isParrelSyncClone = false;
            #if UNITY_EDITOR
            isParrelSyncClone = ParrelSync.ClonesManager.IsClone();
            #endif

            IsDedicatedServer = false;
            if (isParrelSyncClone)
            {
                // ParrelSync clone = always a client, never a server
                IsDedicatedServer = false;
                Debug.Log("[NetworkManager] ParrelSync clone detected — forcing Host/Client mode (not dedicated server)");
            }
            else
            {
                switch (serverMode)
                {
                    case ServerMode.DedicatedServer:
                        IsDedicatedServer = true;
                        break;
                    case ServerMode.Auto:
                    default:
                        // Check command-line args for -server flag
                        string[] args = System.Environment.GetCommandLineArgs();
                        foreach (string arg in args)
                        {
                            if (arg.ToLower() == "-server" || arg.ToLower() == "--server")
                            {
                                IsDedicatedServer = true;
                                break;
                            }
                        }
                        // Also check Unity's UNITY_SERVER define (set automatically in Dedicated Server builds)
                        #if UNITY_SERVER
                        IsDedicatedServer = true;
                        #endif
                        break;
                }
            }
            Debug.Log($"[NetworkManager] ServerMode={serverMode}, IsDedicatedServer={IsDedicatedServer}, ParrelSyncClone={isParrelSyncClone}");
        }

        private void Start()
        {
            // --- DEDICATED SERVER: skip all UI, auto-start as Server ---
            if (IsDedicatedServer)
            {
                _inGameplayScene = true; // Server is always in gameplay scene
                Debug.Log("[NetworkManager] Dedicated Server mode — skipping UI, auto-starting as Server...");
                StartServer();
                return;
            }

            // --- Setup Room UI ---
            // Hide lobby panel on start — WalletConnectPanel will show it after wallet connects
            if (lobbyPanel != null) lobbyPanel.SetActive(false);
            if (connectedPanel != null) connectedPanel.SetActive(false);

            // Wire up button clicks
            if (createRoomButton != null)
                createRoomButton.onClick.AddListener(OnCreateRoomClicked);
            if (joinRoomButton != null)
                joinRoomButton.onClick.AddListener(OnJoinRoomClicked);
            if (disconnectButton != null)
                disconnectButton.onClick.AddListener(Disconnect);
            if (enterBattleButton != null)
            {
                enterBattleButton.onClick.AddListener(LoadGameplay);
                enterBattleButton.gameObject.SetActive(false); // Show only after room connects
            }

            if (clientJoinedText != null)
                clientJoinedText.gameObject.SetActive(false);

            if (!Application.isEditor && (autoHostInBuild || autoClientInBuild))
            {
                Debug.Log("[NetworkManager] Auto-starting with AutoHostOrClient (Build)...");
                StartAutoHostOrClient();
            }
        }

        // ── Room UI helpers ──────────────────────────────────────────

        public void ShowLobbyUI()
        {
            Debug.Log($"[NetworkManager] ShowLobbyUI called. " +
                      $"lobbyPanel={(lobbyPanel != null ? "assigned" : "NULL")}, " +
                      $"createBtn={(createRoomButton != null ? "assigned" : "NULL")}, " +
                      $"joinBtn={(joinRoomButton != null ? "assigned" : "NULL")}");

            if (lobbyPanel != null) lobbyPanel.SetActive(true);
            if (connectedPanel != null) connectedPanel.SetActive(false);
            if (statusText != null) statusText.text = "";
            if (roomCodeInput != null) roomCodeInput.text = "";

            // Re-enable buttons in case they were disabled during a previous attempt
            if (createRoomButton != null) createRoomButton.interactable = true;
            if (joinRoomButton != null) joinRoomButton.interactable = true;
        }

        private void ShowConnectedUI()
        {
            if (lobbyPanel != null) lobbyPanel.SetActive(false);
            if (connectedPanel != null) connectedPanel.SetActive(true);
            if (roomCodeDisplayText != null) roomCodeDisplayText.text = $"Room: {CurrentRoomCode}";

            // Only the HOST sees "Enter Battle" — clients wait for the host to start.
            // Fusion's NetworkSceneManagerDefault syncs the scene change to clients automatically.
            bool isHost = Runner != null && Runner.IsServer;
            if (enterBattleButton != null) enterBattleButton.gameObject.SetActive(isHost);

            if (!isHost && statusText != null)
            {
                statusText.gameObject.SetActive(true);
                statusText.text = "Waiting for host to start...";
            }
        }

        /// <summary>
        /// Loads the gameplay scene. Called by "Enter Battle" button.
        /// ONLY the HOST/SERVER actually loads the scene — Fusion's NetworkSceneManagerDefault
        /// automatically syncs the scene change to all connected clients.
        /// If a CLIENT clicks this, it shows a waiting message (the host hasn't started yet).
        /// </summary>
        public void LoadGameplay()
        {
            if (string.IsNullOrEmpty(gameplaySceneName))
            {
                Debug.LogError("[NetworkManager] gameplaySceneName is empty! Set it in the Inspector.");
                return;
            }

            if (Runner == null)
            {
                Debug.LogError("[NetworkManager] Runner is null — not connected!");
                return;
            }

            if (Runner.IsServer)
            {
                // HOST: Load the scene. NetworkSceneManagerDefault will sync to all clients.
                Debug.Log($"[NetworkManager] HOST loading gameplay scene: {gameplaySceneName}");
                UnityEngine.SceneManagement.SceneManager.LoadScene(gameplaySceneName);
            }
            else
            {
                // CLIENT: Don't load manually — Fusion will sync the scene from the host.
                Debug.Log("[NetworkManager] CLIENT: waiting for host to start the game...");
                if (statusText != null)
                {
                    statusText.gameObject.SetActive(true);
                    statusText.text = "Waiting for host to start...";
                }
            }
        }

        // ── Room Code Generation ─────────────────────────────────────

        /// <summary>
        /// Generates a random 4-character room code using uppercase letters and digits.
        /// Excludes confusing characters: O, 0, I, 1, L to avoid mix-ups when sharing verbally.
        /// </summary>
        private string GenerateRoomCode()
        {
            const string chars = "ABCDEFGHJKMNPQRSTUVWXYZ23456789"; // no O,0,I,1,L
            char[] code = new char[4];
            for (int i = 0; i < 4; i++)
                code[i] = chars[UnityEngine.Random.Range(0, chars.Length)];
            return new string(code);
        }

        // ── Button Handlers ──────────────────────────────────────────

        /// <summary>
        /// Called when player clicks "Create Room."
        /// Generates a room code and starts as Host with that code as the Fusion session name.
        /// </summary>
        public void OnCreateRoomClicked()
        {
            CurrentRoomCode = GenerateRoomCode();
            Debug.Log($"[NetworkManager] Creating room with code: {CurrentRoomCode}");
            if (statusText != null) statusText.text = "Creating room...";
            if (createRoomButton != null) createRoomButton.interactable = false;
            if (joinRoomButton != null) joinRoomButton.interactable = false;
            StartHost();
        }

        /// <summary>
        /// Called when player clicks "Join Room."
        /// Reads the code from the input field and joins that Fusion session.
        /// </summary>
        public void OnJoinRoomClicked()
        {
            if (roomCodeInput == null) return;

            string code = roomCodeInput.text.Trim().ToUpper();
            if (code.Length != 4)
            {
                if (statusText != null) statusText.text = "Enter a 4-character room code";
                return;
            }

            CurrentRoomCode = code;
            Debug.Log($"[NetworkManager] Joining room with code: {CurrentRoomCode}");
            if (statusText != null) statusText.text = "Joining room...";
            if (createRoomButton != null) createRoomButton.interactable = false;
            if (joinRoomButton != null) joinRoomButton.interactable = false;
            StartClient();
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
            // --- DEDICATED SERVER: no UI, no keyboard input, no raw send ---
            // The server only receives data (OnReliableDataReceived), never sends.
            if (IsDedicatedServer) return;

            if (!IsConnected)
            {
                return; // Lobby UI is button-driven, nothing to poll here
            }
            else
            {
                // Update player count on the connected panel
                if (playerCountText != null)
                {
                    playerCountText.text = $"Players: {Runner.ActivePlayers.Count()}/{maxPlayers}";
                }
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
            // 7 floats (4 bytes each) + 1 bool (1 byte) + 1 int (4 bytes) + 1 int for buttons (4 bytes) = 37 bytes
            byte[] bytes = new byte[37];
            int offset = 0;
            WriteFloat(bytes, ref offset, data.moveX);
            WriteFloat(bytes, ref offset, data.moveY);
            WriteFloat(bytes, ref offset, data.moveZ);
            WriteFloat(bytes, ref offset, data.steerPitch);
            WriteFloat(bytes, ref offset, data.steerYaw);
            WriteFloat(bytes, ref offset, data.steerRoll);
            bytes[offset++] = (byte)(data.boost ? 1 : 0);
            WriteInt(bytes, ref offset, data.buttons.Bits);
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
            
            int buttonsBits = ReadInt(bytes, ref offset);
            data.buttons = default;
            for (int i = 0; i < 32; i++)
            {
                data.buttons.Set(i, (buttonsBits & (1 << i)) != 0);
            }

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
        /// Start as Dedicated Server — no local player, no camera, no UI.
        /// Uses Fusion's GameMode.Server which creates a server-only runner.
        /// All players connect as clients; no one has "host advantage."
        /// </summary>
        public async void StartServer()
        {
            Debug.Log("[NetworkManager] Starting as DEDICATED SERVER...");
            await StartGame(GameMode.Server);
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
            // Skip on dedicated server — there's no screen to set resolution on.
            if (!Application.isEditor && !IsDedicatedServer)
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
            
            // Dedicated server has no local player, so no input to provide.
            // Host/Client modes need ProvideInput so Fusion calls OnInput().
            Runner.ProvideInput = !IsDedicatedServer;

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
            Debug.Log($"[NetworkManager] Active scene: '{scene.name}', buildIndex={scene.buildIndex}, isLoaded={scene.isLoaded}");
            if (scene.buildIndex < 0)
            {
                Debug.LogError("[NetworkManager] WARNING: Scene buildIndex is negative! This scene may not be in Build Settings.");
            }
            sceneInfo.AddSceneRef(SceneRef.FromIndex(scene.buildIndex));
            
            // Setup AppSettings with fixed region and fallback AppID
            var appSettings = new Fusion.Photon.Realtime.FusionAppSettings();
            
            string appId = "";
            string appVersion = "1.0";
            
            bool globalSettingsFound = Fusion.Photon.Realtime.PhotonAppSettings.Global != null &&
                Fusion.Photon.Realtime.PhotonAppSettings.Global.AppSettings != null;
            Debug.Log($"[NetworkManager] PhotonAppSettings.Global found: {globalSettingsFound}");

            if (globalSettingsFound)
            {
                appId = Fusion.Photon.Realtime.PhotonAppSettings.Global.AppSettings.AppIdFusion;
                appVersion = Fusion.Photon.Realtime.PhotonAppSettings.Global.AppSettings.AppVersion;
                Debug.Log($"[NetworkManager] Loaded from Global: AppId length={appId?.Length ?? 0}, AppVersion='{appVersion}'");
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
            
            // Use room code as session name. Dedicated server and AutoHostOrClient fall back to "GV_Race".
            string sessionName = !string.IsNullOrEmpty(CurrentRoomCode) ? CurrentRoomCode : "GV_Race";

            Debug.Log($"[NetworkManager] Starting game. Mode={mode}, Region={fixedRegion}, " +
                      $"Session={sessionName}, MaxPlayers={maxPlayers}, " +
                      $"AppID=...{appId.Substring(Math.Max(0, appId.Length - 4))}, " +
                      $"AppVersion='{appVersion}', UseNameServer={appSettings.UseNameServer}");
            _lastError = $"Starting... Region: {fixedRegion}"; // Show status in UI

            StartGameResult result;
            try
            {
                result = await Runner.StartGame(new StartGameArgs
                {
                    GameMode = mode,
                    SessionName = sessionName,
                    PlayerCount = maxPlayers,
                    Scene = sceneInfo,
                    SceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>(),
                    CustomPhotonAppSettings = appSettings
                });
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[NetworkManager] EXCEPTION during StartGame: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
                _lastError = $"Exception: {ex.Message}";
                return;
            }
            Debug.Log($"[NetworkManager] StartGame returned: Ok={result.Ok}, ShutdownReason={result.ShutdownReason}");
            
            // Belt-and-suspenders: re-register callbacks and re-confirm ProvideInput AFTER StartGame.
            // Some Fusion versions clear callbacks during StartGame. Adding again is safe (Fusion deduplicates).
            // Runner can be null if StartGame failed and triggered a shutdown.
            if (Runner == null)
            {
                Debug.LogError($"[NetworkManager] Runner is null after StartGame — connection failed. ShutdownReason={result.ShutdownReason}");
                _lastError = $"Failed: {result.ShutdownReason}";
                return;
            }
            Runner.AddCallbacks(this);
            Runner.ProvideInput = !IsDedicatedServer;
            Debug.Log($"[NetworkManager] Post-StartGame: re-registered callbacks, ProvideInput={Runner.ProvideInput}, " +
                      $"IsClient={Runner.IsClient}, IsServer={Runner.IsServer}, LocalPlayer={Runner.LocalPlayer}");

            if (result.Ok)
            {
                Debug.Log($"[NetworkManager] Started game as {mode}, LocalPlayer={Runner.LocalPlayer}, Session={sessionName}");
                OnConnectedEvent?.Invoke(Runner);

                // Switch from lobby UI to connected UI
                if (!IsDedicatedServer)
                    ShowConnectedUI();

                if (Runner.IsServer && levelSynchronizerPrefab != null)
                {
                    Runner.Spawn(levelSynchronizerPrefab);
                    Debug.Log("[NetworkManager] Spawned LevelSynchronizer");
                }

                // Don't auto-load gameplay scene here — player clicks "Enter Battle" to proceed.
                // This gives time for other players to join the room first.
            }
            else
            {
                Debug.LogError($"[NetworkManager] Failed to start: {result.ShutdownReason}");
                _lastError = $"Failed: {result.ShutdownReason}";

                // Go back to lobby so player can try again
                if (!IsDedicatedServer)
                {
                    ShowLobbyUI();
                    if (statusText != null) statusText.text = $"Failed: {result.ShutdownReason}";
                }
            }
        }
        
        public void Disconnect()
        {
            if (Runner != null)
            {
                Runner.Shutdown();
            }
        }

        private void OnDestroy()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnUnitySceneLoaded;
        }

        /// <summary>
        /// Backup scene load handler — Fusion's OnSceneLoadDone may not fire
        /// when we load scenes manually with SceneManager.LoadScene().
        /// </summary>
        private void OnUnitySceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            Debug.Log($"[NetworkManager] Unity sceneLoaded — scene: {scene.name}, " +
                      $"gameplaySceneName: {gameplaySceneName}, pending: {_pendingSpawns.Count}");

            if (!string.IsNullOrEmpty(gameplaySceneName) && scene.name == gameplaySceneName)
            {
                _inGameplayScene = true;

                // Auto-find the spawn spline in the newly loaded gameplay scene
                TryFindSpawnSpline();

                // Delay spawning by 1 frame so all scene objects (VehicleCamera, etc.)
                // have their Start() called. sceneLoaded fires after Awake() but before Start().
                if (Runner != null && Runner.IsServer)
                {
                    StartCoroutine(SpawnPendingPlayersNextFrame());
                }
            }
        }

        /// <summary>
        /// Waits one frame after scene load, then spawns all players.
        /// This ensures VehicleCamera and other scene objects have their Start() called,
        /// so SetupCamera() in NetworkedSpaceshipBridge can find and use them.
        /// Also re-collects all active players (not just _pendingSpawns) to handle
        /// scene re-entry and second attempts where OnPlayerJoined won't fire again.
        /// </summary>
        private IEnumerator SpawnPendingPlayersNextFrame()
        {
            yield return null; // Wait one frame for scene Start() calls

            if (Runner == null || !Runner.IsServer)
                yield break;

            // Collect ALL active players that need spawning — not just _pendingSpawns.
            // This handles the case where a player was already "joined" (OnPlayerJoined won't
            // fire again) but their ship was destroyed by a scene reload.
            var playersToSpawn = new List<PlayerRef>();

            // First add any pending spawns from OnPlayerJoined
            foreach (var player in _pendingSpawns)
            {
                if (!playersToSpawn.Contains(player))
                    playersToSpawn.Add(player);
            }
            _pendingSpawns.Clear();

            // Then add any active players who don't have a spawned ship
            foreach (var player in Runner.ActivePlayers)
            {
                if (!_spawnedPlayers.ContainsKey(player) && !playersToSpawn.Contains(player))
                {
                    Debug.Log($"[NetworkManager] Active player {player.PlayerId} has no ship — adding to spawn list");
                    playersToSpawn.Add(player);
                }
                else if (_spawnedPlayers.ContainsKey(player) && _spawnedPlayers[player] == null)
                {
                    // Ship reference went null (destroyed by scene load) — respawn
                    Debug.Log($"[NetworkManager] Player {player.PlayerId}'s ship was destroyed — re-spawning");
                    _spawnedPlayers.Remove(player);
                    playersToSpawn.Add(player);
                }
            }

            Debug.Log($"[NetworkManager] Spawning {playersToSpawn.Count} players after 1-frame delay");
            foreach (var player in playersToSpawn)
            {
                SpawnPlayer(Runner, player);
            }
        }
        
        // ... (spawn logic omitted for brevity, keeping existing methods) ...
        
        // ── Spline-based spawn helpers ──────────────────────────────────

        /// <summary>
        /// Searches the current scene for a SplineContainer on a GameObject matching spawnSplineName.
        /// Called when the gameplay scene loads, since the NetworkManager persists via DontDestroyOnLoad
        /// and can't hold a direct Inspector reference to objects in other scenes.
        /// </summary>
        private void TryFindSpawnSpline()
        {
            // Clear cached positions — scene changed, so old positions are invalid
            _cachedSpawnPositions = null;
            _cachedSpawnRotations = null;

            if (string.IsNullOrEmpty(spawnSplineName))
            {
                Debug.LogWarning("[NetworkManager] spawnSplineName is empty — can't auto-find spline.");
                return;
            }

            // ALWAYS search for the scene instance of the spline by name.
            // The prefab's spawnSpline field may point to the Spline_2 PREFAB ASSET
            // (transform at origin) instead of the SCENE INSTANCE (correct world position).
            // A prefab reference survives scene loads but gives wrong world-space positions.
            var splineObj = GameObject.Find(spawnSplineName);
            if (splineObj != null)
            {
                spawnSpline = splineObj.GetComponent<SplineContainer>();
                if (spawnSpline != null)
                {
                    Debug.Log($"[NetworkManager] Found spawnSpline on scene instance '{spawnSplineName}' " +
                              $"at worldPos: {splineObj.transform.position}");
                }
                else
                {
                    Debug.LogWarning($"[NetworkManager] Found '{spawnSplineName}' but it has no SplineContainer component!");
                }
            }
            else
            {
                Debug.LogWarning($"[NetworkManager] Could not find GameObject named '{spawnSplineName}' in scene!");
            }
        }

        /// <summary>
        /// Pre-calculates spawn positions for all possible player slots (up to maxPlayers).
        /// Uses LevelSynchronizer seed so every client gets the same result.
        /// Called once when the first player joins.
        /// </summary>
        private Vector3[] _cachedSpawnPositions;
        private Quaternion[] _cachedSpawnRotations;

        private void CalculateSplineSpawnPoints()
        {
            // Already calculated this session
            if (_cachedSpawnPositions != null) return;

            _cachedSpawnPositions = new Vector3[maxPlayers];
            _cachedSpawnRotations = new Quaternion[maxPlayers];

            // If no spline assigned, fall back to simple X-axis spread
            if (spawnSpline == null)
            {
                Debug.LogWarning("[NetworkManager] No spawnSpline assigned! Falling back to X-axis spread.");
                for (int i = 0; i < maxPlayers; i++)
                {
                    _cachedSpawnPositions[i] = new Vector3(i * 10f, 0f, 0f);
                    _cachedSpawnRotations[i] = Quaternion.identity;
                }
                return;
            }

            var spline = spawnSpline.Spline;

            // Use LevelSynchronizer seed for deterministic randomness across all clients
            int seed = LevelSynchronizer.Instance != null ? LevelSynchronizer.Instance.LevelSeed : 42;
            System.Random rng = new System.Random(seed);

            // Convert minSpawnSpacing from world units to normalized t (0–1)
            float splineLength = spline.GetLength();
            float spacingT = splineLength > 0f ? minSpawnSpacing / splineLength : 0.1f;

            // Pick a random starting t value
            float startT = (float)rng.NextDouble();

            Debug.Log($"[NetworkManager] Spline spawn — seed: {seed}, splineLength: {splineLength:F1}, " +
                      $"spacingT: {spacingT:F4}, startT: {startT:F4}");

            for (int i = 0; i < maxPlayers; i++)
            {
                // Each player gets startT + (i * spacingT), wrapped to 0–1
                float t = (startT + i * spacingT) % 1f;

                // Evaluate position and tangent on the spline at this t
                float3 localPos = SplineUtility.EvaluatePosition(spline, t);
                float3 localTangent = SplineUtility.EvaluateTangent(spline, t);

                // Convert from spline local space to world space
                Vector3 worldPos = spawnSpline.transform.TransformPoint(localPos);
                Vector3 worldTangent = spawnSpline.transform.TransformDirection(math.normalize(localTangent));

                _cachedSpawnPositions[i] = worldPos;

                // Face the ship along the spline direction
                if (math.lengthsq(localTangent) > 0.001f)
                    _cachedSpawnRotations[i] = Quaternion.LookRotation(worldTangent, Vector3.up);
                else
                    _cachedSpawnRotations[i] = Quaternion.identity;

                Debug.Log($"[NetworkManager] Player slot {i} → t={t:F4}, pos={worldPos}, rot={_cachedSpawnRotations[i].eulerAngles}");
            }
        }

        private Vector3 GetSpawnPosition(PlayerRef player)
        {
            CalculateSplineSpawnPoints();
            int index = player.PlayerId % maxPlayers;
            return _cachedSpawnPositions[index];
        }

        private Quaternion GetSpawnRotation(PlayerRef player)
        {
            CalculateSplineSpawnPoints();
            int index = player.PlayerId % maxPlayers;
            return _cachedSpawnRotations[index];
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
            Debug.Log($"[NetworkManager] Player {player.PlayerId} joined. IsServer: {runner.IsServer}, " +
                      $"inGameplayScene: {_inGameplayScene}, playerPrefab: {(playerPrefab != null ? playerPrefab.name : "NULL")}");

            if (runner.IsServer && playerPrefab != null)
            {
                // Don't spawn ships in the menu/lobby scene — queue them for when gameplay scene loads
                if (!_inGameplayScene)
                {
                    Debug.Log($"[NetworkManager] Player {player.PlayerId} queued — waiting for gameplay scene to spawn.");
                    if (!_pendingSpawns.Contains(player))
                        _pendingSpawns.Add(player);
                }
                else
                {
                    SpawnPlayer(runner, player);
                }
            }
            else
            {
                Debug.LogWarning($"[NetworkManager] NOT spawning - IsServer: {runner.IsServer}, playerPrefab: {(playerPrefab != null ? "assigned" : "NULL")}");
            }

            // Show "Client has joined" on the host when a non-host player joins, then auto-hide.
            // Skip on dedicated server — no UI to show.
            if (!IsDedicatedServer && runner.IsServer && player != runner.LocalPlayer && clientJoinedText != null)
            {
                clientJoinedText.text = "Client has joined";
                clientJoinedText.gameObject.SetActive(true);
                Debug.Log("[NetworkManager] Client has joined — showing TMP notification");
                StartCoroutine(HideClientJoinedAfterDelay(3f));
            }

            OnPlayerJoinedGame?.Invoke(runner, player);
        }

        private void SpawnPlayer(NetworkRunner runner, PlayerRef player)
        {
            // Guard: don't double-spawn if both OnUnitySceneLoaded and OnSceneLoadDone fire
            if (_spawnedPlayers.ContainsKey(player) && _spawnedPlayers[player] != null)
            {
                Debug.Log($"[NetworkManager] Player {player.PlayerId} already has a ship — skipping spawn");
                return;
            }

            var spawnPos = GetSpawnPosition(player);
            var spawnRot = GetSpawnRotation(player);

            Debug.Log($"[NetworkManager] Spawning player {player.PlayerId} at spawnPoint: {spawnPos}");

            var playerObject = runner.Spawn(playerPrefab, spawnPos, spawnRot, inputAuthority: player);

            Debug.Log($"[NetworkManager] Spawn returned: {(playerObject != null ? playerObject.name : "NULL")}");

            if (playerObject != null)
            {
                playerObject.AssignInputAuthority(player);
                Debug.Log($"[NetworkManager] Assigned InputAuthority to {player}, now authority is: {playerObject.InputAuthority}");

                if (!IsDedicatedServer && player == runner.LocalPlayer)
                {
                    SetupCameraFollow(playerObject.gameObject);
                }
            }

            _spawnedPlayers[player] = playerObject;
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

        private IEnumerator HideClientJoinedAfterDelay(float delay)
        {
            yield return new WaitForSeconds(delay);
            if (clientJoinedText != null)
            {
                clientJoinedText.gameObject.SetActive(false);
            }
        }

        public void OnInput(NetworkRunner runner, NetworkInput input)
        {
            // Dedicated server has no local player and no input devices — skip.
            if (IsDedicatedServer) return;

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

        private Coroutine _failedStatusCoroutine;

        private IEnumerator ShowConnectionFailedStatus(string message)
        {
            if (statusText != null) statusText.text = message;

            yield return new WaitForSeconds(3f);

            // Reset lobby to let player try again
            ShowLobbyUI();

            _failedStatusCoroutine = null;
        }
        
        public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
        {
            Debug.LogWarning($"[NetworkManager] OnShutdown called! Reason={shutdownReason}, " +
                $"Runner={(runner != null ? "exists" : "NULL")}, " +
                $"IsRunning={(runner != null && runner.IsRunning ? "YES" : "NO")}, " +
                $"IsDedicatedServer={IsDedicatedServer}");
            _lastError = $"Shutdown: {shutdownReason}";

            // Reset UI on disconnect (skip on dedicated server — no UI)
            if (!IsDedicatedServer)
            {
                if (_failedStatusCoroutine != null) StopCoroutine(_failedStatusCoroutine);

                CurrentRoomCode = "";
                if (shutdownReason != ShutdownReason.Ok)
                {
                    _failedStatusCoroutine = StartCoroutine(ShowConnectionFailedStatus("Connection Failed"));
                }
                else
                {
                    ShowLobbyUI();
                }
            }
            
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
            CurrentRoomCode = "";

            if (_failedStatusCoroutine != null) StopCoroutine(_failedStatusCoroutine);
            _failedStatusCoroutine = StartCoroutine(ShowConnectionFailedStatus($"Connection Failed: {reason}"));
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
            if (data.Count >= 37)
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
        public void OnSceneLoadDone(NetworkRunner runner)
        {
            var activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            Debug.Log($"[NetworkManager] OnSceneLoadDone — scene: {activeScene}, pending spawns: {_pendingSpawns.Count}");

            // Check if we've arrived in the gameplay scene
            if (!string.IsNullOrEmpty(gameplaySceneName) && activeScene == gameplaySceneName)
            {
                _inGameplayScene = true;

                // Auto-find the spawn spline in the newly loaded gameplay scene
                TryFindSpawnSpline();

                // Use the same delayed spawn as OnUnitySceneLoaded — wait 1 frame for Start() calls.
                // Both callbacks may fire (Fusion's OnSceneLoadDone + Unity's sceneLoaded).
                // SpawnPendingPlayersNextFrame handles duplicates by checking _spawnedPlayers.
                if (runner.IsServer)
                {
                    StartCoroutine(SpawnPendingPlayersNextFrame());
                }
            }
        }
        public void OnSceneLoadStart(NetworkRunner runner) { }
        public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
        public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
        
        #endregion
        
        private string _lastError = "";
        private string _currentRegion = "Unknown";
        
        private void OnGUI()
        {
            // Dedicated server has no screen — skip all GUI rendering.
            if (IsDedicatedServer) return;
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
                GUILayout.Label("Use UI to Create/Join Room");
            }
            else
            {
                GUILayout.Label($"Connected as {(Runner.IsServer ? "Host" : "Client")}");
                GUILayout.Label($"Players: {Runner.ActivePlayers.Count()}");
                GUILayout.Label($"Room: {CurrentRoomCode}");
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
