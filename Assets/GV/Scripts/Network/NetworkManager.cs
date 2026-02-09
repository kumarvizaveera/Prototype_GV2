using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using Fusion;
using Fusion.Sockets;

namespace GV.Network
{
    /// <summary>
    /// Central manager for Photon Fusion 2 networking.
    /// Handles connection, player spawning, and network callbacks.
    /// </summary>
    public class NetworkManager : MonoBehaviour, INetworkRunnerCallbacks
    {
        public static NetworkManager Instance { get; private set; }
        
        [Header("Configuration")]
        [SerializeField] private NetworkRunner runnerPrefab;
        [SerializeField] private NetworkObject playerPrefab;
        [SerializeField] private int maxPlayers = 4;
        
        [Header("Spawn Points")]
        [SerializeField] private Transform[] spawnPoints;
        
        [Header("Debug")]
        [SerializeField] private bool showDebugUI = true;
        
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
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            
            Instance = this;
            DontDestroyOnLoad(gameObject);
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
            }
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
        
        private async Task StartGame(GameMode mode)
        {
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
            
            var sceneInfo = new NetworkSceneInfo();
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            sceneInfo.AddSceneRef(SceneRef.FromIndex(scene.buildIndex));
            
            var result = await Runner.StartGame(new StartGameArgs
            {
                GameMode = mode,
                SessionName = "GV_Race",
                PlayerCount = maxPlayers,
                Scene = sceneInfo,
                SceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>()
            });
            
            // Add callbacks after starting (Fusion 2 pattern)
            Runner.AddCallbacks(this);

            
            if (result.Ok)
            {
                Debug.Log($"[NetworkManager] Started game as {mode}");
                OnConnectedEvent?.Invoke(Runner);
            }
            else
            {
                Debug.LogError($"[NetworkManager] Failed to start: {result.ShutdownReason}");
            }
        }
        
        public void Disconnect()
        {
            if (Runner != null)
            {
                Runner.Shutdown();
            }
        }
        
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
        
        #region INetworkRunnerCallbacks
        
        public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
        {
            Debug.Log($"[NetworkManager] Player {player.PlayerId} joined. IsServer: {runner.IsServer}, playerPrefab: {(playerPrefab != null ? playerPrefab.name : "NULL")}");
            
            if (runner.IsServer && playerPrefab != null)
            {
                var spawnPos = GetSpawnPosition(player);
                var spawnRot = GetSpawnRotation(player);
                
                Debug.Log($"[NetworkManager] About to spawn player {player.PlayerId} at {spawnPos}");
                
                // Spawn player with explicit input authority assignment
                var playerObject = runner.Spawn(playerPrefab, spawnPos, spawnRot, inputAuthority: player);
                
                Debug.Log($"[NetworkManager] Spawn returned: {(playerObject != null ? playerObject.name : "NULL")}");
                
                // Double-check: explicitly assign input authority after spawn
                if (playerObject != null)
                {
                    playerObject.AssignInputAuthority(player);
                    Debug.Log($"[NetworkManager] Assigned InputAuthority to {player}, now authority is: {playerObject.InputAuthority}");
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
        
        public void OnInput(NetworkRunner runner, NetworkInput input) { }
        public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
        
        public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
        {
            Debug.Log($"[NetworkManager] Shutdown: {shutdownReason}");
            OnDisconnectedEvent?.Invoke(runner, shutdownReason);
            _spawnedPlayers.Clear();
            Runner = null;
        }
        
        public void OnConnectedToServer(NetworkRunner runner) { }
        public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) { }
        public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { request.Accept(); }
        public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
        public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
        public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
        public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
        public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
        public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }
        public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
        public void OnSceneLoadDone(NetworkRunner runner) { }
        public void OnSceneLoadStart(NetworkRunner runner) { }
        public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
        public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
        
        #endregion
        
        private void OnGUI()
        {
            if (!showDebugUI) return;
            
            GUILayout.BeginArea(new Rect(10, 10, 200, 150));
            GUILayout.BeginVertical("box");
            
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
                if (GUILayout.Button("Disconnect")) Disconnect();
            }
            
            GUILayout.EndVertical();
            GUILayout.EndArea();
        }
    }
}
