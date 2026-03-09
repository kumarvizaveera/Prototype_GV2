using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading;
using UnityEngine;
using Fusion;

namespace GV.Network
{
    /// <summary>
    /// Manages multiple game rooms on the dedicated server.
    /// Each room is a separate Fusion session with its own NetworkRunner + NetworkManager.
    ///
    /// Exposes a lightweight HTTP API for clients to create/list/query rooms:
    ///   POST /create        → creates a new room, returns JSON { "code": "ABCD" }
    ///   GET  /rooms         → returns JSON array of active rooms
    ///   GET  /rooms/{code}  → returns JSON with room info (player count, status)
    ///   DELETE /rooms/{code} → shuts down a room (for admin/debug)
    ///
    /// Clients flow:
    ///   1. Client calls POST /create → gets room code "ABCD"
    ///   2. Client joins Fusion session "ABCD" as GameMode.Client
    ///   3. Other clients call GET /rooms to see active rooms, then join by code
    ///
    /// On the server side:
    ///   - RoomManager instantiates a NetworkManager per room
    ///   - Each NetworkManager gets its own NetworkRunner
    ///   - Rooms are cleaned up after all players leave or match ends
    /// </summary>
    public class RoomManager : MonoBehaviour
    {
        public static RoomManager Instance { get; private set; }

        [Header("Configuration")]
        [Tooltip("HTTP port for room management API")]
        [SerializeField] private int httpPort = 7350;

        [Tooltip("Maximum simultaneous rooms")]
        [SerializeField] private int maxRooms = 10;

        [Tooltip("Maximum players per room")]
        [SerializeField] private int maxPlayersPerRoom = 4;

        [Tooltip("NetworkManager prefab to instantiate per room")]
        [SerializeField] private GameObject networkManagerPrefab;

        [Tooltip("Seconds to wait before cleaning up an empty room")]
        [SerializeField] private float emptyRoomTimeout = 30f;

        // --- Room tracking ---
        private class RoomInfo
        {
            public string Code;
            public NetworkManager Manager;
            public NetworkRunner Runner;
            public DateTime CreatedAt;
            public DateTime LastPlayerLeftAt;
            public bool IsEmpty => Manager != null && Manager.Runner != null &&
                                   Manager.Runner.IsRunning && Manager.Runner.ActivePlayers != null &&
                                   !HasPlayers();
            public RoomState State;

            public bool HasPlayers()
            {
                if (Manager == null || Manager.Runner == null || !Manager.Runner.IsRunning) return false;
                foreach (var _ in Manager.Runner.ActivePlayers) return true;
                return false;
            }

            public int PlayerCount
            {
                get
                {
                    if (Manager == null || Manager.Runner == null || !Manager.Runner.IsRunning) return 0;
                    int count = 0;
                    foreach (var _ in Manager.Runner.ActivePlayers) count++;
                    return count;
                }
            }
        }

        private enum RoomState
        {
            Starting,   // Room created, Fusion session starting
            Lobby,      // Session active, waiting for players / Enter Battle
            InGame,     // Match in progress
            Closing     // Shutting down
        }

        private readonly Dictionary<string, RoomInfo> _rooms = new Dictionary<string, RoomInfo>();
        private readonly object _roomLock = new object();

        // HTTP listener
        private HttpListener _httpListener;
        private Thread _httpThread;
        private bool _httpRunning = false;

        // Room code generation
        private static readonly char[] CODE_CHARS = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789".ToCharArray(); // no I/O/0/1
        private static readonly System.Random _rng = new System.Random();

        // Queued actions from HTTP thread → Unity main thread
        private readonly Queue<Action> _mainThreadActions = new Queue<Action>();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[RoomManager] Duplicate detected — destroying this instance");
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            Debug.Log("[RoomManager] Instance created");
        }

        private void Start()
        {
            StartHttpListener();
            Debug.Log($"[RoomManager] Ready. HTTP API on port {httpPort}, max {maxRooms} rooms");
        }

