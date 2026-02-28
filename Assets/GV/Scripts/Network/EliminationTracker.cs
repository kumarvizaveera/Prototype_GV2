using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Fusion;
using VSX.VehicleCombatKits;
using VSX.Health;

namespace GV.Network
{
    /// <summary>
    /// Tracks player eliminations in a Battle Royale match.
    ///
    /// What this does:
    /// - Monitors all spawned players' ships for destruction
    /// - When a ship's health reaches zero, records that player's elimination
    ///   (first to die = last place, last standing = 1st place)
    /// - When only one player remains, ends the match automatically
    /// - Broadcasts final placements to all clients via RPC
    ///
    /// Technical note:
    /// - Does NOT use FixedUpdateNetwork (Fusion may not call it on late-added components)
    /// - Uses Update() with event-based race state tracking instead
    /// - Detects Racing state by subscribing to NetworkedGameManager.OnRaceStarted
    ///
    /// How to set it up:
    /// - Add this component to the same GameObject as NetworkedGameManager
    ///   (the "RaceManager" object in your gameplay scene)
    /// </summary>
    public class EliminationTracker : NetworkBehaviour
    {
        public static EliminationTracker Instance { get; private set; }

        // --- Events (fire on ALL clients after RPC) ---

        public event Action<PlayerRef, int> OnPlayerEliminated;
        public event Action<List<PlayerPlacement>> OnMatchResultsReady;

        // --- State ---

        private List<PlayerRef> _eliminationOrder = new List<PlayerRef>();
        private List<PlayerRef> _matchPlayers = new List<PlayerRef>();
        private Dictionary<PlayerRef, VehicleHealth> _monitoredVehicles = new Dictionary<PlayerRef, VehicleHealth>();
        private List<PlayerPlacement> _finalPlacements = new List<PlayerPlacement>();
        private bool _matchEnded = false;
        private bool _isSpawned = false;
        private bool _isHost = false;
        private bool _isRacing = false;
        private bool _subscribedToGameManager = false;

        // --- Public Properties ---

        public IReadOnlyList<PlayerPlacement> FinalPlacements => _finalPlacements;
        public bool MatchEnded => _matchEnded;
        public int AliveCount => _matchPlayers.Count - _eliminationOrder.Count;

        private void Awake()
        {
            Instance = this;
        }

        public override void Spawned()
        {
            _isSpawned = true;
            _isHost = Object.HasStateAuthority;

            Debug.Log($"[EliminationTracker] Spawned! IsHost={_isHost}");

            _eliminationOrder.Clear();
            _matchPlayers.Clear();
            _monitoredVehicles.Clear();
            _finalPlacements.Clear();
            _matchEnded = false;
            _isRacing = false;
        }

        private void Update()
        {
            if (!_isSpawned || !_isHost) return;
            if (_matchEnded) return;

            // Keep trying to subscribe to game manager events
            TrySubscribeToGameManager();

            if (!_isRacing) return;

            MonitorPlayerVehicles();
            CheckForEliminations();
        }

        private void OnDisable()
        {
            var gm = NetworkedGameManager.Instance;
            if (gm != null && _subscribedToGameManager)
            {
                gm.OnRaceStarted -= HandleRaceStarted;
                gm.OnRaceFinished -= HandleRaceFinished;
            }
        }

        // --- Race State Tracking (via events, not [Networked] properties) ---

        private void TrySubscribeToGameManager()
        {
            if (_subscribedToGameManager) return;

            var gm = NetworkedGameManager.Instance;
            if (gm == null) return;

            gm.OnRaceStarted += HandleRaceStarted;
            gm.OnRaceFinished += HandleRaceFinished;
            _subscribedToGameManager = true;

            Debug.Log("[EliminationTracker] Subscribed to GameManager events");

            // Check if race is ALREADY running (we subscribed late, missed the event)
            // Read CurrentState via the OnGUI-safe getter — it's a networked property
            // but we can check if the game manager already fired RaceStarted
            // by checking if ships are spawned and flying
            if (NetworkManager.Instance?.Runner != null)
            {
                var allVH = FindObjectsOfType<VehicleHealth>();
                bool shipsExist = false;
                foreach (var vh in allVH)
                {
                    var netObj = vh.GetComponentInParent<NetworkObject>();
                    if (netObj != null)
                    {
                        shipsExist = true;
                        break;
                    }
                }

                if (shipsExist)
                {
                    _isRacing = true;
                    Debug.Log("[EliminationTracker] Race already in progress — starting tracking now");
                }
            }
        }

        private void HandleRaceStarted()
        {
            _isRacing = true;
            Debug.Log("[EliminationTracker] Race started — beginning elimination tracking");
        }

        private void HandleRaceFinished()
        {
            _isRacing = false;
        }

        // --- Vehicle Monitoring ---

        private void MonitorPlayerVehicles()
        {
            if (NetworkManager.Instance == null) return;
            var runner = NetworkManager.Instance.Runner;
            if (runner == null) return;

            VehicleHealth[] allVehicleHealths = null;

            foreach (var player in runner.ActivePlayers)
            {
                if (_monitoredVehicles.ContainsKey(player)) continue;

                if (allVehicleHealths == null)
                {
                    allVehicleHealths = FindObjectsOfType<VehicleHealth>();
                    Debug.Log($"[EliminationTracker] Searching for vehicles... " +
                        $"Found {allVehicleHealths.Length} VehicleHealth in scene");
                }

                foreach (var vh in allVehicleHealths)
                {
                    var netObj = vh.GetComponentInParent<NetworkObject>();
                    if (netObj != null && netObj.InputAuthority == player)
                    {
                        if (!_matchPlayers.Contains(player))
                        {
                            _matchPlayers.Add(player);
                        }

                        _monitoredVehicles[player] = vh;

                        Debug.Log($"[EliminationTracker] Now monitoring Player {player.PlayerId}'s ship " +
                            $"({vh.gameObject.name}) — {vh.Damageables.Count} damageables");

                        for (int i = 0; i < vh.Damageables.Count; i++)
                        {
                            var d = vh.Damageables[i];
                            Debug.Log($"  Damageable[{i}]: {d.gameObject.name} " +
                                $"HP={d.CurrentHealth}/{d.HealthCapacity} " +
                                $"IsDamageable={d.IsDamageable} Destroyed={d.Destroyed}");
                        }
                        break;
                    }
                }
            }
        }

