using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
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

        [Header("Dedicated Server (VPS)")]
        [Tooltip("URL of the VPS RoomManager HTTP API (e.g. http://187.124.96.178:7350). " +
                 "Used by clients to create rooms on the dedicated server.")]
        [SerializeField] private string vpsRoomApiUrl = "http://187.124.96.178:7350";

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
        internal bool _inGameplayScene = false;
        private Coroutine _hideClientJoinedCoroutine;

        /// <summary>
        /// True when this client is connected to a dedicated server (not a player-hosted game).
        /// Detected by checking if the session was created by a Server (no local player on host side).
        /// In dedicated server mode, clients get the Enter Battle button instead of waiting.
        /// </summary>
        private bool _connectedToDedicatedServer = false;

        /// <summary>
        /// Prevents double-countdown when client starts countdown locally AND receives
        /// COUNTDOWN from the server in dedicated server mode.
        /// </summary>
        internal bool _countdownActive = false;

        /// <summary>
        /// When true, the next raw input send will embed a START_MATCH signal (magic % 1000 == 99).
        /// This piggybacks on the proven 37-byte input channel instead of a separate 4-byte reliable key
        /// which Fusion sometimes fails to deliver when input data is flooding.
        /// </summary>
        private bool _sendStartMatchViaInput = false;
        private int _startMatchSendAttempts = 0;
        private const int START_MATCH_SEND_REPEATS = 60; // Send the signal for ~1 second of frames to ensure delivery

        /// <summary>
        /// Flag-based countdown for dedicated server. Avoids StartCoroutine which can crash
        /// with ArgumentNullException when the MonoBehaviour is in a bad state (destroyed but
        /// still receiving Fusion callbacks). Set by TriggerMatchStart(), ticked by Update().
        /// </summary>
        private bool _matchStartTriggered = false;
        private float _matchStartTime = 0f;
        private bool _matchCountdownSentLoad = false;

        /// <summary>
        /// Persistent loading screen created via code. Survives scene loads via DontDestroyOnLoad.
        /// Automatically hidden when the local player's ship is detected in the scene.
        /// </summary>
        private GameObject _loadingScreenGO;
        private bool _waitingForShipToAppear = false;
        private float _loadingScreenShownTime = 0f;
        private const float LOADING_SCREEN_TIMEOUT = 25f; // Force-hide after 25s (past server's 15s timeout)
        private int _shipDetectLogCounter = 0;

        /// <summary>
        /// When true, this NetworkManager instance was created by RoomManager for a specific room.
        /// It will NOT auto-start in Start() — RoomManager calls StartServerForRoom() instead.
        /// Also skips singleton enforcement (multiple instances exist, one per room).
        /// </summary>
        private bool _isRoomManagerControlled = false;

        /// <summary>
        /// Persistent flag indicating we're in a room-based flow (Create Room or Join Room).
        /// Unlike _connectedToDedicatedServer, this is NOT reset in OnShutdown.
        /// This ensures that if the Runner shuts down and restarts (e.g., reconnect),
        /// we still use NoOpSceneManager instead of NetworkSceneManagerDefault.
        /// Only cleared when the user explicitly returns to the main lobby.
        /// </summary>
        private bool _roomFlowActive = false;

        /// <summary>
        /// Set to true ONLY when OUR code intentionally loads the gameplay scene
        /// (after Enter Battle countdown). This prevents OnUnitySceneLoaded from
        /// treating Fusion's auto-synced scene loads as intentional gameplay starts.
        /// Without this guard, Fusion's scene sync loads Room_Tests_2 behind the UI,
        /// OnUnitySceneLoaded sets _inGameplayScene=true, and ships spawn prematurely.
        /// </summary>
        private bool _manualSceneLoadRequested = false;

        /// <summary>
        /// True when this client created the room (leader). False when joining an existing room.
        /// The leader gets the Enter Battle button; joiners see "Waiting for leader to start..."
        /// </summary>
        private bool _isRoomCreator = false;

        /// <summary>
        /// Callback invoked after StartServerForRoom completes.
        /// </summary>
        private Action<bool> _roomStartCallback;

        private void Awake()
        {
            Debug.Log($"[NetworkManager] Awake() on instance {GetInstanceID()}, " +
                      $"existing Instance={(Instance != null ? Instance.GetInstanceID().ToString() : "NULL")}, " +
                      $"scene={gameObject.scene.name}, roomManagerControlled={_isRoomManagerControlled}");

            // When RoomManager creates per-room instances, skip singleton enforcement.
            // Multiple NetworkManagers coexist on the server (one per room).
            if (!_isRoomManagerControlled)
            {
                if (Instance != null && Instance != this)
                {
                    Debug.LogWarning($"[NetworkManager] DUPLICATE detected! Destroying {GetInstanceID()}, keeping {Instance.GetInstanceID()}");
                    Destroy(gameObject);
                    return;
                }
                Instance = this;
            }

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
            Debug.Log("[NetworkManager] BUILD_VERSION: 2026-03-10-camera-fix-v8");
        }

        private void Start()
        {
            // --- DEDICATED SERVER (RoomManager-controlled): do nothing here ---
            // RoomManager will call StartServerForRoom() when a room is created.
            if (IsDedicatedServer && _isRoomManagerControlled)
            {
                Debug.Log("[NetworkManager] Dedicated Server (RoomManager-controlled) — waiting for StartServerForRoom()");
                return;
            }

            // --- DEDICATED SERVER (legacy/standalone): auto-start single session ---
            // Kept as fallback if RoomManager is not used.
            if (IsDedicatedServer)
            {
                _inGameplayScene = true;
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

            // Client waits for player to click Create Room or Join Room.
            // No auto-start — all games go through the VPS dedicated server.
        }

        // ── Room UI helpers ──────────────────────────────────────────

        public void ShowLobbyUI()
        {
            Debug.Log($"[NetworkManager] ShowLobbyUI called. " +
                      $"lobbyPanel={(lobbyPanel != null ? "assigned" : "NULL")}, " +
                      $"createBtn={(createRoomButton != null ? "assigned" : "NULL")}, " +
                      $"joinBtn={(joinRoomButton != null ? "assigned" : "NULL")}");

            // Clear room flow state when returning to lobby
            _roomFlowActive = false;

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

            Debug.Log($"[MATCH-DEBUG] ShowConnectedUI: _isRoomCreator={_isRoomCreator}, " +
                      $"enterBattleButton={(enterBattleButton != null ? "exists" : "NULL")}, " +
                      $"clientJoinedText={(clientJoinedText != null ? "exists" : "NULL")}");

            if (_isRoomCreator)
            {
                // Leader (room creator) gets the Enter Battle button
                Debug.Log("[MATCH-DEBUG] ShowConnectedUI: CREATOR — showing Enter Battle button");
                if (enterBattleButton != null) enterBattleButton.gameObject.SetActive(true);
                if (clientJoinedText != null) clientJoinedText.gameObject.SetActive(false);
            }
            else
            {
                // Joiners wait for the leader to start the match
                Debug.Log("[MATCH-DEBUG] ShowConnectedUI: JOINER — showing 'Waiting for leader...'");
                if (enterBattleButton != null) enterBattleButton.gameObject.SetActive(false);
                if (clientJoinedText != null)
                {
                    clientJoinedText.text = "Waiting for leader to start the match...";
                    clientJoinedText.gameObject.SetActive(true);
                }
            }
        }

        /// <summary>
        /// Hides all menu/lobby UI panels. Called before loading gameplay scene.
        /// These panels are on the NetworkManager (DontDestroyOnLoad) so they persist
        /// across scene loads and need to be explicitly hidden.
        /// </summary>
        private void HideAllMenuUI()
        {
            if (lobbyPanel != null) lobbyPanel.SetActive(false);
            if (connectedPanel != null) connectedPanel.SetActive(false);
            if (enterBattleButton != null) enterBattleButton.gameObject.SetActive(false);
            if (statusText != null) statusText.gameObject.SetActive(false);
        }

        /// <summary>
        /// Creates a full-screen loading overlay that persists through scene loads.
        /// Shows "Loading..." text centered on a semi-transparent black background.
        /// </summary>
        private void ShowLoadingScreen()
        {
            if (_loadingScreenGO != null) return; // Already showing

            _loadingScreenGO = new GameObject("LoadingScreen");
            DontDestroyOnLoad(_loadingScreenGO);

            var canvas = _loadingScreenGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 9999; // On top of everything

            _loadingScreenGO.AddComponent<UnityEngine.UI.CanvasScaler>();
            _loadingScreenGO.AddComponent<UnityEngine.UI.GraphicRaycaster>();

            // Semi-transparent black background
            var bgGO = new GameObject("Background");
            bgGO.transform.SetParent(_loadingScreenGO.transform, false);
            var bgImage = bgGO.AddComponent<UnityEngine.UI.Image>();
            bgImage.color = new Color(0f, 0f, 0f, 0.85f);
            var bgRect = bgGO.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;

            // "Loading..." text
            var textGO = new GameObject("LoadingText");
            textGO.transform.SetParent(_loadingScreenGO.transform, false);
            var tmp = textGO.AddComponent<TMPro.TextMeshProUGUI>();
            tmp.text = "Loading...";
            tmp.fontSize = 48;
            tmp.alignment = TMPro.TextAlignmentOptions.Center;
            tmp.color = Color.white;
            var textRect = textGO.GetComponent<RectTransform>();
            textRect.anchorMin = new Vector2(0.3f, 0.4f);
            textRect.anchorMax = new Vector2(0.7f, 0.6f);
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            _waitingForShipToAppear = true;
            _loadingScreenShownTime = Time.time;
            _shipDetectLogCounter = 0;
            Debug.Log("[NetworkManager] Loading screen SHOWN — waiting for ship to appear");
        }

        /// <summary>
        /// Destroys the loading screen overlay.
        /// </summary>
        private void HideLoadingScreen()
        {
            if (_loadingScreenGO != null)
            {
                Destroy(_loadingScreenGO);
                _loadingScreenGO = null;
            }
            _waitingForShipToAppear = false;
            Debug.Log("[NetworkManager] Loading screen HIDDEN — ship appeared");
        }

        /// <summary>
        /// Loads the gameplay scene. Called by "Enter Battle" button.
        /// Client sends START_MATCH to the dedicated server, which then
        /// sends countdown/load to all clients. Server is already in gameplay scene.
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
                Debug.LogWarning("[NetworkManager] LoadGameplay called but Runner is null — ignoring.");
                return;
            }

            // CLIENT sends START_MATCH to dedicated server
            if (Runner.IsClient && !Runner.IsServer)
            {
                Debug.Log($"[MATCH-DEBUG] CLIENT: LoadGameplay() CALLED! Runner.IsClient={Runner.IsClient}, " +
                          $"Runner.IsServer={Runner.IsServer}, _isRoomCreator={_isRoomCreator}, " +
                          $"LocalPlayer={Runner.LocalPlayer}, roomCode={CurrentRoomCode}");
                if (enterBattleButton != null) enterBattleButton.interactable = false;

                // ═══ PRIMARY: HTTP POST to VPS /start/{code} (TCP — guaranteed delivery) ═══
                // Fusion's ReliableData has proven unreliable for START_MATCH signals.
                // HTTP uses the same VPS endpoint that room creation uses, which is rock-solid.
                if (_connectedToDedicatedServer && !string.IsNullOrEmpty(CurrentRoomCode))
                {
                    Debug.Log($"[MATCH-DEBUG] CLIENT: Sending START_MATCH via HTTP to VPS /start/{CurrentRoomCode}");
                    StartCoroutine(SendStartMatchViaHttp(CurrentRoomCode));
                }

                // ═══ FALLBACK 1: Embed in magic number (raw input channel) ═══
                _sendStartMatchViaInput = true;
                _startMatchSendAttempts = 0;
                Debug.Log("[NetworkManager] CLIENT: START_MATCH flag SET — will embed magic=99 in raw input sends");

                try
                {
                    // FALLBACK 2: Old 4-byte reliable key (may not arrive due to Fusion channel issue)
                    Runner.SendReliableDataToServer(START_MATCH_KEY, START_MATCH_MAGIC);
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[NetworkManager] 4-byte START_MATCH send failed (non-critical): {ex.Message}");
                }

                // Client starts its own countdown — server will also send COUNTDOWN to all clients,
                // but starting locally ensures no delay for the button-presser
                StartCoroutine(CountdownThenLoad());
                return;
            }

            Debug.LogWarning("[NetworkManager] LoadGameplay called but not a client — ignoring.");
        }

        /// <summary>
        /// Sends START_MATCH to the VPS via HTTP POST (TCP transport — guaranteed delivery).
        /// This bypasses Fusion's ReliableData which has proven unreliable for signaling.
        /// </summary>
        private IEnumerator SendStartMatchViaHttp(string roomCode)
        {
            string url = $"{vpsRoomApiUrl.TrimEnd('/')}/start/{roomCode}";
            Debug.Log($"[MATCH-DEBUG] CLIENT: HTTP POST {url}");

            using (var request = new UnityEngine.Networking.UnityWebRequest(url, "POST"))
            {
                request.downloadHandler = new UnityEngine.Networking.DownloadHandlerBuffer();
                request.timeout = 5;

                yield return request.SendWebRequest();

                if (request.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
                {
                    Debug.Log($"[MATCH-DEBUG] CLIENT: HTTP /start/{roomCode} SUCCESS: {request.downloadHandler.text}");
                }
                else
                {
                    Debug.LogError($"[MATCH-DEBUG] CLIENT: HTTP /start/{roomCode} FAILED: {request.error} " +
                                   $"(code={request.responseCode})");
                }
            }
        }

        /// <summary>
        /// Shows a countdown on screen (5, 4, 3, 2, 1) then triggers scene loading.
        /// Called on HOST after sending countdown command to clients.
        /// Called on CLIENT after receiving countdown command from host.
        /// </summary>
        private IEnumerator CountdownThenLoad()
        {
            // Guard against double-countdown (client starts locally + receives COUNTDOWN from server)
            if (_countdownActive) yield break;
            _countdownActive = true;

            Debug.Log($"[CTL-DIAG] CountdownThenLoad STARTED. Runner={Runner != null}, " +
                      $"IsClient={Runner?.IsClient}, IsServer={Runner?.IsServer}, " +
                      $"_connectedToDedicatedServer={_connectedToDedicatedServer}, " +
                      $"gameplaySceneName='{gameplaySceneName}', " +
                      $"activeScene='{UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}'");

            // Cancel any "Client has joined" auto-hide so it doesn't hide our countdown
            if (_hideClientJoinedCoroutine != null)
            {
                StopCoroutine(_hideClientJoinedCoroutine);
                _hideClientJoinedCoroutine = null;
            }

            // Show countdown using clientJoinedText (inside connectedPanel, visible on both sides)
            if (clientJoinedText != null)
            {
                clientJoinedText.gameObject.SetActive(true);
            }

            for (int i = COUNTDOWN_SECONDS; i > 0; i--)
            {
                if (clientJoinedText != null)
                    clientJoinedText.text = $"Match starting in {i}...";
                Debug.Log($"[NetworkManager] Countdown: {i}");
                yield return new WaitForSeconds(1f);
            }

            if (clientJoinedText != null)
                clientJoinedText.text = "GO!";
            Debug.Log("[NetworkManager] Countdown finished!");

            // Small pause to show "GO!"
            yield return new WaitForSeconds(0.3f);

            // Show "Loading..." while scene loads
            if (clientJoinedText != null)
                clientJoinedText.text = "Loading...";

            Debug.Log($"[CTL-DIAG] Post-countdown state: Runner={Runner != null}, " +
                      $"IsClient={Runner?.IsClient}, IsServer={Runner?.IsServer}, " +
                      $"IsDedicatedServer={IsDedicatedServer}, " +
                      $"_connectedToDedicatedServer={_connectedToDedicatedServer}, " +
                      $"_manualSceneLoadRequested={_manualSceneLoadRequested}, " +
                      $"_inGameplayScene={_inGameplayScene}");

            // HOST (non-dedicated): send LOAD command to clients and then load the scene
            if (Runner != null && Runner.IsServer && !IsDedicatedServer)
            {
                Debug.Log("[CTL-DIAG] Taking HOST path (IsServer && !IsDedicatedServer)");
                StartCoroutine(LoadGameplayWithClientSync());
            }
            // CLIENT connected to dedicated server: load the scene ASYNCHRONOUSLY.
            // CRITICAL: We MUST use LoadSceneAsync so that Unity's game loop keeps running
            // during the load. This lets Update() continue sending input to the server,
            // keeping the Fusion connection alive. Synchronous LoadScene blocks for 5-7s
            // on heavy scenes, causing Fusion to disconnect the client.
            else if (Runner != null && Runner.IsClient && _connectedToDedicatedServer)
            {
                Debug.Log($"[CTL-DIAG] Taking CLIENT+DEDICATED path. Loading '{gameplaySceneName}' ASYNC.");
                _manualSceneLoadRequested = true;
                _inGameplayScene = true;
                _inGameplaySceneTimestamp = Time.time;
                _clientReadyWatchdogFired = false;

                // Show a persistent loading screen that survives scene load
                try
                {
                    ShowLoadingScreen();
                    HideAllMenuUI();
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[CTL-DIAG] EXCEPTION in ShowLoadingScreen/HideAllMenuUI: {ex}");
                }

                // Async load — game loop continues, network stays alive
                AsyncOperation asyncOp = null;
                try
                {
                    asyncOp = UnityEngine.SceneManagement.SceneManager.LoadSceneAsync(gameplaySceneName);
                    Debug.Log($"[CTL-DIAG] LoadSceneAsync('{gameplaySceneName}') returned: {(asyncOp != null ? "valid AsyncOperation" : "NULL")}");
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[CTL-DIAG] EXCEPTION in LoadSceneAsync: {ex}");
                }

                if (asyncOp != null)
                {
                    Debug.Log($"[CTL-DIAG] Yielding on asyncOp (isDone={asyncOp.isDone}, progress={asyncOp.progress})...");
                    yield return asyncOp; // Coroutine waits for load to complete
                    Debug.Log($"[CTL-DIAG] Async scene load COMPLETED for '{gameplaySceneName}'. " +
                              $"_clientSendingReady={_clientSendingReady}, _manualSceneLoadRequested={_manualSceneLoadRequested}");
                }
                else
                {
                    // Fallback to sync load if async returns null (shouldn't happen)
                    Debug.LogWarning("[CTL-DIAG] LoadSceneAsync returned null! Falling back to sync load.");
                    try
                    {
                        UnityEngine.SceneManagement.SceneManager.LoadScene(gameplaySceneName);
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogError($"[CTL-DIAG] EXCEPTION in sync LoadScene: {ex}");
                    }
                }

                // === FALLBACK CLIENT_READY ===
                // OnUnitySceneLoaded SHOULD have fired during the scene load above and set
                // _clientSendingReady=true + sent CLIENT_READY on all 3 channels. But if it
                // didn't fire (e.g., scene name mismatch, _manualSceneLoadRequested was false,
                // RegisterSceneNetworkObjects threw, or any other failure), we send CLIENT_READY
                // here as a safety net. The server uses a HashSet for _clientsReady, so duplicate
                // signals are harmless.
                if (!_clientSendingReady && Runner != null && Runner.IsClient && Runner.IsRunning)
                {
                    Debug.LogWarning($"[CTL-DIAG] FALLBACK: _clientSendingReady is STILL false after scene load! " +
                                     $"OnUnitySceneLoaded did NOT send CLIENT_READY. Sending from CountdownThenLoad fallback. " +
                                     $"_manualSceneLoadRequested={_manualSceneLoadRequested}, _inGameplayScene={_inGameplayScene}");

                    // Channel 1: 4-byte CLIENT_READY_KEY
                    try
                    {
                        Runner.SendReliableDataToServer(CLIENT_READY_KEY, CLIENT_READY_MAGIC);
                        Debug.Log("[CTL-DIAG] FALLBACK: Sent CLIENT_READY via 4-byte key.");
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogWarning($"[CTL-DIAG] FALLBACK: Failed 4-byte CLIENT_READY: {ex.Message}");
                    }

                    // Channel 2: 37-byte CLIENT_READY_SIGNAL_MAGIC on INPUT_DATA_KEY
                    try
                    {
                        var readyData = new PlayerInputData();
                        readyData.magicNumber = CLIENT_READY_SIGNAL_MAGIC;
                        byte[] readyBytes = SerializeInput(readyData);
                        Runner.SendReliableDataToServer(INPUT_DATA_KEY, readyBytes);
                        Debug.Log("[CTL-DIAG] FALLBACK: Sent CLIENT_READY via 37-byte INPUT_DATA_KEY.");
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogWarning($"[CTL-DIAG] FALLBACK: Failed 37-byte CLIENT_READY: {ex.Message}");
                    }

                    // Channel 3: Embed in regular input
                    _clientSendingReady = true;
                    _clientReadySendStartTime = Time.time;
                    Debug.Log($"[CTL-DIAG] FALLBACK: _clientSendingReady=true, will embed magic%1000==77 for {CLIENT_READY_SEND_DURATION}s");
                }
                else if (_clientSendingReady)
                {
                    Debug.Log("[CTL-DIAG] CLIENT_READY already active (sent by OnUnitySceneLoaded). No fallback needed.");
                }
            }
            else
            {
                // NO PATH MATCHED — this is the bug if it fires on a dedicated-server client
                Debug.LogError($"[CTL-DIAG] NO SCENE LOAD PATH MATCHED! " +
                               $"Runner={(Runner != null ? "exists" : "NULL")}, " +
                               $"IsClient={Runner?.IsClient}, IsServer={Runner?.IsServer}, " +
                               $"IsDedicatedServer={IsDedicatedServer}, " +
                               $"_connectedToDedicatedServer={_connectedToDedicatedServer}. " +
                               $"SCENE WILL NOT LOAD — CLIENT_READY WILL NEVER BE SENT!");
            }

            Debug.Log($"[CTL-DIAG] CountdownThenLoad EXITING. _clientSendingReady={_clientSendingReady}");
        }

        /// <summary>
        /// Sends scene load command to all clients, waits for transmission, then loads locally.
        /// The delay gives Fusion time to actually send the data before the scene change.
        /// </summary>
        private IEnumerator LoadGameplayWithClientSync()
        {
            Debug.Log($"[NetworkManager] HOST: Sending scene load command to all clients...");

            // Send "LOAD" command to every connected client
            foreach (var player in Runner.ActivePlayers)
            {
                if (player != Runner.LocalPlayer)
                {
                    try
                    {
                        Runner.SendReliableDataToPlayer(player, SCENE_LOAD_KEY, SCENE_LOAD_MAGIC);
                        Debug.Log($"[NetworkManager] Sent SCENE_LOAD to player {player.PlayerId}");
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogError($"[NetworkManager] Failed to send scene load to player {player.PlayerId}: {ex.Message}");
                    }
                }
            }

            // Wait 10 frames for Fusion to transmit the data to clients
            for (int i = 0; i < 10; i++)
                yield return null;

            Debug.Log($"[NetworkManager] HOST: Now loading gameplay scene (async): {gameplaySceneName}");
            _manualSceneLoadRequested = true;
            HideAllMenuUI();
            // Async load — keeps game loop running so Fusion connection stays alive
            var asyncOp = UnityEngine.SceneManagement.SceneManager.LoadSceneAsync(gameplaySceneName);
            if (asyncOp != null)
                yield return asyncOp;
        }

        /// <summary>
        /// Safely triggers match start on dedicated server. Sets flags instead of using
        /// StartCoroutine directly, avoiding ArgumentNullException when the MonoBehaviour
        /// is in a destroyed state but still receiving Fusion callbacks.
        /// </summary>
        internal void TriggerMatchStart(PlayerRef triggeringPlayer)
        {
            // CRITICAL: If this callback fired on a stale/destroyed instance (DUAL INSTANCE),
            // delegate to the live Instance so that Update() will see the flags.
            var target = (Instance != null && Instance != this) ? Instance : this;
            var activeRunner = (target.Runner != null && target.Runner.IsRunning) ? target.Runner
                             : (Runner != null && Runner.IsRunning) ? Runner : null;

            if (target != this)
            {
                Debug.LogWarning($"[SPAWN-DEBUG] TriggerMatchStart REDIRECTED from stale instance {GetInstanceID()} to live Instance {target.GetInstanceID()}");
            }

            target._countdownActive = true;
            target._matchStartTriggered = true;
            target._matchStartTime = Time.time;
            target._matchCountdownSentLoad = false;
            target._serverSendingCountdown = true;

            Debug.Log($"[SPAWN-DEBUG] SERVER: TriggerMatchStart — sending COUNTDOWN, will spawn after 0.5s (client handles its own {COUNTDOWN_SECONDS}s countdown)");

            // Send COUNTDOWN to ALL clients
            if (activeRunner != null)
            {
                foreach (var p in activeRunner.ActivePlayers)
                {
                    try
                    {
                        activeRunner.SendReliableDataToPlayer(p, COUNTDOWN_KEY, COUNTDOWN_MAGIC);
                        Debug.Log($"[SPAWN-DEBUG] SERVER: Sent COUNTDOWN to player {p.PlayerId}");
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogError($"[SPAWN-DEBUG] SERVER: Failed to send COUNTDOWN to {p.PlayerId}: {ex.Message}");
                    }
                }
            }
            else
            {
                Debug.LogError("[SPAWN-DEBUG] SERVER: TriggerMatchStart — NO active Runner found! Cannot send COUNTDOWN.");
            }
        }

        /// <summary>
        /// Update-based dedicated server countdown. Replaces the coroutine approach
        /// which crashed with ArgumentNullException on destroyed MonoBehaviours.
        ///
        /// NEW FLOW (ack-based):
        /// 1. Countdown finishes → send SCENE_LOAD to all clients (do NOT spawn yet)
        /// 2. Keep sending SCENE_LOAD every frame until all clients respond with CLIENT_READY
        /// 3. Once all clients are ready (or timeout), THEN spawn player objects
        /// This ensures clients have their gameplay scene loaded before NetworkObjects are created.
        /// </summary>
        private void UpdateMatchStartCountdown()
        {
            if (!IsDedicatedServer) return;

            // --- PHASE 3: WAITING FOR CLIENT_READY (runs after SCENE_LOAD sent, before spawn) ---
            if (_waitingForClientsReady && Runner != null)
            {
                float waitElapsed = Time.time - _sceneLoadSignalStartTime;

                // NOTE: We do NOT re-send SCENE_LOAD every frame anymore.
                // Flooding SCENE_LOAD on INPUT_DATA_KEY can cause the client's OnReliableDataReceived
                // to fire repeatedly, and if the client loads a scene from inside that callback it
                // corrupts Fusion's runner. SCENE_LOAD was already sent once when entering PHASE 3.
                // Clients that received COUNTDOWN will load the scene via CountdownThenLoad() anyway.

                // Check if all expected players are ready
                bool allReady = false; // Default to false — only true if at least one connected player is ready
                int connectedWaiting = 0;
                int connectedReady = 0;
                foreach (var p in _playersAwaitingReady)
                {
                    // Skip players who disconnected while we were waiting
                    bool stillConnected = false;
                    foreach (var ap in Runner.ActivePlayers)
                    {
                        if (ap == p) { stillConnected = true; break; }
                    }
                    if (!stillConnected)
                    {
                        Debug.Log($"[SPAWN-DEBUG] SERVER: Player {p.PlayerId} disconnected while waiting for CLIENT_READY — skipping.");
                        continue;
                    }

                    connectedWaiting++;
                    if (_clientsReady.Contains(p))
                    {
                        connectedReady++;
                    }
                }
                // All connected players are ready (and there IS at least one)
                allReady = (connectedWaiting > 0 && connectedReady == connectedWaiting);

                // If ALL players disconnected, stop waiting (nothing to spawn)
                if (connectedWaiting == 0)
                {
                    Debug.LogWarning("[SPAWN-DEBUG] SERVER: All players disconnected while waiting for CLIENT_READY. Aborting spawn.");
                    _waitingForClientsReady = false;
                    _serverSendingSceneLoad = false;
                    return;
                }

                bool timedOut = waitElapsed >= CLIENT_READY_TIMEOUT;

                if (allReady || timedOut)
                {
                    if (timedOut && !allReady)
                    {
                        string missing = "";
                        foreach (var p in _playersAwaitingReady)
                        {
                            if (!_clientsReady.Contains(p)) missing += p.PlayerId + " ";
                        }
                        Debug.LogWarning($"[SPAWN-DEBUG] SERVER: CLIENT_READY TIMEOUT after {CLIENT_READY_TIMEOUT}s! " +
                                         $"Missing players: {missing.Trim()}. Spawning anyway.");
                    }
                    else
                    {
                        Debug.Log($"[SPAWN-DEBUG] SERVER: All clients reported READY in {waitElapsed:F1}s! Spawning players...");
                    }

                    // Stop sending SCENE_LOAD, stop waiting
                    _waitingForClientsReady = false;
                    _serverSendingSceneLoad = false;

                    // NOW spawn all players (clients have their scene loaded)
                    ServerSpawnAllPendingPlayers();
                }
                else if (waitElapsed > 0 && (int)(waitElapsed * 2) % 2 == 0) // Log every ~1s
                {
                    // Periodic status log
                    Debug.Log($"[SPAWN-DEBUG] SERVER: Waiting for CLIENT_READY... {connectedReady}/{connectedWaiting} connected ready, " +
                              $"{_clientsReady.Count}/{_playersAwaitingReady.Count} total, " +
                              $"elapsed={waitElapsed:F1}s/{CLIENT_READY_TIMEOUT}s");
                }

                return; // Don't fall through to countdown logic while waiting
            }

            // --- PHASE 1: COUNTDOWN (sending countdown signals to clients) ---
            if (!_matchStartTriggered || _matchCountdownSentLoad) return;

            float elapsed = Time.time - _matchStartTime;

            // Repeated COUNTDOWN signal (piggyback on proven INPUT_DATA_KEY channel)
            if (_serverSendingCountdown && Runner != null)
            {
                var countdownData = new PlayerInputData();
                countdownData.magicNumber = COUNTDOWN_SIGNAL_MAGIC;
                byte[] countdownBytes = SerializeInput(countdownData);
                foreach (var p in Runner.ActivePlayers)
                {
                    try
                    {
                        Runner.SendReliableDataToPlayer(p, INPUT_DATA_KEY, countdownBytes);
                    }
                    catch (System.Exception) { /* best-effort */ }
                }
            }

            // Wait for countdown duration (same as client countdown)
            float spawnDelay = COUNTDOWN_SECONDS + 0.5f;
            if (elapsed < spawnDelay) return;

            // --- PHASE 2: COUNTDOWN FINISHED → SEND SCENE_LOAD (do NOT spawn yet) ---
            _matchCountdownSentLoad = true;
            _serverSendingCountdown = false;
            Debug.Log("[SPAWN-DEBUG] SERVER: Flag-based countdown finished! Sending SCENE_LOAD to clients (waiting for CLIENT_READY before spawning)...");

            // Prepare gameplay state on server side
            _inGameplayScene = true;
            Debug.Log($"[SPAWN-DEBUG] SERVER: _inGameplayScene set to TRUE. pendingSpawns={_pendingSpawns.Count}, " +
                      $"alreadySpawned={_spawnedPlayers.Count}, Runner={Runner != null}, RunnerIsRunning={Runner?.IsRunning}, " +
                      $"playerPrefab={(playerPrefab != null ? playerPrefab.name : "NULL")}");

            TryFindSpawnSpline();
            Debug.Log($"[SPAWN-DEBUG] SERVER: TryFindSpawnSpline done. spawnSpline={(spawnSpline != null ? spawnSpline.name : "NULL")}");

            // Track which players we're waiting for
            _clientsReady.Clear();
            _playersAwaitingReady.Clear();
            if (Runner != null)
            {
                foreach (var p in Runner.ActivePlayers)
                {
                    _playersAwaitingReady.Add(p);
                }
            }
            Debug.Log($"[SPAWN-DEBUG] SERVER: Waiting for CLIENT_READY from {_playersAwaitingReady.Count} players...");

            // Mark that we've entered PHASE 3 (waiting for CLIENT_READY)
            _serverSendingSceneLoad = true;
            _waitingForClientsReady = true;
            _sceneLoadSignalStartTime = Time.time;

            // Send SCENE_LOAD ONCE on both channels (4-byte key + 37-byte INPUT_DATA_KEY)
            // We do NOT re-send every frame — flooding from OnReliableDataReceived can corrupt
            // the client's Fusion runner if it triggers SceneManager.LoadScene.
            if (Runner != null)
            {
                var sceneLoadData = new PlayerInputData();
                sceneLoadData.magicNumber = SCENE_LOAD_SIGNAL_MAGIC;
                byte[] sceneLoadBytes = SerializeInput(sceneLoadData);

                foreach (var player in Runner.ActivePlayers)
                {
                    try
                    {
                        // 37-byte channel (proven delivery)
                        Runner.SendReliableDataToPlayer(player, INPUT_DATA_KEY, sceneLoadBytes);
                        // 4-byte channel (legacy fallback)
                        Runner.SendReliableDataToPlayer(player, SCENE_LOAD_KEY, SCENE_LOAD_MAGIC);
                        Debug.Log($"[NetworkManager] SERVER: Sent SCENE_LOAD to player {player.PlayerId}");
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogError($"[NetworkManager] SERVER: Failed to send SCENE_LOAD to {player.PlayerId}: {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Spawns all pending players. Called once all clients have reported CLIENT_READY
        /// (or after timeout). Extracted from the old countdown logic for reuse.
        /// </summary>
        private void ServerSpawnAllPendingPlayers()
        {
            var playersToSpawn = new List<PlayerRef>(_pendingSpawns);
            _pendingSpawns.Clear();

            Debug.Log($"[SPAWN-DEBUG] SERVER: Copied {playersToSpawn.Count} from pendingSpawns. Checking ActivePlayers...");

            if (Runner != null)
            {
                int activeCount = 0;
                foreach (var p in Runner.ActivePlayers)
                {
                    activeCount++;
                    bool alreadySpawned = _spawnedPlayers.ContainsKey(p);
                    bool alreadyInList = playersToSpawn.Contains(p);
                    Debug.Log($"[SPAWN-DEBUG] SERVER: ActivePlayer {p.PlayerId} — alreadySpawned={alreadySpawned}, alreadyInList={alreadyInList}");
                    if (!alreadySpawned && !alreadyInList)
                        playersToSpawn.Add(p);
                }
                Debug.Log($"[SPAWN-DEBUG] SERVER: ActivePlayers count={activeCount}, final playersToSpawn={playersToSpawn.Count}");
            }

            Debug.Log($"[SPAWN-DEBUG] SERVER: Spawning {playersToSpawn.Count} players now...");
            foreach (var p in playersToSpawn)
            {
                Debug.Log($"[SPAWN-DEBUG] SERVER: About to SpawnPlayer for {p.PlayerId}, slot={_spawnedPlayers.Count}");
                SpawnPlayer(Runner, p, _spawnedPlayers.Count);
                Debug.Log($"[SPAWN-DEBUG] SERVER: SpawnPlayer returned for {p.PlayerId}, _spawnedPlayers now has {_spawnedPlayers.Count} entries");
            }

            Debug.Log("[NetworkManager] SERVER: Match started — all clients ready, players spawned.");
        }

        /// <summary>
        /// Dedicated server version of the countdown + load flow (LEGACY COROUTINE — kept as backup).
        /// Waits for countdown, then sends SCENE_LOAD to all clients and waits for CLIENT_READY.
        /// Server does NOT load a new scene — it's already in the gameplay scene.
        /// NOTE: The Update-based UpdateMatchStartCountdown() is the primary path. This coroutine
        /// is only used if something triggers it directly (legacy code paths).
        /// </summary>
        private IEnumerator DedicatedServerCountdownThenLoad()
        {
            // Guard against duplicate coroutines (both old 4-byte key AND input channel may trigger)
            if (_countdownActive)
            {
                Debug.Log("[SPAWN-DEBUG] SERVER: DedicatedServerCountdownThenLoad SKIPPED — countdown already active");
                yield break;
            }
            _countdownActive = true;

            // Wait for countdown duration (server doesn't show UI, just waits)
            Debug.Log($"[NetworkManager] SERVER: Starting {COUNTDOWN_SECONDS}s countdown...");
            for (int i = COUNTDOWN_SECONDS; i > 0; i--)
            {
                Debug.Log($"[NetworkManager] SERVER: Countdown: {i}");
                yield return new WaitForSeconds(1f);
            }
            Debug.Log("[NetworkManager] SERVER: Countdown finished! Sending SCENE_LOAD to clients (waiting for CLIENT_READY)...");

            yield return new WaitForSeconds(0.3f); // Match the "GO!" pause on clients

            // Activate gameplay on server side (server is already in the scene)
            _inGameplayScene = true;
            Debug.Log($"[SPAWN-DEBUG] SERVER: _inGameplayScene set to TRUE. pendingSpawns={_pendingSpawns.Count}, " +
                      $"alreadySpawned={_spawnedPlayers.Count}, Runner={Runner != null}, RunnerIsRunning={Runner?.IsRunning}, " +
                      $"playerPrefab={(playerPrefab != null ? playerPrefab.name : "NULL")}");

            TryFindSpawnSpline();
            Debug.Log($"[SPAWN-DEBUG] SERVER: TryFindSpawnSpline done. spawnSpline={(spawnSpline != null ? spawnSpline.name : "NULL")}");

            // Send SCENE_LOAD to all clients FIRST (do NOT spawn yet)
            _clientsReady.Clear();
            _playersAwaitingReady.Clear();
            foreach (var player in Runner.ActivePlayers)
            {
                _playersAwaitingReady.Add(player);
                try
                {
                    Runner.SendReliableDataToPlayer(player, SCENE_LOAD_KEY, SCENE_LOAD_MAGIC);
                    Debug.Log($"[NetworkManager] SERVER: Sent SCENE_LOAD to player {player.PlayerId}");
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[NetworkManager] SERVER: Failed to send SCENE_LOAD to {player.PlayerId}: {ex.Message}");
                }
            }

            // Also start sending on the proven INPUT_DATA_KEY channel
            _serverSendingSceneLoad = true;
            _waitingForClientsReady = true;
            _sceneLoadSignalStartTime = Time.time;

            Debug.Log($"[SPAWN-DEBUG] SERVER: Waiting for CLIENT_READY from {_playersAwaitingReady.Count} players (coroutine path)...");

            // Wait for all clients to report ready (or timeout)
            float waitStart = Time.time;
            while (Time.time - waitStart < CLIENT_READY_TIMEOUT)
            {
                int connectedWaiting = 0;
                int connectedReady = 0;
                foreach (var p in _playersAwaitingReady)
                {
                    bool connected = false;
                    foreach (var ap in Runner.ActivePlayers)
                    {
                        if (ap == p) { connected = true; break; }
                    }
                    if (!connected) continue;
                    connectedWaiting++;
                    if (_clientsReady.Contains(p)) connectedReady++;
                }

                if (connectedWaiting == 0)
                {
                    Debug.LogWarning("[SPAWN-DEBUG] SERVER: All players disconnected while waiting for CLIENT_READY (coroutine). Aborting.");
                    _waitingForClientsReady = false;
                    _serverSendingSceneLoad = false;
                    yield break;
                }

                if (connectedReady == connectedWaiting)
                {
                    Debug.Log($"[SPAWN-DEBUG] SERVER: All clients reported READY in {Time.time - waitStart:F1}s (coroutine path)!");
                    break;
                }
                yield return null;
            }

            _waitingForClientsReady = false;
            _serverSendingSceneLoad = false;

            // NOW spawn all players (clients have their scenes loaded)
            ServerSpawnAllPendingPlayers();
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
            Debug.Log("[NetworkManager] Create Room clicked — requesting room from VPS...");
            if (statusText != null) statusText.text = "Creating room on server...";
            if (createRoomButton != null) createRoomButton.interactable = false;
            if (joinRoomButton != null) joinRoomButton.interactable = false;
            StartCoroutine(CreateRoomOnVPS());
        }

        /// <summary>
        /// Sends HTTP POST to the VPS RoomManager to create a new room,
        /// then joins that room as a client.
        /// </summary>
        private IEnumerator CreateRoomOnVPS()
        {
            string url = $"{vpsRoomApiUrl.TrimEnd('/')}/create";
            Debug.Log($"[NetworkManager] POST {url}");

            using (var request = new UnityWebRequest(url, "POST"))
            {
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.timeout = 10;

                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"[NetworkManager] VPS room creation failed: {request.error}");
                    if (statusText != null) statusText.text = $"Server error: {request.error}";
                    if (createRoomButton != null) createRoomButton.interactable = true;
                    if (joinRoomButton != null) joinRoomButton.interactable = true;
                    yield break;
                }

                // Parse response: {"code":"ABCD"}
                string json = request.downloadHandler.text;
                Debug.Log($"[NetworkManager] VPS response: {json}");

                string code = ParseCodeFromJson(json);
                if (string.IsNullOrEmpty(code))
                {
                    Debug.LogError($"[NetworkManager] Could not parse room code from: {json}");
                    if (statusText != null) statusText.text = "Failed to create room";
                    if (createRoomButton != null) createRoomButton.interactable = true;
                    if (joinRoomButton != null) joinRoomButton.interactable = true;
                    yield break;
                }

                // Join the room as a client (we're the leader/creator)
                Debug.Log($"[NetworkManager] Room created on VPS: {code} — connecting as client (leader)...");
                CurrentRoomCode = code;
                _connectedToDedicatedServer = true;
                _roomFlowActive = true;
                _isRoomCreator = true;
                if (statusText != null) statusText.text = $"Room {code} created! Connecting...";
                StartClient();
            }
        }

        /// <summary>
        /// Simple JSON parser to extract "code" field from {"code":"XXXX"}.
        /// Avoids dependency on JsonUtility for this trivial case.
        /// </summary>
        private static string ParseCodeFromJson(string json)
        {
            // Look for "code":"VALUE"
            int idx = json.IndexOf("\"code\"");
            if (idx < 0) return null;
            int colonIdx = json.IndexOf(':', idx);
            if (colonIdx < 0) return null;
            int firstQuote = json.IndexOf('"', colonIdx);
            if (firstQuote < 0) return null;
            int secondQuote = json.IndexOf('"', firstQuote + 1);
            if (secondQuote < 0) return null;
            return json.Substring(firstQuote + 1, secondQuote - firstQuote - 1);
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
            // All rooms are on the dedicated server now — always true
            _connectedToDedicatedServer = true;
            _roomFlowActive = true;
            _isRoomCreator = false;
            Debug.Log($"[NetworkManager] Joining room with code: {CurrentRoomCode} (dedicated server)");
            if (statusText != null) statusText.text = $"Joining room {code}...";
            if (createRoomButton != null) createRoomButton.interactable = false;
            if (joinRoomButton != null) joinRoomButton.interactable = false;
            StartClient();
        }

        public async void StartAutoHostOrClient()
        {
            Debug.Log("[NetworkManager] Starting as AutoHostOrClient...");
            await StartGame(GameMode.AutoHostOrClient);
        }
        
        // --- RAW DATA SCENE LOAD COMMAND ---
        // HOST sends this to all clients when Enter Battle is clicked.
        // Clients receive it in OnReliableDataReceived and load the gameplay scene.
        private static readonly ReliableKey SCENE_LOAD_KEY = ReliableKey.FromInts(0x47, 0x56, 0x53, 0x43); // "GVSC"
        private static readonly byte[] SCENE_LOAD_MAGIC = { 0x4C, 0x4F, 0x41, 0x44 }; // "LOAD"

        // HOST sends this to tell clients to start the countdown timer
        private static readonly ReliableKey COUNTDOWN_KEY = ReliableKey.FromInts(0x47, 0x56, 0x43, 0x44); // "GVCD"
        private static readonly byte[] COUNTDOWN_MAGIC = { 0x43, 0x4E, 0x54, 0x44 }; // "CNTD"

        // CLIENT sends this to dedicated server to request match start (replaces host clicking Enter Battle)
        private static readonly ReliableKey START_MATCH_KEY = ReliableKey.FromInts(0x47, 0x56, 0x53, 0x4D); // "GVSM"
        private static readonly byte[] START_MATCH_MAGIC = { 0x53, 0x54, 0x52, 0x54 }; // "STRT"

        // CLIENT sends this to server after finishing gameplay scene load, telling server it's safe to spawn
        private static readonly ReliableKey CLIENT_READY_KEY = ReliableKey.FromInts(0x47, 0x56, 0x43, 0x52); // "GVCR"
        private static readonly byte[] CLIENT_READY_MAGIC = { 0x52, 0x44, 0x59, 0x21 }; // "RDY!"

        /// <summary>
        /// Magic number for CLIENT_READY signal sent via 37-byte INPUT_DATA_KEY channel (proven delivery).
        /// Clients send this after their gameplay scene finishes loading so the server knows it's safe to spawn.
        /// </summary>
        private const int CLIENT_READY_SIGNAL_MAGIC = 777777;

        private const int COUNTDOWN_SECONDS = 3;

        /// <summary>
        /// Button bit used to embed START_MATCH in regular 37-byte input data.
        /// This is the most reliable delivery method — it piggybacks on the proven
        /// INPUT_DATA_KEY channel that Fusion always delivers, avoiding the separate
        /// ReliableKey channels that Fusion sometimes silently drops.
        /// </summary>
        private const int START_MATCH_BUTTON_BIT = 31;

        /// <summary>
        /// Reserved magic numbers for server→client match state signals.
        /// These are embedded in 37-byte packets on INPUT_DATA_KEY (the proven channel)
        /// because Fusion silently drops 4-byte messages on custom ReliableKeys.
        /// Sent every frame from the server to ensure at least one delivery per Fusion tick.
        /// </summary>
        private const int COUNTDOWN_SIGNAL_MAGIC = 999999;
        private const int SCENE_LOAD_SIGNAL_MAGIC = 888888;

        /// <summary>
        /// When true, the server sends COUNTDOWN signals to all clients every frame.
        /// Set by TriggerMatchStart(), cleared after spawn delay + buffer time.
        /// </summary>
        private bool _serverSendingCountdown = false;

        /// <summary>
        /// When true, the server sends SCENE_LOAD signals to all clients every frame.
        /// Set after countdown finishes, cleared after all clients report CLIENT_READY or timeout.
        /// </summary>
        private bool _serverSendingSceneLoad = false;

        /// <summary>
        /// Time.time when _serverSendingSceneLoad was enabled. Used for timeout fallback.
        /// </summary>
        private float _sceneLoadSignalStartTime = 0f;

        /// <summary>
        /// Set of players who have sent CLIENT_READY (finished loading the gameplay scene).
        /// Server defers spawning until all expected players are ready (or timeout).
        /// </summary>
        private HashSet<PlayerRef> _clientsReady = new HashSet<PlayerRef>();

        /// <summary>
        /// Players that the server is waiting for CLIENT_READY from before spawning.
        /// Populated when SCENE_LOAD is sent, cleared once all respond or timeout.
        /// </summary>
        private HashSet<PlayerRef> _playersAwaitingReady = new HashSet<PlayerRef>();

        /// <summary>
        /// When true, the server has sent SCENE_LOAD and is waiting for CLIENT_READY from all players
        /// before spawning. Set by countdown finish, cleared after spawn.
        /// </summary>
        private bool _waitingForClientsReady = false;

        /// <summary>
        /// Timeout in seconds for waiting for CLIENT_READY. If a client hasn't responded
        /// by this time, spawn anyway to avoid infinite hangs.
        /// </summary>
        private const float CLIENT_READY_TIMEOUT = 15f;

        /// <summary>
        /// When true, the client is sending CLIENT_READY signals every frame to ensure delivery.
        /// Set after the client finishes loading the gameplay scene, cleared after a few seconds.
        /// </summary>
        private bool _clientSendingReady = false;

        /// <summary>
        /// When true, the client received SCENE_LOAD from server and needs to load the gameplay scene.
        /// This is handled in Update() to avoid calling SceneManager.LoadScene from inside
        /// OnReliableDataReceived (which corrupts Fusion's runner).
        /// </summary>
        private bool _pendingSceneLoadFromServer = false;
        private float _clientReadySendStartTime = 0f;

        /// <summary>
        /// Watchdog: tracks when _inGameplayScene was set true on the client.
        /// If CLIENT_READY hasn't been sent within CLIENT_READY_WATCHDOG_DELAY seconds,
        /// the watchdog in Update() force-sends it. Covers edge cases where
        /// OnUnitySceneLoaded fails or doesn't fire.
        /// </summary>
        private float _inGameplaySceneTimestamp = 0f;
        private bool _clientReadyWatchdogFired = false;
        private const float CLIENT_READY_WATCHDOG_DELAY = 3f;
        private const float CLIENT_READY_SEND_DURATION = 10f; // Send for 10 seconds (embedded in regular input, no extra cost)

        // --- RAW DATA INPUT TRANSPORT ---
        // Fusion's OnInput/GetInput pipeline and RPCs both silently fail in our setup.
        // This bypasses both entirely using Runner.SendReliableDataToServer(), which:
        // - Doesn't need IL weaving (unlike RPCs and INetworkInput)
        // - Runs from NetworkManager (always active, never disabled by DisableLocalInput)
        // - Uses a simple byte[] transport that Fusion routes to OnReliableDataReceived on the host
        private static readonly ReliableKey INPUT_DATA_KEY = ReliableKey.FromInts(0x47, 0x56, 0x49, 0x4E); // "GVIN"
        private static readonly ReliableKey START_MATCH_INPUT_KEY = ReliableKey.FromInts(0x47, 0x56, 0x53, 0x49); // "GVSI" — dedicated key for START_MATCH via input
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
            // --- DEDICATED SERVER: flag-based countdown (replaces coroutine to avoid crash) ---
            if (IsDedicatedServer)
            {
                UpdateMatchStartCountdown();
                return;
            }

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

                    // Encode signals in the magic number itself (proven delivery channel).
                    // Normal:       magic = playerId*1000 + 42  (e.g. 2042)
                    // START_MATCH:  magic = playerId*1000 + 99  (e.g. 2099)
                    // CLIENT_READY: magic = playerId*1000 + 77  (e.g. 2077)
                    // The server checks magic % 1000 to detect signals.
                    if (_clientSendingReady)
                    {
                        // CLIENT_READY piggybacks on regular input — no separate send needed
                        data.magicNumber = Runner.LocalPlayer.PlayerId * 1000 + 77;
                    }
                    else if (_sendStartMatchViaInput)
                    {
                        data.magicNumber = Runner.LocalPlayer.PlayerId * 1000 + 99;
                        data.buttons.Set(START_MATCH_BUTTON_BIT, true); // Also set bit 31 as backup
                        if (_startMatchSendAttempts <= 3)
                        {
                            Debug.Log($"[MATCH-DEBUG] CLIENT: Embedding START_MATCH in magic={data.magicNumber} (% 1000 == 99) " +
                                      $"+ bit 31. buttons.Bits=0x{data.buttons.Bits:X8}, attempt #{_startMatchSendAttempts}");
                        }
                    }
                    else
                    {
                        data.magicNumber = Runner.LocalPlayer.PlayerId * 1000 + 42;
                    }

                    byte[] bytes = SerializeInput(data);

                    // If START_MATCH flag is active, ALSO send on a SEPARATE ReliableKey (38 bytes)
                    // This uses a different key so Fusion won't merge it with regular input
                    if (_sendStartMatchViaInput)
                    {
                        byte[] startBytes = new byte[bytes.Length + 1];
                        System.Buffer.BlockCopy(bytes, 0, startBytes, 0, bytes.Length);
                        startBytes[bytes.Length] = 0xFF; // Marker byte
                        try
                        {
                            Runner.SendReliableDataToServer(START_MATCH_INPUT_KEY, startBytes);
                        }
                        catch (System.Exception ex)
                        {
                            Debug.LogError($"[NetworkManager] CLIENT: Failed to send START_MATCH via input key: {ex.Message}");
                        }
                        _startMatchSendAttempts++;
                        if (_startMatchSendAttempts >= START_MATCH_SEND_REPEATS)
                        {
                            _sendStartMatchViaInput = false;
                            Debug.Log($"[NetworkManager] CLIENT: START_MATCH signal sent {_startMatchSendAttempts} times via dedicated key, stopping.");
                        }
                    }

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

            // --- CLIENT: Deferred scene load from SCENE_LOAD signal ---
            // OnReliableDataReceived sets _pendingSceneLoadFromServer=true instead of calling
            // SceneManager.LoadScene directly (which would corrupt Fusion's runner).
            if (_pendingSceneLoadFromServer && !_inGameplayScene)
            {
                _pendingSceneLoadFromServer = false;
                _manualSceneLoadRequested = true;
                _inGameplayScene = true;
                _inGameplaySceneTimestamp = Time.time;
                _clientReadyWatchdogFired = false;
                Debug.Log($"[SPAWN-DEBUG] CLIENT: Executing deferred ASYNC scene load for '{gameplaySceneName}'...");
                ShowLoadingScreen();
                HideAllMenuUI();
                // Use async load so game loop keeps running and Fusion connection stays alive
                UnityEngine.SceneManagement.SceneManager.LoadSceneAsync(gameplaySceneName);
            }

            // --- CLIENT: CLIENT_READY is now embedded in regular input magic (% 1000 == 77) ---
            // No separate send needed. The _clientSendingReady flag modifies the magic number
            // in the regular input send above. Just handle the timeout/expiry here.
            if (_clientSendingReady && Time.time - _clientReadySendStartTime > CLIENT_READY_SEND_DURATION)
            {
                _clientSendingReady = false;
                Debug.Log("[SPAWN-DEBUG] CLIENT: Stopped sending CLIENT_READY signals (duration expired).");
            }

            // --- CLIENT: WATCHDOG — force-send CLIENT_READY if scene loaded but it was never sent ---
            // This catches ALL edge cases: OnUnitySceneLoaded didn't fire, threw an exception,
            // scene name mismatch, _manualSceneLoadRequested race condition, etc.
            // Fires once, CLIENT_READY_WATCHDOG_DELAY seconds after _inGameplayScene was set true.
            if (_inGameplayScene && !_clientSendingReady && !_clientReadyWatchdogFired
                && _inGameplaySceneTimestamp > 0f
                && Time.time - _inGameplaySceneTimestamp > CLIENT_READY_WATCHDOG_DELAY
                && Runner != null && Runner.IsClient && Runner.IsRunning)
            {
                _clientReadyWatchdogFired = true;
                Debug.LogWarning($"[CTL-DIAG] WATCHDOG: _inGameplayScene=true for {CLIENT_READY_WATCHDOG_DELAY}s " +
                                 $"but CLIENT_READY was NEVER sent! Force-sending now.");

                // Channel 1: 4-byte
                try { Runner.SendReliableDataToServer(CLIENT_READY_KEY, CLIENT_READY_MAGIC); }
                catch (System.Exception ex) { Debug.LogWarning($"[CTL-DIAG] WATCHDOG 4-byte failed: {ex.Message}"); }

                // Channel 2: 37-byte
                try
                {
                    var readyData = new PlayerInputData();
                    readyData.magicNumber = CLIENT_READY_SIGNAL_MAGIC;
                    byte[] readyBytes = SerializeInput(readyData);
                    Runner.SendReliableDataToServer(INPUT_DATA_KEY, readyBytes);
                }
                catch (System.Exception ex) { Debug.LogWarning($"[CTL-DIAG] WATCHDOG 37-byte failed: {ex.Message}"); }

                // Channel 3: Embed in regular input
                _clientSendingReady = true;
                _clientReadySendStartTime = Time.time;
                Debug.Log($"[CTL-DIAG] WATCHDOG: _clientSendingReady=true, will embed magic%1000==77 for {CLIENT_READY_SEND_DURATION}s");
            }

            // --- CLIENT: Detect ship spawn and hide loading screen ---
            if (_waitingForShipToAppear && Runner != null && Runner.IsRunning)
            {
                float loadingElapsed = Time.time - _loadingScreenShownTime;

                try
                {
                    // GetPlayerObject requires SetPlayerObject which we never call.
                    // Instead, find any NetworkObject with our InputAuthority.
                    bool found = false;
                    int totalObjects = 0;
                    foreach (var no in Runner.GetAllNetworkObjects())
                    {
                        totalObjects++;
                        if (no != null && no.InputAuthority == Runner.LocalPlayer && no.gameObject != this.gameObject)
                        {
                            Debug.Log($"[NetworkManager] CLIENT: Local player ship detected ({no.name})! Hiding loading screen after {loadingElapsed:F1}s.");
                            HideLoadingScreen();
                            found = true;
                            break;
                        }
                    }

                    // Periodic diagnostic log (every ~60 frames ≈ once per second)
                    if (!found)
                    {
                        _shipDetectLogCounter++;
                        if (_shipDetectLogCounter % 60 == 1)
                        {
                            Debug.Log($"[SHIP-DETECT] Waiting for ship... elapsed={loadingElapsed:F1}s, " +
                                      $"totalNetworkObjects={totalObjects}, LocalPlayer={Runner.LocalPlayer}, " +
                                      $"IsRunning={Runner.IsRunning}, IsClient={Runner.IsClient}");
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    // Log the exception so we can diagnose instead of silently hiding the error
                    _shipDetectLogCounter++;
                    if (_shipDetectLogCounter % 60 == 1)
                    {
                        Debug.LogWarning($"[SHIP-DETECT] Exception in ship detection (elapsed={loadingElapsed:F1}s): {ex.Message}");
                    }
                }

                // TIMEOUT: Force-hide loading screen if it's been showing too long.
                // The server force-spawns after 15s, so by 25s something is clearly wrong.
                // Don't leave the player stuck on a loading screen forever.
                if (loadingElapsed > LOADING_SCREEN_TIMEOUT)
                {
                    Debug.LogWarning($"[NetworkManager] CLIENT: Loading screen TIMEOUT after {loadingElapsed:F1}s! " +
                                     $"Force-hiding loading screen. Ship detection may have failed.");
                    HideLoadingScreen();
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

            // DIAGNOSTIC: Compare raw buttonsBits vs reconstructed Bits
            if (buttonsBits != 0 && data.buttons.Bits != buttonsBits)
            {
                Debug.LogWarning($"[MATCH-DEBUG] DeserializeInput: MISMATCH! raw buttonsBits=0x{buttonsBits:X8} but after Set loop, buttons.Bits=0x{data.buttons.Bits:X8}");
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
        /// Start as Client and join existing session on the dedicated server.
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
        /// Called by RoomManager to start this NetworkManager as a server for a specific room.
        /// Sets up the room code, marks as dedicated server, and starts Fusion in Server mode.
        /// </summary>
        /// <param name="roomCode">The 4-character room code for the Fusion session name.</param>
        /// <param name="maxPlayers">Max players allowed in this room.</param>
        /// <param name="callback">Called with true on success, false on failure.</param>
        public async void StartServerForRoom(string roomCode, int maxPlayers, Action<bool> callback)
        {
            _isRoomManagerControlled = true;
            _roomFlowActive = true;
            _roomStartCallback = callback;
            IsDedicatedServer = true;
            // Do NOT spawn ships yet — wait until client clicks Enter Battle
            // and sends START_MATCH. If we spawn immediately, Fusion replicates
            // the ship objects to the client before they're ready, and the player
            // can fire/hear sounds while still on the lobby UI.
            _inGameplayScene = false;
            CurrentRoomCode = roomCode;
            this.maxPlayers = maxPlayers;

            Debug.Log($"[NetworkManager] StartServerForRoom: code={roomCode}, maxPlayers={maxPlayers}");

            try
            {
                await StartGame(GameMode.Server);

                bool success = Runner != null && Runner.IsRunning;
                Debug.Log($"[NetworkManager] StartServerForRoom result: success={success}");

                // The gameplay scene is ALREADY loaded on the VPS (Web3Bootstrap loaded it
                // before RoomManager created this room). OnUnitySceneLoaded won't fire because
                // no new scene load happens. We must manually register all scene-placed
                // NetworkObjects (BattleZoneController, RandomPowerSphere, etc.) with this
                // Runner so their Spawned() fires and [Networked] properties sync to clients.
                if (success)
                {
                    var activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                    Debug.Log($"[NetworkManager] StartServerForRoom: Registering scene NetworkObjects for '{activeScene.name}'...");
                    try
                    {
                        RegisterSceneNetworkObjects(activeScene);
                    }
                    catch (System.Exception regEx)
                    {
                        Debug.LogError($"[NetworkManager] StartServerForRoom: RegisterSceneNetworkObjects failed: {regEx}");
                    }
                }

                callback?.Invoke(success);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NetworkManager] StartServerForRoom exception: {ex}");
                callback?.Invoke(false);
            }
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
                // Protect Runner from scene changes — especially on dedicated server where
                // Web3Bootstrap loads the gameplay scene (destroying Bootstrap scene objects)
                // while StartGame is still connecting asynchronously.
                DontDestroyOnLoad(Runner.gameObject);
                Debug.Log("[NetworkManager] Runner created with DontDestroyOnLoad");
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

            // --- Unique UserId per client ---
            // ParrelSync clones share the same project folder, so Photon generates the same
            // default UserId for both editors. When the second editor connects to the same
            // Fusion session, Photon treats it as the same user reconnecting and kicks the first.
            // Fix: generate a unique UserId per client instance.
            string uniqueUserId = System.Guid.NewGuid().ToString();
            #if UNITY_EDITOR
            if (ParrelSync.ClonesManager.IsClone())
            {
                // Append clone arg to make it deterministic but unique per clone
                string cloneArg = ParrelSync.ClonesManager.GetArgument();
                uniqueUserId = "clone_" + (string.IsNullOrEmpty(cloneArg) ? System.Guid.NewGuid().ToString() : cloneArg);
            }
            else
            {
                uniqueUserId = "editor_main_" + System.Guid.NewGuid().ToString().Substring(0, 8);
            }
            #endif
            var authValues = new Fusion.Photon.Realtime.AuthenticationValues(uniqueUserId);
            Debug.Log($"[NetworkManager] Photon UserId set to: {uniqueUserId}");

            // Use room code as session name. Dedicated server and AutoHostOrClient fall back to "GV2S".
            // Must be uppercase and exactly 4 chars — room code UI only accepts 4-char codes.
            string sessionName = !string.IsNullOrEmpty(CurrentRoomCode) ? CurrentRoomCode : "GV2S";

            Debug.Log($"[NetworkManager] Starting game. Mode={mode}, Region={fixedRegion}, " +
                      $"Session={sessionName}, MaxPlayers={maxPlayers}, " +
                      $"AppID=...{appId.Substring(Math.Max(0, appId.Length - 4))}, " +
                      $"AppVersion='{appVersion}', UseNameServer={appSettings.UseNameServer}");
            _lastError = $"Starting... Region: {fixedRegion}"; // Show status in UI

            // --- Scene Manager decision ---
            // FIRST: Remove ANY existing INetworkSceneManager from the Runner's GameObject.
            // The Runner prefab or Fusion bootstrap may have pre-attached a NetworkSceneManagerDefault.
            // If we don't remove it, both ours and the existing one coexist, and Fusion uses the wrong one.
            var existingSceneManagers = Runner.gameObject.GetComponents<INetworkSceneManager>();
            foreach (var existing in existingSceneManagers)
            {
                Debug.Log($"[NetworkManager] Removing pre-existing {existing.GetType().Name} from Runner GO");
                Destroy((Component)existing);
            }

            INetworkSceneManager sceneManager;
            if (!_isRoomManagerControlled && !_connectedToDedicatedServer && !_roomFlowActive)
            {
                sceneManager = Runner.gameObject.AddComponent<NetworkSceneManagerDefault>();
                Debug.Log("[NetworkManager] Using NetworkSceneManagerDefault on Runner GO (non-room flow)");
            }
            else
            {
                sceneManager = Runner.gameObject.AddComponent<NoOpSceneManager>();
                Debug.Log($"[NetworkManager] Using NoOpSceneManager on Runner GO ({Runner.gameObject.name}) — scene loading handled manually");
            }

            // Belt-and-suspenders: also remove from NetworkManager GO in case old code left one there
            var staleManagers = gameObject.GetComponents<INetworkSceneManager>();
            foreach (var stale in staleManagers)
            {
                Debug.Log($"[NetworkManager] Removing stale {stale.GetType().Name} from NetworkManager GO");
                Destroy((Component)stale);
            }

            StartGameResult result;
            try
            {
                result = await Runner.StartGame(new StartGameArgs
                {
                    GameMode = mode,
                    SessionName = sessionName,
                    PlayerCount = maxPlayers,
                    Scene = sceneInfo,
                    SceneManager = sceneManager,
                    CustomPhotonAppSettings = appSettings,
                    AuthValues = authValues
                });
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[NetworkManager] EXCEPTION during StartGame: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
                _lastError = $"Exception: {ex.Message}";
                return;
            }
            Debug.Log($"[NetworkManager] StartGame returned: Ok={result.Ok}, ShutdownReason={result.ShutdownReason}");

            // Post-StartGame cleanup: Fusion's StartGame may have auto-added a NetworkSceneManagerDefault
            // even though we passed our own. Remove any that aren't ours.
            if (Runner != null && sceneManager is NoOpSceneManager)
            {
                var postManagers = Runner.gameObject.GetComponents<NetworkSceneManagerDefault>();
                foreach (var pm in postManagers)
                {
                    Debug.Log($"[NetworkManager] POST-STARTGAME: Removing auto-added NetworkSceneManagerDefault from Runner");
                    DestroyImmediate(pm);
                }
            }

            // --- DIAGNOSTIC: Dump ALL INetworkSceneManager components on Runner and this GO ---
            if (Runner != null)
            {
                var runnerSMs = Runner.gameObject.GetComponents<INetworkSceneManager>();
                Debug.Log($"[SCENE-DIAG] Runner GO '{Runner.gameObject.name}' has {runnerSMs.Length} INetworkSceneManager(s):");
                foreach (var sm in runnerSMs)
                    Debug.Log($"[SCENE-DIAG]   - {sm.GetType().Name} (instanceID={((Component)sm).GetInstanceID()})");

                var mySMs = gameObject.GetComponents<INetworkSceneManager>();
                Debug.Log($"[SCENE-DIAG] NetworkManager GO '{gameObject.name}' has {mySMs.Length} INetworkSceneManager(s):");
                foreach (var sm in mySMs)
                    Debug.Log($"[SCENE-DIAG]   - {sm.GetType().Name} (instanceID={((Component)sm).GetInstanceID()})");

                // Check ALL GameObjects in scene for NetworkSceneManagerDefault
                var allDefaults = FindObjectsOfType<NetworkSceneManagerDefault>();
                Debug.Log($"[SCENE-DIAG] Total NetworkSceneManagerDefault in scene: {allDefaults.Length}");
                foreach (var d in allDefaults)
                    Debug.Log($"[SCENE-DIAG]   - on GO '{d.gameObject.name}' (instanceID={d.GetInstanceID()})");

                Debug.Log($"[SCENE-DIAG] Runner.gameObject == this.gameObject? {Runner.gameObject == gameObject}");
                Debug.Log($"[SCENE-DIAG] _connectedToDedicatedServer={_connectedToDedicatedServer}, _isRoomManagerControlled={_isRoomManagerControlled}, _roomFlowActive={_roomFlowActive}");
            }

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
                      $"gameplaySceneName: {gameplaySceneName}, pending: {_pendingSpawns.Count}, " +
                      $"STACK TRACE:\n{System.Environment.StackTrace}");

            if (!string.IsNullOrEmpty(gameplaySceneName) && scene.name == gameplaySceneName)
            {
                // CRITICAL GUARD: Only treat this as an intentional gameplay start if WE
                // requested the scene load (via Enter Battle flow). Fusion's auto scene sync
                // may load the gameplay scene behind the UI before the player clicks Enter Battle.
                // Without this guard, _inGameplayScene gets set true prematurely and ships spawn
                // before the player is ready.
                if (!_manualSceneLoadRequested)
                {
                    Debug.LogWarning($"[NetworkManager] Unity loaded gameplay scene '{scene.name}' but _manualSceneLoadRequested=false — " +
                                     "this was Fusion auto-sync, NOT our Enter Battle flow. IGNORING spawn/setup.");
                    return;
                }

                Debug.Log($"[NetworkManager] Gameplay scene '{scene.name}' loaded via manual request — activating gameplay.");
                _inGameplayScene = true;
                _manualSceneLoadRequested = false; // Reset for next time

                // Auto-find the spawn spline in the newly loaded gameplay scene
                TryFindSpawnSpline();

                // Register scene-placed NetworkObjects with Fusion.
                // Because we load scenes manually (SceneManager.LoadScene) instead of through
                // Fusion's NetworkSceneManagerDefault, Fusion doesn't know about NetworkObjects
                // placed in the scene (like BattleZoneController). This tells Fusion to adopt them
                // so their Spawned() callback fires and [Networked] properties work.
                // WRAPPED in try-catch: RegisterSceneNetworkObjects must NEVER prevent CLIENT_READY
                // from being sent. If it throws (e.g., scene objects in bad state during async reload),
                // we log the error but continue to the critical CLIENT_READY sends below.
                try
                {
                    RegisterSceneNetworkObjects(scene);
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[NetworkManager] RegisterSceneNetworkObjects THREW — continuing to CLIENT_READY sends. Error: {ex}");
                }

                // CLIENT: Send CLIENT_READY to the server so it knows we've loaded the scene
                // and it's safe to spawn our player object. Send on ALL channels for reliability.
                if (Runner != null && Runner.IsClient)
                {
                    Debug.Log($"[CTL-DIAG] CLIENT: Gameplay scene loaded! Sending CLIENT_READY on ALL channels. " +
                              $"LocalPlayer={Runner.LocalPlayer}, IsRunning={Runner.IsRunning}");

                    // Channel 1: 4-byte CLIENT_READY_KEY ("RDY!")
                    try
                    {
                        Runner.SendReliableDataToServer(CLIENT_READY_KEY, CLIENT_READY_MAGIC);
                        Debug.Log("[SPAWN-DEBUG] CLIENT: Sent CLIENT_READY via 4-byte key.");
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogWarning($"[SPAWN-DEBUG] CLIENT: Failed to send CLIENT_READY via 4-byte key: {ex.Message}");
                    }

                    // Channel 2: 37-byte CLIENT_READY_SIGNAL_MAGIC on INPUT_DATA_KEY (proven channel)
                    try
                    {
                        var readyData = new PlayerInputData();
                        readyData.magicNumber = CLIENT_READY_SIGNAL_MAGIC;
                        byte[] readyBytes = SerializeInput(readyData);
                        Runner.SendReliableDataToServer(INPUT_DATA_KEY, readyBytes);
                        Debug.Log("[SPAWN-DEBUG] CLIENT: Sent CLIENT_READY via 37-byte INPUT_DATA_KEY (magic=777777).");
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogWarning($"[SPAWN-DEBUG] CLIENT: Failed to send CLIENT_READY via 37-byte key: {ex.Message}");
                    }

                    // Channel 3: Embed CLIENT_READY in regular input magic (% 1000 == 77) every frame
                    _clientSendingReady = true;
                    _clientReadySendStartTime = Time.time;
                    _clientReadyWatchdogFired = true; // Mark watchdog as satisfied — CLIENT_READY was sent
                    Debug.Log($"[CTL-DIAG] CLIENT: _clientSendingReady=true, will embed magic%1000==77 for {CLIENT_READY_SEND_DURATION}s");
                }

                // SERVER: Delay spawning by 1 frame so all scene objects (VehicleCamera, etc.)
                // have their Start() called. sceneLoaded fires after Awake() but before Start().
                if (Runner != null && Runner.IsServer)
                {
                    StartCoroutine(SpawnPendingPlayersNextFrame());
                }
            }
        }

        /// <summary>
        /// Finds all NetworkObjects placed in the scene and registers them with Fusion.
        /// This is necessary because we load scenes manually (bypassing Fusion's scene manager),
        /// so Fusion doesn't automatically discover scene-placed NetworkObjects.
        /// Without this, components like BattleZoneController never get Spawned() called.
        /// </summary>
        private void RegisterSceneNetworkObjects(UnityEngine.SceneManagement.Scene scene)
        {
            if (Runner == null)
            {
                Debug.LogWarning("[NetworkManager] RegisterSceneNetworkObjects — Runner is null, skipping.");
                return;
            }

            // Collect all NetworkObjects in the loaded scene (same approach Fusion uses internally)
            var sceneObjects = scene.GetComponents<NetworkObject>(includeInactive: true, out var rootObjects);

            if (sceneObjects.Length == 0)
            {
                Debug.Log("[NetworkManager] RegisterSceneNetworkObjects — no NetworkObjects found in scene.");
                return;
            }

            // Sort by SortKey for deterministic ordering (must match on host and clients)
            System.Array.Sort(sceneObjects, NetworkObjectSortKeyComparer.Instance);

            var sceneRef = SceneRef.FromIndex(scene.buildIndex);
            int registered = Runner.RegisterSceneObjects(sceneRef, sceneObjects);
            Debug.Log($"[NetworkManager] RegisterSceneNetworkObjects — registered {registered}/{sceneObjects.Length} " +
                      $"NetworkObjects in scene '{scene.name}' (SceneRef={sceneRef})");
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

            Debug.Log($"[NetworkManager] Spawning {playersToSpawn.Count} players after 1-frame delay " +
                      $"(already spawned: {_spawnedPlayers.Count})");
            foreach (var player in playersToSpawn)
            {
                // Use _spawnedPlayers.Count as slot — it grows after each SpawnPlayer call,
                // so each player gets the next available slot. This handles the case where
                // OnPlayerJoined already spawned some players (e.g., host) at earlier slots.
                SpawnPlayer(Runner, player, _spawnedPlayers.Count);
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
            Debug.Log($"[SPAWN-DEBUG] OnPlayerJoined: player={player.PlayerId}, IsServer={runner.IsServer}, " +
                      $"inGameplayScene={_inGameplayScene}, IsDedicatedServer={IsDedicatedServer}, " +
                      $"playerPrefab={(playerPrefab != null ? playerPrefab.name : "NULL")}, " +
                      $"pendingSpawns={_pendingSpawns.Count}, spawnedPlayers={_spawnedPlayers.Count}, " +
                      $"isRoomManagerControlled={_isRoomManagerControlled}, " +
                      $"instanceID={GetInstanceID()}");

            if (runner.IsServer && playerPrefab != null)
            {
                // DUAL INSTANCE: Always queue on the LIVE instance so UpdateMatchStartCountdown sees the player
                var liveInst = (Instance != null) ? Instance : this;

                // Don't spawn ships in the menu/lobby scene — queue them for when gameplay scene loads
                if (!liveInst._inGameplayScene)
                {
                    Debug.Log($"[SPAWN-DEBUG] Player {player.PlayerId} QUEUED in _pendingSpawns (inGameplayScene=false). " +
                              $"pendingSpawns will be {liveInst._pendingSpawns.Count + 1} after add. " +
                              $"thisID={GetInstanceID()}, liveID={liveInst.GetInstanceID()}");
                    if (!liveInst._pendingSpawns.Contains(player))
                        liveInst._pendingSpawns.Add(player);
                    else
                        Debug.LogWarning($"[SPAWN-DEBUG] Player {player.PlayerId} was ALREADY in _pendingSpawns!");
                }
                // Server is waiting for CLIENT_READY — queue this late joiner too
                else if (liveInst._waitingForClientsReady)
                {
                    Debug.Log($"[SPAWN-DEBUG] Player {player.PlayerId} QUEUED in _pendingSpawns (waiting for CLIENT_READY). " +
                              $"Also added to _playersAwaitingReady.");
                    if (!liveInst._pendingSpawns.Contains(player))
                        liveInst._pendingSpawns.Add(player);
                    liveInst._playersAwaitingReady.Add(player);
                }
                else
                {
                    Debug.Log($"[SPAWN-DEBUG] Player {player.PlayerId} spawning IMMEDIATELY (inGameplayScene=true)");
                    SpawnPlayer(runner, player, liveInst._spawnedPlayers.Count);
                }
            }
            else
            {
                Debug.LogWarning($"[SPAWN-DEBUG] NOT spawning player {player.PlayerId} — IsServer={runner.IsServer}, " +
                                 $"playerPrefab={(playerPrefab != null ? "assigned" : "NULL")}");
            }

            // Show "Client has joined" on the host when a non-host player joins, then auto-hide.
            // Skip on dedicated server — no UI to show.
            if (!IsDedicatedServer && runner.IsServer && player != runner.LocalPlayer && clientJoinedText != null)
            {
                clientJoinedText.text = "Client has joined";
                clientJoinedText.gameObject.SetActive(true);
                Debug.Log("[NetworkManager] Client has joined — showing TMP notification");
                if (_hideClientJoinedCoroutine != null) StopCoroutine(_hideClientJoinedCoroutine);
                _hideClientJoinedCoroutine = StartCoroutine(HideClientJoinedAfterDelay(3f));
            }

            OnPlayerJoinedGame?.Invoke(runner, player);
        }

        private void SpawnPlayer(NetworkRunner runner, PlayerRef player, int slotIndex = -1)
        {
            Debug.Log($"[SPAWN-DEBUG] SpawnPlayer ENTER: player={player.PlayerId}, slotIndex={slotIndex}, " +
                      $"runner={runner != null}, runner.IsRunning={runner?.IsRunning}, " +
                      $"runner.IsServer={runner?.IsServer}, _spawnedPlayers.Count={_spawnedPlayers.Count}");

            // Guard: don't double-spawn if both OnUnitySceneLoaded and OnSceneLoadDone fire
            if (_spawnedPlayers.ContainsKey(player) && _spawnedPlayers[player] != null)
            {
                Debug.Log($"[SPAWN-DEBUG] Player {player.PlayerId} already has a ship ({_spawnedPlayers[player].name}) — skipping spawn");
                return;
            }

            if (_spawnedPlayers.ContainsKey(player) && _spawnedPlayers[player] == null)
            {
                Debug.LogWarning($"[SPAWN-DEBUG] Player {player.PlayerId} has NULL ship reference — removing stale entry and re-spawning");
                _spawnedPlayers.Remove(player);
            }

            // Use explicit slot index if provided, otherwise fall back to count of existing ships
            if (slotIndex < 0) slotIndex = _spawnedPlayers.Count;

            CalculateSplineSpawnPoints();
            int clampedIndex = Mathf.Clamp(slotIndex, 0, maxPlayers - 1);
            var spawnPos = _cachedSpawnPositions[clampedIndex];
            var spawnRot = _cachedSpawnRotations[clampedIndex];

            Debug.Log($"[SPAWN-DEBUG] Spawning player {player.PlayerId} at slot {clampedIndex}, " +
                      $"pos={spawnPos}, rot={spawnRot.eulerAngles}, splineFound={spawnSpline != null}, " +
                      $"playerPrefab={playerPrefab.name}");

            try
            {
                var playerObject = runner.Spawn(playerPrefab, spawnPos, spawnRot, inputAuthority: player);

                Debug.Log($"[SPAWN-DEBUG] runner.Spawn returned: {(playerObject != null ? playerObject.name : "NULL")} " +
                          $"for player {player.PlayerId}");

                if (playerObject != null)
                {
                    playerObject.AssignInputAuthority(player);
                    Debug.Log($"[SPAWN-DEBUG] Assigned InputAuthority to {player.PlayerId}, " +
                              $"authority is now: {playerObject.InputAuthority}");

                    if (!IsDedicatedServer && player == runner.LocalPlayer)
                    {
                        SetupCameraFollow(playerObject.gameObject);
                    }
                }
                else
                {
                    Debug.LogError($"[SPAWN-DEBUG] runner.Spawn returned NULL for player {player.PlayerId}! " +
                                   $"Check if playerPrefab is a valid NetworkObject with a NetworkRunner reference.");
                }

                _spawnedPlayers[player] = playerObject;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[SPAWN-DEBUG] EXCEPTION during runner.Spawn for player {player.PlayerId}: {ex.Message}\n{ex.StackTrace}");
            }
        }
        
        public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
        {
            Debug.Log($"[NetworkManager] Player {player.PlayerId} left");

            // Clean up pending spawn queue — prevents ghost players from blocking spawn logic
            _pendingSpawns.Remove(player);
            _playersAwaitingReady.Remove(player);
            _clientsReady.Remove(player);

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
            _pendingSpawns.Clear();
            _countdownActive = false;
            _connectedToDedicatedServer = false;
            _isRoomCreator = false;
            _sendStartMatchViaInput = false;
            _manualSceneLoadRequested = false;
            _serverSendingCountdown = false;
            _serverSendingSceneLoad = false;
            _waitingForClientsReady = false;
            _clientsReady.Clear();
            _playersAwaitingReady.Clear();
            _clientSendingReady = false;
            _pendingSceneLoadFromServer = false;
            _inGameplayScene = false;
            _inGameplaySceneTimestamp = 0f;
            _clientReadyWatchdogFired = false;
            _waitingForShipToAppear = false;
            _loadingScreenShownTime = 0f;
            _shipDetectLogCounter = 0;
            // Clean up loading screen so it doesn't persist into the next game
            if (_loadingScreenGO != null)
            {
                Destroy(_loadingScreenGO);
                _loadingScreenGO = null;
            }
            // NOTE: _roomFlowActive is intentionally NOT reset here.
            // If the Runner shuts down and restarts (unexpected reconnect),
            // we need to remember we're in a room flow so NoOpSceneManager is used
            // instead of NetworkSceneManagerDefault. _roomFlowActive is only cleared
            // when the user explicitly returns to the main lobby (ShowLobbyUI).
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

            // DEBUG: Log ALL 4-byte messages to diagnose delivery
            if (data.Count == 4)
            {
                byte[] debugBytes = new byte[4];
                System.Buffer.BlockCopy(data.Array, data.Offset, debugBytes, 0, 4);
                Debug.Log($"[SPAWN-DEBUG] SERVER: Received 4-byte message: [{debugBytes[0]},{debugBytes[1]},{debugBytes[2]},{debugBytes[3]}] = \"{System.Text.Encoding.ASCII.GetString(debugBytes)}\" from player {player.PlayerId}");
            }

            // --- START_MATCH COMMAND (exactly 4 bytes: "STRT") ---
            // Client sends this to dedicated server to request match start
            if (data.Count == START_MATCH_MAGIC.Length && runner.IsServer && IsDedicatedServer)
            {
                byte[] startBytes = new byte[data.Count];
                System.Buffer.BlockCopy(data.Array, data.Offset, startBytes, 0, data.Count);

                bool isStart = true;
                for (int i = 0; i < START_MATCH_MAGIC.Length; i++)
                {
                    if (startBytes[i] != START_MATCH_MAGIC[i]) { isStart = false; break; }
                }

                if (isStart)
                {
                    // Check guards on the LIVE instance (handles DUAL INSTANCE case)
                    var liveInst = (Instance != null) ? Instance : this;
                    Debug.Log($"[SPAWN-DEBUG] SERVER: Received START_MATCH (4-byte) from player {player.PlayerId}! " +
                              $"pendingSpawns={liveInst._pendingSpawns.Count}, spawnedPlayers={liveInst._spawnedPlayers.Count}, " +
                              $"inGameplayScene={liveInst._inGameplayScene}, countdownActive={liveInst._countdownActive}, " +
                              $"thisID={GetInstanceID()}, liveID={liveInst.GetInstanceID()}");

                    // Guard: only trigger once
                    if (liveInst._countdownActive || liveInst._inGameplayScene)
                    {
                        Debug.Log("[SPAWN-DEBUG] SERVER: SKIPPING 4-byte START_MATCH — already in countdown or gameplay");
                        return;
                    }

                    TriggerMatchStart(player);
                    return;
                }
            }

            // --- COUNTDOWN COMMAND (exactly 4 bytes: "CNTD") ---
            // Host/Server sends this to tell clients to start the countdown timer
            if (data.Count == COUNTDOWN_MAGIC.Length && !runner.IsServer)
            {
                byte[] countdownBytes = new byte[data.Count];
                System.Buffer.BlockCopy(data.Array, data.Offset, countdownBytes, 0, data.Count);

                bool isCountdown = true;
                for (int i = 0; i < COUNTDOWN_MAGIC.Length; i++)
                {
                    if (countdownBytes[i] != COUNTDOWN_MAGIC[i]) { isCountdown = false; break; }
                }

                if (isCountdown)
                {
                    Debug.Log("[NetworkManager] CLIENT: Received COUNTDOWN command from host! Starting countdown...");
                    StartCoroutine(CountdownThenLoad());
                    return;
                }
            }

            // --- SCENE LOAD COMMAND (exactly 4 bytes: "LOAD") ---
            // Host sends this to tell clients to load the gameplay scene
            if (data.Count == SCENE_LOAD_MAGIC.Length && !runner.IsServer)
            {
                byte[] bytes = new byte[data.Count];
                System.Buffer.BlockCopy(data.Array, data.Offset, bytes, 0, data.Count);

                bool isLoadCommand = true;
                for (int i = 0; i < SCENE_LOAD_MAGIC.Length; i++)
                {
                    if (bytes[i] != SCENE_LOAD_MAGIC[i]) { isLoadCommand = false; break; }
                }

                if (isLoadCommand)
                {
                    // Skip if already in gameplay scene or countdown is handling it
                    if (_inGameplayScene || _countdownActive)
                    {
                        Debug.Log($"[NetworkManager] CLIENT: Received SCENE_LOAD but already handled — " +
                                  $"inGameplay={_inGameplayScene}, countdown={_countdownActive}. Ignoring.");
                        return;
                    }
                    // CRITICAL: Do NOT call SceneManager.LoadScene from inside OnReliableDataReceived!
                    // Defer to Update() to avoid corrupting Fusion's runner.
                    Debug.Log($"[NetworkManager] CLIENT: Received SCENE_LOAD (4-byte) — deferring scene load to Update().");
                    _manualSceneLoadRequested = true;
                    _pendingSceneLoadFromServer = true;
                    return; // Don't process as input data
                }
            }

            // --- CLIENT_READY (exactly 4 bytes: "RDY!") ---
            // Client sends this after loading the gameplay scene, telling server it's safe to spawn
            if (data.Count == CLIENT_READY_MAGIC.Length && runner.IsServer)
            {
                byte[] readyBytes = new byte[data.Count];
                System.Buffer.BlockCopy(data.Array, data.Offset, readyBytes, 0, data.Count);

                bool isReady = true;
                for (int i = 0; i < CLIENT_READY_MAGIC.Length; i++)
                {
                    if (readyBytes[i] != CLIENT_READY_MAGIC[i]) { isReady = false; break; }
                }

                if (isReady)
                {
                    var liveInst = (Instance != null) ? Instance : this;
                    if (!liveInst._clientsReady.Contains(player))
                    {
                        liveInst._clientsReady.Add(player);
                        Debug.Log($"[SPAWN-DEBUG] SERVER: Received CLIENT_READY (4-byte) from player {player.PlayerId}! " +
                                  $"Ready: {liveInst._clientsReady.Count}/{liveInst._playersAwaitingReady.Count}");
                    }
                    return;
                }
            }

            // --- START_MATCH VIA DEDICATED KEY (38 bytes = 37 input + 1 marker) ---
            // Client sends on START_MATCH_INPUT_KEY (separate from INPUT_DATA_KEY) to avoid Fusion merging
            if (data.Count == 38 && runner.IsServer && IsDedicatedServer)
            {
                byte[] bytes = new byte[37];
                System.Buffer.BlockCopy(data.Array, data.Offset, bytes, 0, 37);
                var inputData = DeserializeInput(bytes);

                Debug.Log($"[SPAWN-DEBUG] SERVER: Received 38-byte START_MATCH packet from player {player.PlayerId}! magic={inputData.magicNumber}");

                var liveInst = (Instance != null) ? Instance : this;
                if (!liveInst._countdownActive && !liveInst._inGameplayScene)
                {
                    Debug.Log($"[SPAWN-DEBUG] SERVER: START_MATCH via DEDICATED KEY from player {player.PlayerId}! " +
                              $"pendingSpawns={liveInst._pendingSpawns.Count}, spawnedPlayers={liveInst._spawnedPlayers.Count}");
                    TriggerMatchStart(player);
                }

                // Also store as regular input
                _clientInputs[player] = inputData;
                RawRecvCount++;
                return;
            }

            // --- INPUT DATA (37 bytes) ---
            // Accept any reliable data with the right size
            if (data.Count >= 37)
            {
                byte[] bytes = new byte[data.Count];
                System.Buffer.BlockCopy(data.Array, data.Offset, bytes, 0, data.Count);

                // RAW HEX DUMP: Log bytes 24-32 (boost=24, buttons=25-28, magic=29-32)
                // Fires on first 5 packets OR whenever buttons bytes are non-zero
                if (runner.IsServer && IsDedicatedServer)
                {
                    bool hasNonZeroButtons = bytes[25] != 0 || bytes[26] != 0 || bytes[27] != 0 || bytes[28] != 0;
                    if (RawRecvCount <= 5 || hasNonZeroButtons)
                    {
                        string hexDump = "";
                        for (int hx = 24; hx < System.Math.Min(33, bytes.Length); hx++)
                            hexDump += bytes[hx].ToString("X2") + " ";
                        Debug.Log($"[MATCH-DEBUG] SERVER RAW BYTES [24..32] from player {player.PlayerId}: {hexDump} " +
                                  $"(len={data.Count}, hasButtons={hasNonZeroButtons})");
                    }
                }

                var inputData = DeserializeInput(bytes);

                // --- CLIENT: DETECT SERVER→CLIENT COUNTDOWN/SCENE_LOAD SIGNALS ---
                // These are 37-byte packets with reserved magic numbers sent by the server
                // on INPUT_DATA_KEY (the proven channel), because Fusion drops 4-byte packets
                // on custom ReliableKeys.
                if (!runner.IsServer && inputData.magicNumber == COUNTDOWN_SIGNAL_MAGIC)
                {
                    if (!_countdownActive)
                    {
                        Debug.Log("[NetworkManager] CLIENT: Received COUNTDOWN via 37-byte INPUT_DATA_KEY signal! Starting countdown...");
                        StartCoroutine(CountdownThenLoad());
                    }
                    return; // Don't process as regular input
                }

                if (!runner.IsServer && inputData.magicNumber == SCENE_LOAD_SIGNAL_MAGIC)
                {
                    if (!_inGameplayScene && !_countdownActive)
                    {
                        // CRITICAL: Do NOT call SceneManager.LoadScene from inside OnReliableDataReceived!
                        // Synchronous scene load inside a Fusion callback corrupts the runner's internal
                        // state, breaking all subsequent networking (client can't send data anymore).
                        // Instead, set a flag and let Update() handle the actual scene load.
                        Debug.Log($"[NetworkManager] CLIENT: Received SCENE_LOAD signal — deferring scene load to Update().");
                        _manualSceneLoadRequested = true;
                        _pendingSceneLoadFromServer = true;
                    }
                    else if (_countdownActive)
                    {
                        // Countdown is already running — it will handle scene loading via CountdownThenLoad().
                        // No need to do anything here.
                    }
                    return; // Don't process as regular input
                }

                // --- CLIENT_READY SIGNAL via 37-byte INPUT_DATA_KEY (proven channel) ---
                // Client sends this after loading the gameplay scene
                if (runner.IsServer && inputData.magicNumber == CLIENT_READY_SIGNAL_MAGIC)
                {
                    var liveInst = (Instance != null) ? Instance : this;
                    if (!liveInst._clientsReady.Contains(player))
                    {
                        liveInst._clientsReady.Add(player);
                        Debug.Log($"[SPAWN-DEBUG] SERVER: Received CLIENT_READY (37-byte signal) from player {player.PlayerId}! " +
                                  $"Ready: {liveInst._clientsReady.Count}/{liveInst._playersAwaitingReady.Count}");
                    }
                    return; // Don't process as regular input
                }

                // --- CLIENT_READY via magic number encoding (magic % 1000 == 77) ---
                // This is the PRIMARY delivery channel: CLIENT_READY piggybacks on regular input.
                if (inputData.magicNumber % 1000 == 77 && runner.IsServer && IsDedicatedServer)
                {
                    var liveInst = (Instance != null) ? Instance : this;
                    if (!liveInst._clientsReady.Contains(player))
                    {
                        liveInst._clientsReady.Add(player);
                        Debug.Log($"[SPAWN-DEBUG] SERVER: Received CLIENT_READY (magic%1000==77) from player {player.PlayerId}! " +
                                  $"Ready: {liveInst._clientsReady.Count}/{liveInst._playersAwaitingReady.Count}");
                    }
                    // Store as regular input (strip the signal)
                    inputData.magicNumber = (inputData.magicNumber / 1000) * 1000 + 42;
                    _clientInputs[player] = inputData;
                    RawRecvCount++;
                    return;
                }

                // --- LEGACY CHECK FOR START_MATCH SIGNAL (magic % 1000 == 99) ---
                if (inputData.magicNumber % 1000 == 99 && runner.IsServer && IsDedicatedServer)
                {
                    var liveInst = (Instance != null) ? Instance : this;
                    if (!liveInst._countdownActive && !liveInst._inGameplayScene)
                    {
                        Debug.Log($"[SPAWN-DEBUG] SERVER: Received START_MATCH via LEGACY INPUT CHANNEL from player {player.PlayerId}!");
                        TriggerMatchStart(player);
                    }

                    // Also store as regular input (strip the signal)
                    inputData.magicNumber = (inputData.magicNumber / 1000) * 1000 + 42;
                    _clientInputs[player] = inputData;
                    RawRecvCount++;
                    return;
                }

                // --- CHECK FOR START_MATCH VIA RAW BYTES (bypass NetworkButtons entirely) ---
                // Bit 31 of buttons is at byte offset 28 (little-endian: byte 25=bits0-7, 28=bits24-31)
                // Check raw byte 28 for 0x80 bit — this bypasses any potential NetworkButtons.Set() bug
                bool rawBit31 = (bytes[28] & 0x80) != 0;

                // Also check via NetworkButtons for comparison
                bool fusionBit31 = (inputData.buttons.Bits & (1 << START_MATCH_BUTTON_BIT)) != 0;

                if (runner.IsServer && IsDedicatedServer && (rawBit31 || fusionBit31 || inputData.buttons.Bits != 0))
                {
                    Debug.Log($"[MATCH-DEBUG] SERVER: Input from player {player.PlayerId}: " +
                              $"rawBit31={rawBit31}, fusionBit31={fusionBit31}, " +
                              $"buttons.Bits=0x{inputData.buttons.Bits:X8}, rawByte28=0x{bytes[28]:X2}, magic={inputData.magicNumber}");
                }

                if ((rawBit31 || fusionBit31) && runner.IsServer && IsDedicatedServer)
                {
                    var liveInst = (Instance != null) ? Instance : this;
                    if (!liveInst._countdownActive && !liveInst._inGameplayScene)
                    {
                        Debug.Log($"[SPAWN-DEBUG] SERVER: Received START_MATCH via BUTTON BIT from player {player.PlayerId}! " +
                                  $"rawBit31={rawBit31}, fusionBit31={fusionBit31}, " +
                                  $"pendingSpawns={liveInst._pendingSpawns.Count}, spawnedPlayers={liveInst._spawnedPlayers.Count}");
                        TriggerMatchStart(player);
                    }

                    // Clear the bit before storing as regular input
                    inputData.buttons.Set(START_MATCH_BUTTON_BIT, false);
                }

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
            Debug.Log($"[NetworkManager] OnSceneLoadDone — scene: {activeScene}, pending spawns: {_pendingSpawns.Count}, manualLoad={_manualSceneLoadRequested}");

            // Check if we've arrived in the gameplay scene
            if (!string.IsNullOrEmpty(gameplaySceneName) && activeScene == gameplaySceneName)
            {
                // Guard: only activate gameplay if WE requested the load OR if the server
                // has already activated gameplay via DedicatedServerCountdownThenLoad.
                // Without this, Fusion's OnSceneLoadDone fires at startup on the server
                // (already in gameplay scene) and would set _inGameplayScene=true + spawn ships
                // before any client has clicked Enter Battle.
                if (!_manualSceneLoadRequested && !_inGameplayScene)
                {
                    Debug.LogWarning($"[NetworkManager] OnSceneLoadDone for gameplay scene but _manualSceneLoadRequested=false " +
                                     $"and _inGameplayScene=false — IGNORING spawn/setup (waiting for Enter Battle).");
                    return;
                }

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