        private void Update()
        {
            // Process queued main-thread actions from HTTP callbacks
            lock (_mainThreadActions)
            {
                while (_mainThreadActions.Count > 0)
                {
                    var action = _mainThreadActions.Dequeue();
                    try { action(); }
                    catch (Exception ex) { Debug.LogError($"[RoomManager] Main thread action error: {ex}"); }
                }
            }

            // Periodic cleanup of empty rooms
            CleanupEmptyRooms();
        }

        private void OnDestroy()
        {
            StopHttpListener();
            // Shut down all rooms
            lock (_roomLock)
            {
                foreach (var kvp in _rooms)
                {
                    ShutdownRoomInternal(kvp.Value);
                }
                _rooms.Clear();
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  ROOM MANAGEMENT
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Creates a new room with a unique code. Returns the code.
        /// Must be called from the main thread.
        /// </summary>
        public string CreateRoom()
        {
            lock (_roomLock)
            {
                if (_rooms.Count >= maxRooms)
                {
                    Debug.LogWarning($"[RoomManager] Cannot create room — at max capacity ({maxRooms})");
                    return null;
                }
            }

            string code = GenerateUniqueCode();
            Debug.Log($"[RoomManager] Creating room '{code}'...");

            // Instantiate a new NetworkManager for this room
            if (networkManagerPrefab == null)
            {
                Debug.LogError("[RoomManager] networkManagerPrefab not assigned!");
                return null;
            }

            GameObject nmGO = Instantiate(networkManagerPrefab);
            nmGO.name = $"NetworkManager_Room_{code}";
            DontDestroyOnLoad(nmGO);

            NetworkManager nm = nmGO.GetComponent<NetworkManager>();
            if (nm == null)
            {
                Debug.LogError("[RoomManager] networkManagerPrefab doesn't have NetworkManager component!");
                Destroy(nmGO);
                return null;
            }

            var room = new RoomInfo
            {
                Code = code,
                Manager = nm,
                CreatedAt = DateTime.UtcNow,
                State = RoomState.Starting
            };

            lock (_roomLock)
            {
                _rooms[code] = room;
            }

            // Tell the NetworkManager to start as server with this room code
            nm.StartServerForRoom(code, maxPlayersPerRoom, (success) =>
            {
                if (success)
                {
                    room.Runner = nm.Runner;
                    room.State = RoomState.Lobby;
                    Debug.Log($"[RoomManager] Room '{code}' is LIVE — session created, waiting for players");
                }
                else
                {
                    Debug.LogError($"[RoomManager] Room '{code}' FAILED to start — cleaning up");
                    lock (_roomLock) { _rooms.Remove(code); }
                    Destroy(nmGO);
                }
            });

            return code;
        }

        /// <summary>
        /// Shuts down a specific room by code.
        /// </summary>
        public void ShutdownRoom(string code)
        {
            RoomInfo room;
            lock (_roomLock)
            {
                if (!_rooms.TryGetValue(code, out room))
                {
                    Debug.LogWarning($"[RoomManager] Room '{code}' not found");
                    return;
                }
                _rooms.Remove(code);
            }

            ShutdownRoomInternal(room);
            Debug.Log($"[RoomManager] Room '{code}' shut down");
        }

        private void ShutdownRoomInternal(RoomInfo room)
        {
            room.State = RoomState.Closing;
            if (room.Manager != null)
            {
                room.Manager.Disconnect();
                // Destroy the NetworkManager GameObject after a short delay
                // to allow Fusion shutdown to complete
                Destroy(room.Manager.gameObject, 1f);
            }
        }

        private void CleanupEmptyRooms()
        {
            List<string> toRemove = null;

            lock (_roomLock)
            {
                foreach (var kvp in _rooms)
                {
                    var room = kvp.Value;
                    if (room.State == RoomState.Closing) continue;

                    // Check if room's NetworkManager/Runner is dead
                    if (room.Manager == null || room.Manager.Runner == null || !room.Manager.Runner.IsRunning)
                    {
                        if (room.State != RoomState.Starting) // Don't clean up rooms that haven't started yet
                        {
                            toRemove ??= new List<string>();
                            toRemove.Add(kvp.Key);
                        }
                        continue;
                    }

                    // Check if room is empty and timeout exceeded
                    if (room.IsEmpty)
                    {
                        if (room.LastPlayerLeftAt == default)
                        {
                            room.LastPlayerLeftAt = DateTime.UtcNow;
                        }
                        else if ((DateTime.UtcNow - room.LastPlayerLeftAt).TotalSeconds > emptyRoomTimeout)
                        {
                            Debug.Log($"[RoomManager] Room '{kvp.Key}' empty for {emptyRoomTimeout}s — shutting down");
                            toRemove ??= new List<string>();
                            toRemove.Add(kvp.Key);
                        }
                    }
                    else
                    {
                        room.LastPlayerLeftAt = default; // Reset timer if players are present
                    }
                }
            }

            if (toRemove != null)
            {
                foreach (var code in toRemove)
                {
                    ShutdownRoom(code);
                }
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  ROOM CODE GENERATION
        // ══════════════════════════════════════════════════════════════

        private string GenerateUniqueCode()
        {
            string code;
            int attempts = 0;
            do
            {
                code = GenerateCode();
                attempts++;
            }
            while (_rooms.ContainsKey(code) && attempts < 100);

            return code;
        }

        private static string GenerateCode()
        {
            char[] code = new char[4];
            for (int i = 0; i < 4; i++)
                code[i] = CODE_CHARS[_rng.Next(CODE_CHARS.Length)];
            return new string(code);
        }

        // ══════════════════════════════════════════════════════════════
        //  HTTP API
        // ══════════════════════════════════════════════════════════════

        private void StartHttpListener()
        {
            try
            {
                _httpListener = new HttpListener();
                _httpListener.Prefixes.Add($"http://+:{httpPort}/");
                _httpListener.Start();
                _httpRunning = true;

                _httpThread = new Thread(HttpListenLoop)
                {
                    IsBackground = true,
                    Name = "RoomManager-HTTP"
                };
                _httpThread.Start();

                Debug.Log($"[RoomManager] HTTP listener started on port {httpPort}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[RoomManager] Failed to start HTTP listener: {ex.Message}");

                // Fallback: try localhost only
                try
                {
                    _httpListener = new HttpListener();
                    _httpListener.Prefixes.Add($"http://localhost:{httpPort}/");
                    _httpListener.Prefixes.Add($"http://0.0.0.0:{httpPort}/");
                    _httpListener.Start();
                    _httpRunning = true;

                    _httpThread = new Thread(HttpListenLoop)
                    {
                        IsBackground = true,
                        Name = "RoomManager-HTTP"
                    };
                    _httpThread.Start();
                    Debug.Log($"[RoomManager] HTTP listener started on localhost:{httpPort} (fallback)");
                }
                catch (Exception ex2)
                {
                    Debug.LogError($"[RoomManager] HTTP fallback also failed: {ex2.Message}");
                }
            }
        }

        private void StopHttpListener()
        {
            _httpRunning = false;
            try
            {
                _httpListener?.Stop();
                _httpListener?.Close();
            }
            catch { }
            _httpThread = null;
            Debug.Log("[RoomManager] HTTP listener stopped");
        }

        private void HttpListenLoop()
        {
            while (_httpRunning)
            {
                try
                {
                    var context = _httpListener.GetContext();
                    ThreadPool.QueueUserWorkItem((_) => HandleHttpRequest(context));
                }
                catch (HttpListenerException)
                {
                    if (_httpRunning) Debug.LogWarning("[RoomManager] HTTP listener exception (shutting down?)");
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[RoomManager] HTTP error: {ex.Message}");
                }
            }
        }

        private void HandleHttpRequest(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;

            // Add CORS headers for Unity WebGL / browser clients
            response.AddHeader("Access-Control-Allow-Origin", "*");
            response.AddHeader("Access-Control-Allow-Methods", "GET, POST, DELETE, OPTIONS");
            response.AddHeader("Access-Control-Allow-Headers", "Content-Type");

            if (request.HttpMethod == "OPTIONS")
            {
                response.StatusCode = 200;
                response.Close();
                return;
            }

            string path = request.Url.AbsolutePath.TrimEnd('/').ToLower();
            string method = request.HttpMethod;

            try
            {
                if (method == "POST" && path == "/create")
                {
                    HandleCreateRoom(response);
                }
                else if (method == "GET" && path == "/rooms")
                {
                    HandleListRooms(response);
                }
                else if (method == "GET" && path.StartsWith("/rooms/"))
                {
                    string code = request.Url.AbsolutePath.Substring("/rooms/".Length).Trim('/').ToUpper();
                    HandleGetRoom(response, code);
                }
                else if (method == "DELETE" && path.StartsWith("/rooms/"))
                {
                    string code = request.Url.AbsolutePath.Substring("/rooms/".Length).Trim('/').ToUpper();
                    HandleDeleteRoom(response, code);
                }
                else if (method == "POST" && path.StartsWith("/start/"))
                {
                    string code = request.Url.AbsolutePath.Substring("/start/".Length).Trim('/').ToUpper();
                    HandleStartMatch(response, code);
                }
                else if (method == "GET" && (path == "/" || path == "/health"))
                {
                    // Health check
                    int roomCount;
                    lock (_roomLock) { roomCount = _rooms.Count; }
                    SendJson(response, 200, $"{{\"status\":\"ok\",\"rooms\":{roomCount},\"maxRooms\":{maxRooms}}}");
                }
                else
                {
                    SendJson(response, 404, "{\"error\":\"not_found\"}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[RoomManager] HTTP handler error: {ex}");
                try { SendJson(response, 500, $"{{\"error\":\"{EscapeJson(ex.Message)}\"}}"); }
                catch { }
            }
        }

        private void HandleCreateRoom(HttpListenerResponse response)
        {
            // Room creation must happen on the main thread (Unity API)
            // Use a ManualResetEvent to wait for the result
            string resultCode = null;
            string error = null;
            var done = new ManualResetEvent(false);

            lock (_mainThreadActions)
            {
                _mainThreadActions.Enqueue(() =>
                {
                    try
                    {
                        resultCode = CreateRoom();
                        if (resultCode == null)
                            error = "max_rooms_reached";
                    }
                    catch (Exception ex)
                    {
                        error = ex.Message;
                    }
                    finally
                    {
                        done.Set();
                    }
                });
            }

            // Wait up to 10 seconds for the main thread to process
            if (done.WaitOne(TimeSpan.FromSeconds(10)))
            {
                if (error != null)
                {
                    SendJson(response, 503, $"{{\"error\":\"{EscapeJson(error)}\"}}");
                }
                else
                {
                    SendJson(response, 201, $"{{\"code\":\"{resultCode}\"}}");
                    Debug.Log($"[RoomManager] HTTP: Created room '{resultCode}'");
                }
            }
            else
            {
                SendJson(response, 504, "{\"error\":\"timeout\"}");
            }
        }

        private void HandleListRooms(HttpListenerResponse response)
        {
            var sb = new StringBuilder();
            sb.Append("[");
            bool first = true;

            lock (_roomLock)
            {
                foreach (var kvp in _rooms)
                {
                    var room = kvp.Value;
                    if (room.State == RoomState.Closing) continue;

                    if (!first) sb.Append(",");
                    first = false;

                    sb.Append($"{{\"code\":\"{room.Code}\",");
                    sb.Append($"\"players\":{room.PlayerCount},");
                    sb.Append($"\"maxPlayers\":{maxPlayersPerRoom},");
                    sb.Append($"\"state\":\"{room.State}\",");
                    sb.Append($"\"createdAt\":\"{room.CreatedAt:yyyy-MM-ddTHH:mm:ssZ}\"}}");
                }
            }

            sb.Append("]");
            SendJson(response, 200, sb.ToString());
        }

        private void HandleGetRoom(HttpListenerResponse response, string code)
        {
            RoomInfo room;
            lock (_roomLock)
            {
                if (!_rooms.TryGetValue(code, out room))
                {
                    SendJson(response, 404, "{\"error\":\"room_not_found\"}");
                    return;
                }
            }

            string json = $"{{\"code\":\"{room.Code}\"," +
                          $"\"players\":{room.PlayerCount}," +
                          $"\"maxPlayers\":{maxPlayersPerRoom}," +
                          $"\"state\":\"{room.State}\"," +
                          $"\"createdAt\":\"{room.CreatedAt:yyyy-MM-ddTHH:mm:ssZ}\"}}";
            SendJson(response, 200, json);
        }

        private void HandleDeleteRoom(HttpListenerResponse response, string code)
        {
            lock (_roomLock)
            {
                if (!_rooms.ContainsKey(code))
                {
                    SendJson(response, 404, "{\"error\":\"room_not_found\"}");
                    return;
                }
            }

            // Must run on main thread
            var done = new ManualResetEvent(false);
            lock (_mainThreadActions)
            {
                _mainThreadActions.Enqueue(() =>
                {
                    ShutdownRoom(code);
                    done.Set();
                });
            }

            done.WaitOne(TimeSpan.FromSeconds(5));
            SendJson(response, 200, $"{{\"deleted\":\"{code}\"}}");
        }

        /// <summary>
        /// POST /start/{code} — Client requests match start for a room.
        /// This bypasses Fusion's ReliableData (which has proven unreliable for signals)
        /// and uses HTTP (TCP) to guarantee delivery.
        /// </summary>
        private void HandleStartMatch(HttpListenerResponse response, string code)
        {
            RoomInfo room;
            lock (_roomLock)
            {
                if (!_rooms.TryGetValue(code, out room))
                {
                    SendJson(response, 404, "{\"error\":\"room_not_found\"}");
                    return;
                }
            }

            if (room.Manager == null)
            {
                SendJson(response, 500, "{\"error\":\"room_manager_null\"}");
                return;
            }

            string error = null;
            bool alreadyStarted = false;
            var done = new ManualResetEvent(false);

            lock (_mainThreadActions)
            {
                _mainThreadActions.Enqueue(() =>
                {
                    try
                    {
                        var mgr = room.Manager;
                        if (mgr._countdownActive || mgr._inGameplayScene)
                        {
                            alreadyStarted = true;
                            Debug.Log($"[RoomManager] HTTP /start/{code}: Already in countdown or gameplay — ignoring");
                        }
                        else
                        {
                            Debug.Log($"[RoomManager] HTTP /start/{code}: Triggering match start via HTTP!");
                            // Use PlayerRef.None since HTTP doesn't map to a Fusion player
                            // TriggerMatchStart accepts any PlayerRef for logging
                            mgr.TriggerMatchStart(Fusion.PlayerRef.None);
                            room.State = RoomState.InGame;
                        }
                    }
                    catch (Exception ex)
                    {
                        error = ex.Message;
                        Debug.LogError($"[RoomManager] HTTP /start/{code} error: {ex}");
                    }
                    finally
                    {
                        done.Set();
                    }
                });
            }

            if (done.WaitOne(TimeSpan.FromSeconds(10)))
            {
                if (error != null)
                    SendJson(response, 500, $"{{\"error\":\"{EscapeJson(error)}\"}}");
                else if (alreadyStarted)
                    SendJson(response, 200, "{\"status\":\"already_started\"}");
                else
                    SendJson(response, 200, "{\"status\":\"match_starting\"}");
            }
            else
            {
                SendJson(response, 504, "{\"error\":\"timeout\"}");
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  HTTP HELPERS
        // ══════════════════════════════════════════════════════════════

        private static void SendJson(HttpListenerResponse response, int statusCode, string json)
        {
            response.StatusCode = statusCode;
            response.ContentType = "application/json";
            byte[] buffer = Encoding.UTF8.GetBytes(json);
            response.ContentLength64 = buffer.Length;
            response.OutputStream.Write(buffer, 0, buffer.Length);
            response.OutputStream.Close();
        }

        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");
        }
    }
}
