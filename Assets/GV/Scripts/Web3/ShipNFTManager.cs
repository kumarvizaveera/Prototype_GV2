using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using UnityEngine;
using Thirdweb;
using Thirdweb.Unity;

namespace GV.Web3
{
    /// <summary>
    /// Manages ship NFT ownership and selection.
    ///
    /// What this does:
    /// - Talks to the ERC-1155 smart contract on Fuji to check which ships the player owns
    /// - Stores a list of all possible ships (configured in the Inspector — names, rarities, etc.)
    /// - After wallet connect, fetches the player's NFT balances
    /// - Lets the player select a ship from the ones they own
    /// - The default ship is always available (no NFT needed)
    ///
    /// How to set it up:
    /// 1. Add this to the same GameObject as Web3Manager (or any persistent object)
    /// 2. Paste the contract address (you get this after deploying via Thirdweb dashboard)
    /// 3. Configure ships in the Inspector — set names, token IDs, rarity, and mesh index
    /// 4. Ship names, rarities, etc. are all editable anytime — nothing is stored on-chain
    ///
    /// This is a singleton that survives scene changes, just like Web3Manager.
    /// </summary>
    public class ShipNFTManager : MonoBehaviour
    {
        public static ShipNFTManager Instance { get; private set; }

        // --- Contract Config (set in Inspector) ---
        [Header("Contract Settings")]
        [Tooltip("The ERC-1155 contract address on Fuji. Get this from the Thirdweb dashboard after deploying.")]
        [SerializeField] private string contractAddress = "";

        // --- Ship Definitions (all editable in Inspector!) ---
        [Header("Ship Roster")]
        [Tooltip("Configure all ships here. Names, rarities, mesh indices — change anytime.")]
        [SerializeField] private List<ShipDefinition> ships = new List<ShipDefinition>();

        // --- State ---
        // Which ships the player actually owns (by token ID)
        private Dictionary<int, int> _ownedShipBalances = new Dictionary<int, int>();

        // The ship the player has selected for the next match
        private ShipDefinition _selectedShip;

        // Is the manager currently fetching NFT data?
        private bool _isFetching = false;

        // Has the fetch completed at least once?
        private bool _hasFetchedOnce = false;

        // --- Public Properties ---

        /// <summary>All configured ships (from the Inspector).</summary>
        public IReadOnlyList<ShipDefinition> AllShips => ships;

        /// <summary>The currently selected ship. Returns the default ship if nothing is selected.</summary>
        public ShipDefinition SelectedShip => _selectedShip ?? GetDefaultShip();

        /// <summary>Is the manager currently loading NFT data from the blockchain?</summary>
        public bool IsFetching => _isFetching;

        /// <summary>Has the NFT data been loaded at least once this session?</summary>
        public bool HasFetchedOnce => _hasFetchedOnce;

        // --- Events ---

        /// <summary>Fired when NFT balances have been fetched. Passes the list of owned ships.</summary>
        public event Action<List<ShipDefinition>> OnShipsFetched;

        /// <summary>Fired when the player selects a ship. Passes the selected ship.</summary>
        public event Action<ShipDefinition> OnShipSelected;

        /// <summary>Fired if something goes wrong during fetch.</summary>
        public event Action<string> OnFetchError;

        // --- Singleton Setup ---
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[ShipNFTManager] Duplicate detected, destroying this one.");
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            Debug.Log("[ShipNFTManager] Initialized.");

            // Auto-select the default ship
            _selectedShip = GetDefaultShip();
        }

        private void OnEnable()
        {
            // Automatically fetch ships when wallet connects
            if (Web3Manager.Instance != null)
            {
                Web3Manager.Instance.OnWalletConnected += HandleWalletConnected;
                Web3Manager.Instance.OnWalletDisconnected += HandleWalletDisconnected;
            }
        }

        private void OnDisable()
        {
            if (Web3Manager.Instance != null)
            {
                Web3Manager.Instance.OnWalletConnected -= HandleWalletConnected;
                Web3Manager.Instance.OnWalletDisconnected -= HandleWalletDisconnected;
            }
        }

        // --- Public Methods ---

        /// <summary>
        /// Check if the player owns a specific ship (by token ID).
        /// The default ship always returns true.
        /// NOTE: This does NOT check isLocked — use IsShipAvailable() for selection checks.
        /// </summary>
        public bool OwnsShip(ShipDefinition ship)
        {
            if (ship == null) return false;
            if (ship.isDefault) return true; // Default ship is always available
            return _ownedShipBalances.ContainsKey(ship.tokenId) && _ownedShipBalances[ship.tokenId] > 0;
        }

