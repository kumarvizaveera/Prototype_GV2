using System.Collections.Generic;
using UnityEngine;
using VSX.VehicleCombatKits;
using VSX.Health;

namespace GV.Web3
{
    /// <summary>
    /// Bridges the multiplayer game state to the reward system.
    ///
    /// What this does:
    /// - Listens for match results from EliminationTracker (real placements)
    /// - Also detects local ship destruction as a backup trigger
    /// - If EliminationTracker doesn't respond within 5 seconds of ship death, uses fallback
    /// - Distributes token rewards based on actual placement
    /// - Shows the PostMatchRewardUI popup
    ///
    /// How rewards trigger (priority order):
    /// 1. EliminationTracker.OnMatchResultsReady → real placements from host
    /// 2. Local ship destroyed + 5s timeout → fallback placement
    /// 3. OnRaceFinished → fallback placement
    /// 4. Test key (R) → test rewards
    /// </summary>
    public class BattleRewardBridge : MonoBehaviour
    {
        [Header("UI Reference (Optional)")]
        [Tooltip("Drag in the PostMatchRewardUI if you want the reward popup to show. Auto-finds if on same GameObject.")]
        [SerializeField] private PostMatchRewardUI postMatchUI;

        [Header("Debug")]
        [Tooltip("Enable this to test rewards with a keyboard shortcut (press R during a match).")]
        [SerializeField] private bool enableTestKey = true;

        [Tooltip("Key to press for testing reward distribution.")]
        [SerializeField] private KeyCode testKey = KeyCode.R;

        [Header("Fallback Settings")]
        [Tooltip("Seconds to wait for EliminationTracker after ship dies before using fallback.")]
        [SerializeField] private float eliminationTimeout = 5f;

        private bool _hasDistributedThisMatch = false;
        private GV.Network.NetworkedGameManager _gameManager;
        private bool _subscribedToGameManager = false;
        private bool _subscribedToElimination = false;
        private VehicleHealth _localVehicleHealth;
        private bool _subscribedToVehicle = false;

        // Fallback timer: starts when local ship is destroyed
        private bool _localShipDead = false;
        private float _shipDeathTime = 0f;

        private void OnEnable()
        {
            // Dedicated server has no wallet, no UI, no rewards to display — disable entirely.
            if (GV.Network.NetworkManager.Instance != null && GV.Network.NetworkManager.Instance.IsDedicatedServer)
            {
                Debug.Log("[BattleRewardBridge] Dedicated server detected — disabling reward bridge (client-only feature)");
                enabled = false;
                return;
            }

            _hasDistributedThisMatch = false;
            _subscribedToVehicle = false;
            _subscribedToElimination = false;
            _localVehicleHealth = null;
            _localShipDead = false;

            if (postMatchUI == null)
            {
                postMatchUI = GetComponentInChildren<PostMatchRewardUI>();
            }

            // Auto-create BattleRewardManager if it doesn't exist
            // (happens when launching directly into the gameplay scene instead of Bootstrap)
            EnsureBattleRewardManager();
        }

        /// <summary>
        /// Creates a BattleRewardManager if one isn't already in the scene.
        /// This way rewards work no matter which scene you start from.
        /// </summary>
        private void EnsureBattleRewardManager()
        {
            if (BattleRewardManager.Instance != null) return;

            var go = new GameObject("BattleRewardManager (Auto)");
            go.AddComponent<BattleRewardManager>();
            Debug.Log("[BattleRewardBridge] Auto-created BattleRewardManager (not found in scene)");
        }

        private void Update()
        {
            TrySubscribeToGameManager();
            TrySubscribeToEliminationTracker();
            TrySubscribeToLocalVehicle();

            // Fallback: if ship died and EliminationTracker hasn't provided results, use fallback
            if (_localShipDead && !_hasDistributedThisMatch)
            {
                float elapsed = Time.time - _shipDeathTime;
                if (elapsed >= eliminationTimeout)
                {
                    Debug.Log($"[BattleRewardBridge] EliminationTracker timeout ({eliminationTimeout}s) — using fallback placement.");
                    DistributeWithDummyPlacement();
                }
            }

            if (enableTestKey && Input.GetKeyDown(testKey))
            {
                Debug.Log("[BattleRewardBridge] Test key pressed — triggering test rewards...");
                TriggerTestRewards();
            }
        }

        private void OnDisable()
        {
            if (_gameManager != null)
            {
                _gameManager.OnRaceFinished -= HandleRaceFinished;
                _subscribedToGameManager = false;
            }

            if (_subscribedToElimination && GV.Network.EliminationTracker.Instance != null)
            {
                GV.Network.EliminationTracker.Instance.OnMatchResultsReady -= HandleMatchResults;
                _subscribedToElimination = false;
            }

            UnsubscribeFromVehicle();
        }

        // --- Subscriptions ---

        private void TrySubscribeToGameManager()
        {
            if (_subscribedToGameManager) return;

            _gameManager = GV.Network.NetworkedGameManager.Instance;
            if (_gameManager != null)
            {
                _gameManager.OnRaceFinished += HandleRaceFinished;
                _subscribedToGameManager = true;
            }
        }

        private void TrySubscribeToEliminationTracker()
        {
            if (_subscribedToElimination) return;

            var tracker = GV.Network.EliminationTracker.Instance;
            if (tracker != null)
            {
                tracker.OnMatchResultsReady += HandleMatchResults;
                _subscribedToElimination = true;
                Debug.Log("[BattleRewardBridge] Subscribed to EliminationTracker.OnMatchResultsReady");
            }
        }