        private void CheckForEliminations()
        {
            foreach (var kvp in _monitoredVehicles)
            {
                PlayerRef player = kvp.Key;
                VehicleHealth vh = kvp.Value;

                if (_eliminationOrder.Contains(player)) continue;
                if (vh == null) continue;

                bool isDead = vh.Damageables.Count > 0 && vh.Damageables.All(d =>
                    d == null ||
                    d.Destroyed ||
                    !d.IsDamageable ||
                    d.CurrentHealth <= 0f
                );

                if (isDead)
                {
                    HandlePlayerEliminated(player);
                }
            }
        }

        // --- Elimination Logic ---

        private void HandlePlayerEliminated(PlayerRef player)
        {
            if (_matchEnded) return;
            if (_eliminationOrder.Contains(player)) return;

            _eliminationOrder.Add(player);

            int aliveCount = _matchPlayers.Count - _eliminationOrder.Count;
            int placement = aliveCount + 1;

            Debug.Log($"[EliminationTracker] Player {player.PlayerId} ELIMINATED! " +
                $"Placement: {placement} | Alive: {aliveCount}/{_matchPlayers.Count}");

            RPC_PlayerEliminated(player.PlayerId, placement);

            if (aliveCount <= 1)
            {
                EndMatchWithResults();
            }
        }

        private void EndMatchWithResults()
        {
            if (_matchEnded) return;
            _matchEnded = true;

            _finalPlacements.Clear();

            var survivors = _matchPlayers.Where(p => !_eliminationOrder.Contains(p)).ToList();

            int nextPlacement = 1;
            foreach (var survivor in survivors)
            {
                _finalPlacements.Add(new PlayerPlacement
                {
                    playerRef = survivor,
                    playerId = survivor.PlayerId,
                    placement = nextPlacement
                });
                nextPlacement++;
            }

            for (int i = _eliminationOrder.Count - 1; i >= 0; i--)
            {
                var player = _eliminationOrder[i];
                _finalPlacements.Add(new PlayerPlacement
                {
                    playerRef = player,
                    playerId = player.PlayerId,
                    placement = nextPlacement
                });
                nextPlacement++;
            }

            Debug.Log("[EliminationTracker] === MATCH RESULTS ===");
            foreach (var p in _finalPlacements)
            {
                Debug.Log($"  {GetPlacementLabel(p.placement)}: Player {p.playerId}");
            }

            // Copy to a temp list before sending RPCs — the RPCs fire immediately
            // on the host and modify _finalPlacements, which would crash a foreach
            int totalPlayers = _finalPlacements.Count;
            var resultsToSend = new List<PlayerPlacement>(_finalPlacements);
            foreach (var p in resultsToSend)
            {
                RPC_PlayerResult(p.playerId, p.placement, totalPlayers);
            }

            RPC_MatchComplete();

            if (NetworkedGameManager.Instance != null)
            {
                NetworkedGameManager.Instance.EndRace();
            }
        }

        // --- RPCs (host → all clients) ---

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_PlayerEliminated(int playerId, int placement)
        {
            Debug.Log($"[EliminationTracker] RPC: Player {playerId} eliminated → {GetPlacementLabel(placement)}");

            PlayerRef playerRef = default;
            if (NetworkManager.Instance?.Runner != null)
            {
                foreach (var p in NetworkManager.Instance.Runner.ActivePlayers)
                {
                    if (p.PlayerId == playerId)
                    {
                        playerRef = p;
                        break;
                    }
                }
            }

            OnPlayerEliminated?.Invoke(playerRef, placement);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_PlayerResult(int playerId, int placement, int totalPlayers)
        {
            _finalPlacements.RemoveAll(p => p.playerId == playerId);

            _finalPlacements.Add(new PlayerPlacement
            {
                playerId = playerId,
                placement = placement
            });

            Debug.Log($"[EliminationTracker] RPC: Player {playerId} → {GetPlacementLabel(placement)} " +
                $"({_finalPlacements.Count}/{totalPlayers} results received)");
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_MatchComplete()
        {
            _matchEnded = true;
            Debug.Log($"[EliminationTracker] RPC: Match complete! {_finalPlacements.Count} players ranked.");
            OnMatchResultsReady?.Invoke(new List<PlayerPlacement>(_finalPlacements));
        }

        // --- Helpers ---

        public int GetLocalPlayerPlacement()
        {
            if (NetworkManager.Instance?.Runner == null) return 0;

            int localPlayerId = NetworkManager.Instance.Runner.LocalPlayer.PlayerId;
            var result = _finalPlacements.Find(p => p.playerId == localPlayerId);
            return result?.placement ?? 0;
        }

        private string GetPlacementLabel(int placement)
        {
            switch (placement)
            {
                case 1: return "1st Place";
                case 2: return "2nd Place";
                case 3: return "3rd Place";
                case 4: return "4th Place";
                default: return $"{placement}th Place";
            }
        }
    }

    [Serializable]
    public class PlayerPlacement
    {
        public PlayerRef playerRef;
        public int playerId;
        public int placement;
    }
}