        /// <summary>
        /// Check if a ship is available for selection — must be owned AND not locked.
        /// Use this for UI decisions (card interactability, confirm button, etc.).
        /// </summary>
        public bool IsShipAvailable(ShipDefinition ship)
        {
            if (ship == null) return false;
            if (ship.isLocked) return false;
            return OwnsShip(ship);
        }

        /// <summary>
        /// Get a list of all ships the player currently owns (including the default).
        /// </summary>
        public List<ShipDefinition> GetOwnedShips()
        {
            var owned = new List<ShipDefinition>();
            foreach (var ship in ships)
            {
                if (OwnsShip(ship))
                {
                    owned.Add(ship);
                }
            }
            return owned;
        }

        /// <summary>
        /// Select a ship for the next match.
        /// Returns false if the player doesn't own that ship.
        /// </summary>
        public bool SelectShip(ShipDefinition ship)
        {
            if (ship == null)
            {
                Debug.LogWarning("[ShipNFTManager] Can't select null ship.");
                return false;
            }

            if (ship.isLocked)
            {
                Debug.LogWarning($"[ShipNFTManager] Ship is locked: {ship.displayName}");
                return false;
            }

            if (!OwnsShip(ship))
            {
                Debug.LogWarning($"[ShipNFTManager] Player doesn't own ship: {ship.displayName}");
                return false;
            }

            _selectedShip = ship;
            Debug.Log($"[ShipNFTManager] Selected ship: {ship.displayName} (mesh index: {ship.meshRootIndex})");
            OnShipSelected?.Invoke(ship);
            return true;
        }

        /// <summary>
        /// Select a ship by its token ID.
        /// </summary>
        public bool SelectShipByTokenId(int tokenId)
        {
            var ship = ships.Find(s => s.tokenId == tokenId);
            if (ship == null)
            {
                Debug.LogWarning($"[ShipNFTManager] No ship found with token ID: {tokenId}");
                return false;
            }
            return SelectShip(ship);
        }

        /// <summary>
        /// Manually trigger a fetch of NFT balances.
        /// Normally this happens automatically when the wallet connects.
        /// </summary>
        public async void FetchPlayerShips()
        {
            await FetchShipBalances();
        }

        /// <summary>
        /// Get the default (free) ship. Every player always has access to this one.
        /// </summary>
        public ShipDefinition GetDefaultShip()
        {
            var defaultShip = ships.Find(s => s.isDefault);
            if (defaultShip == null && ships.Count > 0)
            {
                Debug.LogWarning("[ShipNFTManager] No default ship configured! Using the first ship in the list.");
                return ships[0];
            }
            return defaultShip;
        }

        // --- Internal Logic ---

        private void HandleWalletConnected(string address)
        {
            Debug.Log($"[ShipNFTManager] Wallet connected, fetching ship NFTs...");
            FetchPlayerShips();
        }

        private void HandleWalletDisconnected()
        {
            Debug.Log("[ShipNFTManager] Wallet disconnected, clearing ship data.");
            _ownedShipBalances.Clear();
            _hasFetchedOnce = false;
            _selectedShip = GetDefaultShip();
        }