        private void TrySubscribeToLocalVehicle()
        {
            if (_subscribedToVehicle) return;

            var allVehicles = FindObjectsOfType<VehicleHealth>();
            foreach (var vh in allVehicles)
            {
                var networkObj = vh.GetComponentInParent<Fusion.NetworkObject>();
                if (networkObj != null && networkObj.HasInputAuthority)
                {
                    _localVehicleHealth = vh;

                    foreach (var damageable in vh.Damageables)
                    {
                        damageable.onDestroyed.AddListener(HandleLocalShipDestroyed);
                    }

                    _subscribedToVehicle = true;
                    Debug.Log($"[BattleRewardBridge] Subscribed to local ship destruction ({vh.gameObject.name})");
                    break;
                }
            }
        }

        private void UnsubscribeFromVehicle()
        {
            if (_localVehicleHealth != null)
            {
                foreach (var damageable in _localVehicleHealth.Damageables)
                {
                    damageable.onDestroyed.RemoveListener(HandleLocalShipDestroyed);
                }
                _subscribedToVehicle = false;
                _localVehicleHealth = null;
            }
        }

        // --- Event Handlers ---

        /// <summary>
        /// PRIMARY TRIGGER — Real placements from EliminationTracker.
        /// Called via RPC on all clients when the match ends.
        /// </summary>
        private void HandleMatchResults(List<GV.Network.PlayerPlacement> results)
        {
            if (_hasDistributedThisMatch) return;

            Debug.Log($"[BattleRewardBridge] Match results received ({results.Count} players). Distributing rewards...");
            _hasDistributedThisMatch = true;

            // Find local player's placement
            int localPlacement = 1;
            var tracker = GV.Network.EliminationTracker.Instance;
            if (tracker != null)
            {
                int realPlacement = tracker.GetLocalPlayerPlacement();
                if (realPlacement > 0) localPlacement = realPlacement;
            }

            // Check if wallet is connected
            bool walletConnected = Web3Manager.Instance != null && Web3Manager.Instance.IsWalletConnected;

            if (!walletConnected)
            {
                Debug.LogWarning("[BattleRewardBridge] No wallet connected — showing placement but can't mint tokens. " +
                    "Start from Bootstrap scene to connect a wallet.");

                if (postMatchUI != null)
                {
                    postMatchUI.ShowNoWallet(localPlacement);
                }
                return;
            }

            var players = new List<PlayerRewardInfo>();
            players.Add(new PlayerRewardInfo
            {
                walletAddress = Web3Manager.Instance.WalletAddress,
                placement = localPlacement
            });

            Debug.Log($"[BattleRewardBridge] Local player {Web3Manager.Instance.GetShortAddress()} → " +
                $"{GetPlacementLabel(localPlacement)} (REAL placement)");

            if (postMatchUI != null && localPlacement > 0)
            {
                postMatchUI.ShowProcessing(localPlacement);
            }

            if (BattleRewardManager.Instance != null)
            {
                BattleRewardManager.Instance.DistributeRewards(players);
            }
        }

        /// <summary>
        /// FALLBACK TRIGGER — Local ship destroyed.
        /// Starts a timeout timer. If EliminationTracker provides results before the timer,
        /// great (HandleMatchResults handles it). If not, Update() triggers fallback.
        /// </summary>
        private void HandleLocalShipDestroyed()
        {
            if (_hasDistributedThisMatch) return;
            if (_localShipDead) return; // already tracking

            _localShipDead = true;
            _shipDeathTime = Time.time;

            Debug.Log("[BattleRewardBridge] Local ship destroyed! " +
                $"Waiting up to {eliminationTimeout}s for EliminationTracker results...");
        }

        /// <summary>
        /// FALLBACK — Match ended via OnRaceFinished.
        /// </summary>
        private void HandleRaceFinished()
        {
            if (_hasDistributedThisMatch) return;

            if (GV.Network.EliminationTracker.Instance != null && GV.Network.EliminationTracker.Instance.MatchEnded)
            {
                return; // EliminationTracker already handled it
            }

            Debug.Log("[BattleRewardBridge] Match ended via OnRaceFinished — distributing rewards...");
            DistributeWithDummyPlacement();
        }

        // --- Helpers ---

        private void DistributeWithDummyPlacement()
        {
            if (_hasDistributedThisMatch) return;
            _hasDistributedThisMatch = true;

            bool walletConnected = Web3Manager.Instance != null && Web3Manager.Instance.IsWalletConnected;

            if (!walletConnected)
            {
                Debug.LogWarning("[BattleRewardBridge] No wallet connected — showing placement but can't mint tokens.");
                if (postMatchUI != null) postMatchUI.ShowNoWallet(1);
                return;
            }

            var players = new List<PlayerRewardInfo>();
            players.Add(new PlayerRewardInfo
            {
                walletAddress = Web3Manager.Instance.WalletAddress,
                placement = 1
            });

            if (postMatchUI != null) postMatchUI.ShowProcessing(1);

            if (BattleRewardManager.Instance != null)
            {
                BattleRewardManager.Instance.DistributeRewards(players);
            }
        }

        private void TriggerTestRewards()
        {
            if (BattleRewardManager.Instance == null)
            {
                Debug.LogError("[BattleRewardBridge] BattleRewardManager not found!");
                return;
            }

            BattleRewardManager.Instance.TestDistributeRewards();
            if (postMatchUI != null) postMatchUI.ShowProcessing(1);
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
}
