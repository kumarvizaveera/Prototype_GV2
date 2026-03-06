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
    /// Manages character NFT ownership and selection.
    ///
    /// What this does:
    /// - Talks to the same ERC-1155 smart contract as ShipNFTManager (different token IDs)
    /// - Stores a list of all possible characters (configured in the Inspector)
    /// - After wallet connect, fetches the player's character NFT balances
    /// - Filters characters by the selected ship's roster (per-ship characters)
    /// - Lets the player select a character from the ones they own
    /// - The default character per roster is always available (no NFT needed)
    ///
    /// How to set it up:
    /// 1. Add this to the same GameObject as Web3Manager / ShipNFTManager
    /// 2. Paste the same contract address as ShipNFTManager (same ERC-1155 contract)
    /// 3. Configure characters in the Inspector — set names, token IDs, rarity, and ship roster index
    /// 4. shipRosterIndex: 0 = Spaceship/Roster A, 1 = Vimana/Roster B
    ///
    /// This is a singleton that survives scene changes.
    /// </summary>
    public class CharacterNFTManager : MonoBehaviour
    {
        public static CharacterNFTManager Instance { get; private set; }

        // --- Contract Config (set in Inspector) ---
        [Header("Contract Settings")]
        [Tooltip("The ERC-1155 contract address on Fuji. Same contract as ShipNFTManager — characters use different token IDs.")]
        [SerializeField] private string contractAddress = "";

        // --- Character Definitions (all editable in Inspector!) ---
        [Header("Character Roster")]
        [Tooltip("Configure all characters here. Names, rarities, roster assignment — change anytime.")]
        [SerializeField] private List<CharacterDefinition> characters = new List<CharacterDefinition>();

        // --- State ---
        // Which characters the player actually owns (by token ID)
        private Dictionary<int, int> _ownedCharacterBalances = new Dictionary<int, int>();

        // The character the player has selected for the next match
        private CharacterDefinition _selectedCharacter;

        // Is the manager currently fetching NFT data?
        private bool _isFetching = false;

        // Has the fetch completed at least once?
        private bool _hasFetchedOnce = false;

        // --- Public Properties ---

        /// <summary>All configured characters (from the Inspector).</summary>
        public IReadOnlyList<CharacterDefinition> AllCharacters => characters;

        /// <summary>The currently selected character. Returns the default if nothing is selected.</summary>
        public CharacterDefinition SelectedCharacter => _selectedCharacter ?? GetDefaultCharacter();

        /// <summary>Is the manager currently loading NFT data from the blockchain?</summary>
        public bool IsFetching => _isFetching;

        /// <summary>Has the NFT data been loaded at least once this session?</summary>
        public bool HasFetchedOnce => _hasFetchedOnce;

        // --- Events ---

        /// <summary>Fired when NFT balances have been fetched. Passes the list of owned characters.</summary>
        public event Action<List<CharacterDefinition>> OnCharactersFetched;

        /// <summary>Fired when the player selects a character. Passes the selected character.</summary>
        public event Action<CharacterDefinition> OnCharacterSelected;

        /// <summary>Fired if something goes wrong during fetch.</summary>
        public event Action<string> OnFetchError;

        // --- Singleton Setup ---
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[CharacterNFTManager] Duplicate detected, destroying this one.");
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            Debug.Log("[CharacterNFTManager] Initialized.");

            // Auto-select the default character
            _selectedCharacter = GetDefaultCharacter();
        }

        private void OnEnable()
        {
            // Automatically fetch characters when wallet connects
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
        /// Check if the player owns a specific character (by token ID).
        /// The default character always returns true.
        /// NOTE: This does NOT check isLocked — use IsCharacterAvailable() for selection checks.
        /// </summary>
        public bool OwnsCharacter(CharacterDefinition character)
        {
            if (character == null) return false;
            if (character.isDefault) return true;
            return _ownedCharacterBalances.ContainsKey(character.tokenId) && _ownedCharacterBalances[character.tokenId] > 0;
        }

        /// <summary>
        /// Check if a character is available for selection — must be owned AND not locked.
        /// Use this for UI decisions (card interactability, confirm button, etc.).
        /// </summary>
        public bool IsCharacterAvailable(CharacterDefinition character)
        {
            if (character == null) return false;
            if (character.isLocked) return false;
            return OwnsCharacter(character);
        }

        /// <summary>
        /// Get a list of all characters the player currently owns (including defaults).
        /// </summary>
        public List<CharacterDefinition> GetOwnedCharacters()
        {
            var owned = new List<CharacterDefinition>();
            foreach (var character in characters)
            {
                if (OwnsCharacter(character))
                {
                    owned.Add(character);
                }
            }
            return owned;
        }

        /// <summary>
        /// Get characters that belong to a specific ship roster.
        /// shipRosterIndex: 0 = Spaceship/Roster A, 1 = Vimana/Roster B
        /// </summary>
        public List<CharacterDefinition> GetCharactersForShip(int shipRosterIndex)
        {
            var matching = new List<CharacterDefinition>();
            foreach (var character in characters)
            {
                if (character.shipRosterIndex == shipRosterIndex)
                {
                    matching.Add(character);
                }
            }
            return matching;
        }

        /// <summary>
        /// Get owned characters filtered by ship roster.
        /// </summary>
        public List<CharacterDefinition> GetOwnedCharactersForShip(int shipRosterIndex)
        {
            var owned = new List<CharacterDefinition>();
            foreach (var character in characters)
            {
                if (character.shipRosterIndex == shipRosterIndex && OwnsCharacter(character))
                {
                    owned.Add(character);
                }
            }
            return owned;
        }

        /// <summary>
        /// Select a character for the next match.
        /// Returns false if the player doesn't own that character or it's locked.
        /// </summary>
        public bool SelectCharacter(CharacterDefinition character)
        {
            if (character == null)
            {
                Debug.LogWarning("[CharacterNFTManager] Can't select null character.");
                return false;
            }

            if (character.isLocked)
            {
                Debug.LogWarning($"[CharacterNFTManager] Character is locked: {character.displayName}");
                return false;
            }

            if (!OwnsCharacter(character))
            {
                Debug.LogWarning($"[CharacterNFTManager] Player doesn't own character: {character.displayName}");
                return false;
            }

            _selectedCharacter = character;
            Debug.Log($"[CharacterNFTManager] Selected character: {character.displayName} (roster: {character.shipRosterIndex})");
            OnCharacterSelected?.Invoke(character);
            return true;
        }

        /// <summary>
        /// Select a character by its token ID.
        /// </summary>
        public bool SelectCharacterByTokenId(int tokenId)
        {
            var character = characters.Find(c => c.tokenId == tokenId);
            if (character == null)
            {
                Debug.LogWarning($"[CharacterNFTManager] No character found with token ID: {tokenId}");
                return false;
            }
            return SelectCharacter(character);
        }

        /// <summary>
        /// Manually trigger a fetch of NFT balances.
        /// Normally this happens automatically when the wallet connects.
        /// </summary>
        public async void FetchPlayerCharacters()
        {
            await FetchCharacterBalances();
        }

        /// <summary>
        /// Get the default (free) character. Every player always has access to this one.
        /// Optionally filter by ship roster index.
        /// </summary>
        public CharacterDefinition GetDefaultCharacter(int shipRosterIndex = -1)
        {
            CharacterDefinition defaultChar = null;

            foreach (var c in characters)
            {
                if (c.isDefault)
                {
                    if (shipRosterIndex < 0 || c.shipRosterIndex == shipRosterIndex)
                    {
                        return c;
                    }
                    if (defaultChar == null) defaultChar = c; // Fallback: any default
                }
            }

            if (defaultChar == null && characters.Count > 0)
            {
                Debug.LogWarning("[CharacterNFTManager] No default character configured! Using the first character in the list.");
                return characters[0];
            }
            return defaultChar;
        }

        // --- Internal Logic ---

        private void HandleWalletConnected(string address)
        {
            Debug.Log($"[CharacterNFTManager] Wallet connected, fetching character NFTs...");
            FetchPlayerCharacters();
        }

        private void HandleWalletDisconnected()
        {
            Debug.Log("[CharacterNFTManager] Wallet disconnected, clearing character data.");
            _ownedCharacterBalances.Clear();
            _hasFetchedOnce = false;
            _selectedCharacter = GetDefaultCharacter();
        }

        /// <summary>
        /// The core function that reads the blockchain to see which characters the player owns.
        /// Same approach as ShipNFTManager — queries balanceOf for each character token ID.
        /// </summary>
        private async Task FetchCharacterBalances()
        {
            if (_isFetching)
            {
                Debug.LogWarning("[CharacterNFTManager] Already fetching, please wait...");
                return;
            }

            if (string.IsNullOrEmpty(contractAddress))
            {
                Debug.LogWarning("[CharacterNFTManager] No contract address set! Skipping NFT fetch. " +
                    "Set the contract address in the Inspector after deploying via Thirdweb dashboard.");
                _hasFetchedOnce = true;
                OnCharactersFetched?.Invoke(GetOwnedCharacters());
                return;
            }

            if (Web3Manager.Instance == null || !Web3Manager.Instance.IsWalletConnected)
            {
                Debug.LogWarning("[CharacterNFTManager] Wallet not connected, can't fetch characters.");
                return;
            }

            _isFetching = true;

            try
            {
                var client = ThirdwebManager.Instance.Client;
                var chainId = Web3Manager.Instance.ChainId;
                var playerAddress = Web3Manager.Instance.WalletAddress;

                var contract = await ThirdwebContract.Create(
                    client: client,
                    address: contractAddress,
                    chain: chainId
                );

                Debug.Log($"[CharacterNFTManager] Querying character NFT balances for {playerAddress}...");

                _ownedCharacterBalances.Clear();

                foreach (var character in characters)
                {
                    if (character.isDefault) continue;

                    try
                    {
                        var balance = await ThirdwebContract.Read<BigInteger>(
                            contract: contract,
                            method: "balanceOf",
                            playerAddress,
                            character.tokenId
                        );

                        int balanceInt = (int)balance;
                        _ownedCharacterBalances[character.tokenId] = balanceInt;

                        Debug.Log($"[CharacterNFTManager] Character '{character.displayName}' (ID {character.tokenId}): " +
                            $"balance = {balanceInt} {(balanceInt > 0 ? "OWNED" : "not owned")}");
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[CharacterNFTManager] Failed to check balance for token {character.tokenId}: {ex.Message}");
                        _ownedCharacterBalances[character.tokenId] = 0;
                    }
                }

                _hasFetchedOnce = true;

                var ownedCharacters = GetOwnedCharacters();
                Debug.Log($"[CharacterNFTManager] Fetch complete. Player owns {ownedCharacters.Count} character(s).");
                OnCharactersFetched?.Invoke(ownedCharacters);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CharacterNFTManager] Failed to fetch character NFTs: {ex.Message}");
                OnFetchError?.Invoke($"Failed to load characters: {ex.Message}");

                _hasFetchedOnce = true;
                OnCharactersFetched?.Invoke(GetOwnedCharacters());
            }
            finally
            {
                _isFetching = false;
            }
        }
    }

    // =========================================================================
    // Character Definition — this is what you edit in the Inspector
    // =========================================================================

    /// <summary>
    /// Defines a single character type. All fields are editable in the Inspector.
    /// Nothing here is stored on the blockchain — only the token ID links to the NFT.
    ///
    /// shipRosterIndex ties this character to a specific ship type:
    /// - 0 = Spaceship / Roster A
    /// - 1 = Vimana / Roster B
    /// This matches the AircraftCharacterManager's rosterA/rosterB system.
    /// </summary>
    [Serializable]
    public class CharacterDefinition
    {
        [Tooltip("The name shown in the character selection screen.")]
        public string displayName = "New Character";

        [Tooltip("A short description for the selection screen.")]
        [TextArea(2, 4)]
        public string description = "";

        [Tooltip("The NFT token ID on the ERC-1155 contract. Must match what was minted. Ignored for the default character.")]
        public int tokenId = 0;

        [Tooltip("Character rarity tier — purely cosmetic/organizational.")]
        public CharacterRarity rarity = CharacterRarity.Common;

        [Tooltip("Which ship roster this character belongs to. 0 = Spaceship/Roster A, 1 = Vimana/Roster B.")]
        public int shipRosterIndex = 0;

        [Tooltip("If true, every player gets this character for free (no NFT required). " +
                 "You should have at least one default per ship roster.")]
        public bool isDefault = false;

        [Tooltip("If true, this character cannot be selected regardless of ownership " +
                 "(e.g. coming soon, level-gated, temporarily disabled).")]
        public bool isLocked = false;

        [Tooltip("Optional: a sprite/icon for the character selection UI.")]
        public Sprite icon;
    }

    /// <summary>
    /// Rarity tiers for characters. Just labels — change or add more anytime.
    /// </summary>
    public enum CharacterRarity
    {
        Common,
        Uncommon,
        Rare,
        Legendary
    }
}