        /// <summary>
        /// The core function that reads the blockchain to see which ships the player owns.
        ///
        /// How it works:
        /// 1. Gets a reference to the ERC-1155 contract using Thirdweb SDK
        /// 2. For each non-default ship, calls balanceOf(playerAddress, tokenId)
        ///    — this is a free "read" call, no gas cost
        /// 3. Stores the results so the UI can show which ships are owned
        /// 4. Fires OnShipsFetched so the selection screen can update
        /// </summary>
        private async Task FetchShipBalances()
        {
            if (_isFetching)
            {
                Debug.LogWarning("[ShipNFTManager] Already fetching, please wait...");
                return;
            }

            if (string.IsNullOrEmpty(contractAddress))
            {
                Debug.LogWarning("[ShipNFTManager] No contract address set! Skipping NFT fetch. " +
                    "Set the contract address in the Inspector after deploying via Thirdweb dashboard.");
                // Still fire the event so UI works (just with default ship only)
                _hasFetchedOnce = true;
                OnShipsFetched?.Invoke(GetOwnedShips());
                return;
            }

            if (Web3Manager.Instance == null || !Web3Manager.Instance.IsWalletConnected)
            {
                Debug.LogWarning("[ShipNFTManager] Wallet not connected, can't fetch ships.");
                return;
            }

            _isFetching = true;

            try
            {
                var client = ThirdwebManager.Instance.Client;
                var chainId = Web3Manager.Instance.ChainId;
                var playerAddress = Web3Manager.Instance.WalletAddress;

                // Get a reference to the contract (doesn't cost anything)
                var contract = await ThirdwebContract.Create(
                    client: client,
                    address: contractAddress,
                    chain: chainId
                );

                Debug.Log($"[ShipNFTManager] Querying NFT balances for {playerAddress}...");

                _ownedShipBalances.Clear();

                // Check balance for each non-default ship
                foreach (var ship in ships)
                {
                    if (ship.isDefault) continue; // Default is always owned, no need to check

                    try
                    {
                        // balanceOf(address, tokenId) — standard ERC-1155 function
                        // Returns how many of that token the player holds
                        var balance = await ThirdwebContract.Read<BigInteger>(
                            contract: contract,
                            method: "balanceOf",
                            playerAddress,
                            ship.tokenId
                        );

                        int balanceInt = (int)balance;
                        _ownedShipBalances[ship.tokenId] = balanceInt;

                        Debug.Log($"[ShipNFTManager] Ship '{ship.displayName}' (ID {ship.tokenId}): " +
                            $"balance = {balanceInt} {(balanceInt > 0 ? "OWNED" : "not owned")}");
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[ShipNFTManager] Failed to check balance for token {ship.tokenId}: {ex.Message}");
                        _ownedShipBalances[ship.tokenId] = 0;
                    }
                }

                _hasFetchedOnce = true;

                var ownedShips = GetOwnedShips();
                Debug.Log($"[ShipNFTManager] Fetch complete. Player owns {ownedShips.Count} ship(s).");
                OnShipsFetched?.Invoke(ownedShips);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ShipNFTManager] Failed to fetch ship NFTs: {ex.Message}");
                OnFetchError?.Invoke($"Failed to load ships: {ex.Message}");

                // Still mark as fetched so UI can proceed with default ship
                _hasFetchedOnce = true;
                OnShipsFetched?.Invoke(GetOwnedShips());
            }
            finally
            {
                _isFetching = false;
            }
        }
    }

    // =========================================================================
    // Ship Definition — this is what you edit in the Inspector
    // =========================================================================

    /// <summary>
    /// Defines a single ship type. All of these fields are editable in the Inspector.
    /// Nothing here is stored on the blockchain — only the token ID links to the NFT.
    ///
    /// Think of it this way:
    /// - tokenId = the NFT's ID number on the blockchain (permanent)
    /// - Everything else = labels and config in your game (change anytime)
    /// </summary>
    [Serializable]
    public class ShipDefinition
    {
        [Tooltip("The name shown in the ship selection screen. Change this anytime — it's not on-chain.")]
        public string displayName = "New Ship";

        [Tooltip("A short description for the selection screen.")]
        [TextArea(2, 4)]
        public string description = "";

        [Tooltip("The NFT token ID on the ERC-1155 contract. Token ID 1, 2, 3, etc. " +
                 "This must match what was minted on the blockchain. Ignored for the default ship.")]
        public int tokenId = 0;

        [Tooltip("Ship rarity tier — purely cosmetic/organizational, change anytime.")]
        public ShipRarity rarity = ShipRarity.Common;

        [Tooltip("Which mesh root to activate. 0 = Aircraft A (Spaceship), 1 = Aircraft B (Vimana). " +
                 "Add more as you add new ship models.")]
        public int meshRootIndex = 0;

        [Tooltip("If true, every player gets this ship for free (no NFT required). " +
                 "You should have exactly one default ship.")]
        public bool isDefault = false;

        [Tooltip("If true, this ship cannot be selected regardless of ownership " +
                 "(e.g. coming soon, level-gated, temporarily disabled).")]
        public bool isLocked = false;

        [Tooltip("Optional: a sprite/icon for the ship selection UI.")]
        public Sprite icon;
    }

    /// <summary>
    /// Rarity tiers for ships. Just labels — change or add more anytime.
    /// </summary>
    public enum ShipRarity
    {
        Common,
        Uncommon,
        Rare,
        Legendary
    }
}
