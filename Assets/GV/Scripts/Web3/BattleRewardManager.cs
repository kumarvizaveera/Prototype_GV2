using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using UnityEngine;
using Thirdweb;
using Thirdweb.Unity;
using Nethereum.Web3;
using Nethereum.Web3.Accounts;
using Nethereum.Contracts;
using Nethereum.Hex.HexTypes;
using Nethereum.RPC.Eth.DTOs;

namespace GV.Web3
{
    /// <summary>
    /// Manages token rewards after matches.
    ///
    /// What this does:
    /// - After a match ends, it mints ERC-20 tokens to each player based on their placement
    /// - Uses a dedicated "reward wallet" (a regular wallet with Minter permission) to sign mint transactions
    /// - All reward amounts, token name, and contract address are configurable in the Inspector
    /// - Fires events so the post-match UI can show what the player earned
    ///
    /// How it works under the hood:
    /// - The reward wallet is a simple private-key wallet that has "Minter" role on the token contract
    /// - Uses Nethereum (an Ethereum .NET library) to send the mint transaction directly
    /// - When a match ends, this script calls mintTo(playerAddress, amount) on the ERC-20 contract
    /// - The reward wallet pays a tiny gas fee (fractions of a cent on Fuji testnet)
    /// - The player sees new tokens appear in their wallet
    ///
    /// Why Nethereum instead of Thirdweb for minting:
    /// - Thirdweb SDK v6's PrivateKeyWallet class isn't available in the Unity build
    /// - Nethereum is already installed (comes with the Reown package) and works perfectly
    /// - Balance reading still uses Thirdweb SDK (it works great for read-only calls)
    ///
    /// ⚠️ PROTOTYPE / TESTNET ONLY:
    /// The reward wallet's private key is stored in the Inspector for simplicity.
    /// In production, token minting would go through a secure backend server —
    /// NEVER ship a private key in a real game build.
    ///
    /// How to set it up:
    /// 1. Deploy an ERC-20 token contract on Fuji (via Thirdweb dashboard)
    /// 2. Create a new wallet and fund it with test AVAX
    /// 3. Grant that wallet "Minter" role on the token contract
    /// 4. Paste the contract address and wallet private key into the Inspector fields below
    /// 5. Configure reward amounts per placement (1st, 2nd, 3rd, 4th)
    ///
    /// This is a singleton that survives scene changes, just like Web3Manager.
    /// </summary>
    public class BattleRewardManager : MonoBehaviour
    {
        public static BattleRewardManager Instance { get; private set; }

        // --- Token Contract Config ---
        [Header("Token Contract")]
        [Tooltip("The ERC-20 token contract address on Fuji. Get this from the Thirdweb dashboard.")]
        [SerializeField] private string tokenContractAddress = "0xBF7c298C0f3E4745Ec902ED0008223747EEbd0d1";

        [Tooltip("Display name for the token (shown in UI). Change anytime.")]
        [SerializeField] private string tokenDisplayName = "PRANA";

        [Tooltip("Token symbol (shown in HUD next to balance). Change anytime.")]
        [SerializeField] private string tokenSymbol = "PRANA";

        [Tooltip("Number of decimals the token uses. Standard ERC-20 is 18. Don't change unless your contract uses a different value.")]
        [SerializeField] private int tokenDecimals = 18;

        // --- Reward Wallet (signs the mint transactions) ---
        [Header("Reward Wallet (PROTOTYPE / TESTNET ONLY)")]
        [Tooltip("Private key of the wallet that has Minter role on the token contract. " +
                 "⚠️ NEVER use this in a production build — move to a backend server instead.")]
        [SerializeField] private string rewardWalletPrivateKey = "0a441d05d1d856a59dde57ae027db86d41998eaeb96335467021d95ff2b76a32";

        // --- Network Config ---
        [Header("Network")]
        [Tooltip("Avalanche Fuji C-Chain RPC URL. Used by the reward wallet to send transactions.")]
        [SerializeField] private string rpcUrl = "https://api.avax-test.network/ext/bc/C/rpc";

        // --- Bonus Reward Names (display only, customizable) ---
        [Header("Bonus Reward Labels")]
        [Tooltip("What to call the first bonus reward in the UI (e.g. 'XP', 'Experience', 'Shakti').")]
        [SerializeField] private string bonusReward1Name = "XP";

        [Tooltip("What to call the second bonus reward in the UI (e.g. 'Energy', 'Prana Energy', 'Tejas').")]
        [SerializeField] private string bonusReward2Name = "Energy";

        [Tooltip("What to call the third bonus reward in the UI (e.g. 'Gems', 'Ratnas', 'Crystals').")]
        [SerializeField] private string bonusReward3Name = "Gems";

        [Tooltip("What to call the fourth bonus reward in the UI (e.g. 'Coins', 'Gold', 'Mudra').")]
        [SerializeField] private string bonusReward4Name = "Coins";

        // --- Reward Amounts Per Placement ---
        [Header("Reward Configuration")]
        [Tooltip("How many tokens each placement earns. Index 0 = 1st place, Index 1 = 2nd place, etc. " +
                 "These are whole token amounts (e.g. 100 means 100 PRANA).")]
        [SerializeField] private List<PlacementReward> rewardsByPlacement = new List<PlacementReward>()
        {
            new PlacementReward { placementLabel = "1st Place", tokenAmount = 100, xpAmount = 500, energyAmount = 50, gemsAmount = 10, coinsAmount = 200 },
            new PlacementReward { placementLabel = "2nd Place", tokenAmount = 60,  xpAmount = 300, energyAmount = 30, gemsAmount = 5,  coinsAmount = 120 },
            new PlacementReward { placementLabel = "3rd Place", tokenAmount = 30,  xpAmount = 150, energyAmount = 15, gemsAmount = 2,  coinsAmount = 60 },
            new PlacementReward { placementLabel = "4th Place", tokenAmount = 10,  xpAmount = 50,  energyAmount = 5,  gemsAmount = 0,  coinsAmount = 20 },
        };

        // --- State ---
        // The Nethereum web3 instance (created from the reward wallet's private key)
        private Nethereum.Web3.Web3 _rewardWeb3;

        // Is a reward distribution currently in progress?
        private bool _isDistributing = false;

        // The player's current token balance (formatted for display)
        private string _tokenBalanceFormatted = "0";

        // Results from the last reward distribution
        private List<RewardResult> _lastResults = new List<RewardResult>();

        // The ABI for the mintTo function — this is the "signature" that tells
        // the blockchain exactly what function we want to call and what parameters it takes
        private const string MINT_TO_ABI = @"[{
            ""inputs"": [
                { ""name"": ""to"", ""type"": ""address"" },
                { ""name"": ""amount"", ""type"": ""uint256"" }
            ],
            ""name"": ""mintTo"",
            ""outputs"": [],
            ""stateMutability"": ""nonpayable"",
            ""type"": ""function""
        }]";

        // --- Public Properties ---

        /// <summary>Display name of the game token (e.g. "PRANA").</summary>
        public string TokenDisplayName => tokenDisplayName;

        /// <summary>Token symbol (e.g. "PRANA").</summary>
        public string TokenSymbol => tokenSymbol;

        /// <summary>Name for bonus reward 1 (e.g. "XP").</summary>
        public string BonusReward1Name => bonusReward1Name;

        /// <summary>Name for bonus reward 2 (e.g. "Energy").</summary>
        public string BonusReward2Name => bonusReward2Name;

        /// <summary>Name for bonus reward 3 (e.g. "Gems").</summary>
        public string BonusReward3Name => bonusReward3Name;

        /// <summary>Name for bonus reward 4 (e.g. "Coins").</summary>
        public string BonusReward4Name => bonusReward4Name;

        /// <summary>The player's token balance formatted for display (e.g. "150 PRANA").</summary>
        public string TokenBalanceFormatted => _tokenBalanceFormatted;

        /// <summary>Is a reward distribution currently happening?</summary>
        public bool IsDistributing => _isDistributing;

        /// <summary>Results from the most recent reward distribution.</summary>
        public IReadOnlyList<RewardResult> LastResults => _lastResults;

        /// <summary>The reward config list (read-only from outside).</summary>
        public IReadOnlyList<PlacementReward> RewardConfig => rewardsByPlacement;

        // --- Events ---

        /// <summary>Fired when rewards have been distributed. Passes the list of results (who got what).</summary>
        public event Action<List<RewardResult>> OnRewardsDistributed;

        /// <summary>Fired when a single player's reward mint succeeds. Passes address and amount.</summary>
        public event Action<string, float> OnRewardMinted;

        /// <summary>Fired when the player's token balance updates. Passes the formatted balance string.</summary>
        public event Action<string> OnTokenBalanceUpdated;

        /// <summary>Fired if something goes wrong. Passes the error message.</summary>
        public event Action<string> OnRewardError;

        // --- Singleton Setup ---
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[BattleRewardManager] Duplicate detected, destroying this one.");
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            Debug.Log($"[BattleRewardManager] Initialized. Token: {tokenDisplayName} ({tokenSymbol})");
        }

        private void OnEnable()
        {
            // Fetch token balance when wallet connects
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
        /// Get how many tokens a specific placement earns.
        /// Placement is 1-based (1 = first place, 2 = second, etc.).
        /// Returns 0 if placement is out of range.
        /// </summary>
        public float GetRewardForPlacement(int placement)
        {
            int index = placement - 1; // Convert to 0-based index
            if (index >= 0 && index < rewardsByPlacement.Count)
            {
                return rewardsByPlacement[index].tokenAmount;
            }
            return 0f;
        }

        /// <summary>
        /// Get the full reward config for a specific placement (tokens, XP, energy, gems).
        /// Placement is 1-based. Returns null if out of range.
        /// </summary>
        public PlacementReward GetPlacementReward(int placement)
        {
            int index = placement - 1;
            if (index >= 0 && index < rewardsByPlacement.Count)
            {
                return rewardsByPlacement[index];
            }
            return null;
        }

        /// <summary>
        /// Distribute rewards to all players based on their placements.
        ///
        /// Call this when the match ends. Pass a list of PlayerRewardInfo
        /// (wallet address + placement for each player).
        ///
        /// How it works:
        /// 1. Looks up how many tokens each placement earns (from the Inspector config)
        /// 2. Creates a Nethereum Web3 instance from the reward wallet's private key
        /// 3. For each player, calls mintTo(playerAddress, tokenAmount) on the ERC-20 contract
        /// 4. Fires OnRewardsDistributed with the results when done
        ///
        /// This runs in the background — it won't freeze the game.
        /// </summary>
        public async void DistributeRewards(List<PlayerRewardInfo> players)
        {
            if (_isDistributing)
            {
                Debug.LogWarning("[BattleRewardManager] Already distributing rewards, please wait...");
                return;
            }

            if (players == null || players.Count == 0)
            {
                Debug.LogWarning("[BattleRewardManager] No players to reward.");
                return;
            }

            _isDistributing = true;
            _lastResults.Clear();

            Debug.Log($"[BattleRewardManager] Distributing rewards to {players.Count} player(s)...");

            try
            {
                // Step 1: Create the Nethereum web3 instance from the private key
                var web3 = GetOrCreateRewardWeb3();
                if (web3 == null)
                {
                    OnRewardError?.Invoke("Reward wallet not configured. Check the private key in Inspector.");
                    return;
                }

                // Step 2: Get a reference to the token contract via Nethereum
                var contract = web3.Eth.GetContract(MINT_TO_ABI, tokenContractAddress);
                var mintToFunction = contract.GetFunction("mintTo");

                // Step 3: Mint tokens to each player
                foreach (var player in players)
                {
                    float rewardAmount = GetRewardForPlacement(player.placement);

                    if (rewardAmount <= 0)
                    {
                        Debug.Log($"[BattleRewardManager] Player {ShortenAddress(player.walletAddress)} " +
                            $"(placement {player.placement}) — no reward configured for this placement.");

                        _lastResults.Add(new RewardResult
                        {
                            walletAddress = player.walletAddress,
                            placement = player.placement,
                            tokenAmount = 0,
                            success = true,
                            message = "No reward for this placement"
                        });
                        continue;
                    }

                    // Convert whole tokens to wei (the smallest unit)
                    // Example: 100 tokens with 18 decimals = 100 * 10^18 wei
                    BigInteger amountInWei = ToWei(rewardAmount);

                    try
                    {
                        Debug.Log($"[BattleRewardManager] Minting {rewardAmount} {tokenSymbol} " +
                            $"to {ShortenAddress(player.walletAddress)} (placement: {player.placement})...");

                        // Get the latest nonce (transaction count) from the network
                        // This prevents "replacement transaction underpriced" errors when
                        // previous transactions are still pending or nonce gets cached stale
                        var nonce = await web3.Eth.Transactions.GetTransactionCount
                            .SendRequestAsync(
                                web3.TransactionManager.Account.Address,
                                Nethereum.RPC.Eth.DTOs.BlockParameter.CreatePending()
                            );

                        Debug.Log($"[BattleRewardManager] Using nonce: {nonce.Value}");

                        // Call mintTo(address, amount) on the ERC-20 contract
                        // Nethereum sends a standard transaction signed by the reward wallet
                        var txInput = mintToFunction.CreateTransactionInput(
                            from: web3.TransactionManager.Account.Address,
                            gas: new HexBigInteger(200000), // gas limit (plenty for a mint)
                            value: new HexBigInteger(0),     // no AVAX sent with this call
                            functionInput: new object[] { player.walletAddress, amountInWei }
                        );
                        txInput.Nonce = nonce;

                        var txHash = await web3.Eth.TransactionManager
                            .SendTransactionAsync(txInput);

                        Debug.Log($"[BattleRewardManager] Tx sent: {txHash}. Waiting for confirmation...");

                        // Wait for the transaction to be confirmed on-chain
                        // Without this, the balance check would run before the mint is processed
                        var receipt = await web3.Eth.Transactions.GetTransactionReceipt
                            .SendRequestAsync(txHash);

                        // Poll until confirmed (Fuji is fast, usually 1-2 seconds)
                        int attempts = 0;
                        while (receipt == null && attempts < 30)
                        {
                            await Task.Delay(1000); // wait 1 second between checks
                            receipt = await web3.Eth.Transactions.GetTransactionReceipt
                                .SendRequestAsync(txHash);
                            attempts++;
                        }

                        if (receipt != null && receipt.Status.Value == 1)
                        {
                            Debug.Log($"[BattleRewardManager] ✓ Minted {rewardAmount} {tokenSymbol} " +
                                $"to {ShortenAddress(player.walletAddress)}. Tx: {txHash}");
                        }
                        else
                        {
                            Debug.LogWarning($"[BattleRewardManager] Tx sent but confirmation unclear. Tx: {txHash}");
                        }

                        _lastResults.Add(new RewardResult
                        {
                            walletAddress = player.walletAddress,
                            placement = player.placement,
                            tokenAmount = rewardAmount,
                            success = true,
                            message = "Tokens minted successfully"
                        });

                        OnRewardMinted?.Invoke(player.walletAddress, rewardAmount);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[BattleRewardManager] ✗ Failed to mint for " +
                            $"{ShortenAddress(player.walletAddress)}: {ex.Message}");

                        _lastResults.Add(new RewardResult
                        {
                            walletAddress = player.walletAddress,
                            placement = player.placement,
                            tokenAmount = rewardAmount,
                            success = false,
                            message = $"Mint failed: {ex.Message}"
                        });
                    }
                }

                Debug.Log($"[BattleRewardManager] Reward distribution complete. " +
                    $"{_lastResults.FindAll(r => r.success).Count}/{_lastResults.Count} succeeded.");

                OnRewardsDistributed?.Invoke(new List<RewardResult>(_lastResults));

                // Refresh the local player's token balance after minting
                await FetchTokenBalance();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[BattleRewardManager] Reward distribution failed: {ex.Message}");
                OnRewardError?.Invoke($"Reward distribution failed: {ex.Message}");
            }
            finally
            {
                _isDistributing = false;
            }
        }

        /// <summary>
        /// Fetch the connected player's token balance from the blockchain.
        /// Uses Thirdweb SDK for reading (it works great for read-only calls).
        /// This is a free "read" call — no gas cost.
        /// </summary>
        public async Task FetchTokenBalance()
        {
            if (Web3Manager.Instance == null || !Web3Manager.Instance.IsWalletConnected)
            {
                _tokenBalanceFormatted = $"0 {tokenSymbol}";
                OnTokenBalanceUpdated?.Invoke(_tokenBalanceFormatted);
                return;
            }

            if (string.IsNullOrEmpty(tokenContractAddress))
            {
                _tokenBalanceFormatted = $"? {tokenSymbol}";
                OnTokenBalanceUpdated?.Invoke(_tokenBalanceFormatted);
                return;
            }

            try
            {
                var client = ThirdwebManager.Instance.Client;
                var chainId = Web3Manager.Instance.ChainId;
                var playerAddress = Web3Manager.Instance.WalletAddress;

                var contract = await ThirdwebContract.Create(
                    client: client,
                    address: tokenContractAddress,
                    chain: chainId
                );

                // Call balanceOf(playerAddress) on the ERC-20 contract
                var balanceWei = await ThirdwebContract.Read<BigInteger>(
                    contract: contract,
                    method: "balanceOf",
                    playerAddress
                );

                // Convert from wei to whole tokens
                float balanceTokens = FromWei(balanceWei);

                // Format nicely — no unnecessary decimals for whole numbers
                string balanceStr = balanceTokens % 1 == 0
                    ? ((int)balanceTokens).ToString()
                    : balanceTokens.ToString("F2");

                _tokenBalanceFormatted = $"{balanceStr} {tokenSymbol}";

                Debug.Log($"[BattleRewardManager] Token balance: {_tokenBalanceFormatted}");
                OnTokenBalanceUpdated?.Invoke(_tokenBalanceFormatted);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[BattleRewardManager] Failed to fetch token balance: {ex.Message}");
                _tokenBalanceFormatted = $"? {tokenSymbol}";
                OnTokenBalanceUpdated?.Invoke(_tokenBalanceFormatted);
            }
        }

        /// <summary>
        /// Manually trigger a token balance refresh.
        /// </summary>
        public async void RefreshTokenBalance()
        {
            await FetchTokenBalance();
        }

        /// <summary>
        /// Quick test method — distributes rewards using dummy placements.
        /// Useful for testing without a real match.
        /// The connected player gets 1st place rewards.
        /// </summary>
        public void TestDistributeRewards()
        {
            if (Web3Manager.Instance == null || !Web3Manager.Instance.IsWalletConnected)
            {
                Debug.LogError("[BattleRewardManager] Can't test — no wallet connected.");
                return;
            }

            var testPlayers = new List<PlayerRewardInfo>
            {
                new PlayerRewardInfo
                {
                    walletAddress = Web3Manager.Instance.WalletAddress,
                    placement = 1 // Give the test player 1st place
                }
            };

            Debug.Log("[BattleRewardManager] Running test reward distribution...");
            DistributeRewards(testPlayers);
        }

        // --- Internal Logic ---

        private void HandleWalletConnected(string address)
        {
            Debug.Log("[BattleRewardManager] Wallet connected, fetching token balance...");
            RefreshTokenBalance();
        }

        private void HandleWalletDisconnected()
        {
            _tokenBalanceFormatted = $"0 {tokenSymbol}";
            OnTokenBalanceUpdated?.Invoke(_tokenBalanceFormatted);
            _rewardWeb3 = null;
        }

        /// <summary>
        /// Creates (or reuses) the Nethereum Web3 instance from the reward wallet's private key.
        ///
        /// How Nethereum works:
        /// - Account = a wallet created from a private key (can sign transactions)
        /// - Web3 = a connection to the blockchain using that wallet
        /// - We tell it to connect to the Fuji RPC URL so it can send transactions
        /// </summary>
        private Nethereum.Web3.Web3 GetOrCreateRewardWeb3()
        {
            if (_rewardWeb3 != null) return _rewardWeb3;

            if (string.IsNullOrEmpty(rewardWalletPrivateKey))
            {
                Debug.LogError("[BattleRewardManager] No reward wallet private key set! " +
                    "Paste the private key in the Inspector. " +
                    "See the setup instructions in the script header.");
                return null;
            }

            try
            {
                // Create an Ethereum account from the private key
                // The 43113 is the chain ID for Avalanche Fuji testnet
                var account = new Account(rewardWalletPrivateKey, 43113);

                // Create a Web3 connection using this account + the Fuji RPC URL
                _rewardWeb3 = new Nethereum.Web3.Web3(account, rpcUrl);

                Debug.Log($"[BattleRewardManager] Reward wallet ready: {ShortenAddress(account.Address)}");

                return _rewardWeb3;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[BattleRewardManager] Failed to create reward wallet: {ex.Message}");
                return null;
            }
        }

        // --- Utility Methods ---

        /// <summary>
        /// Convert whole token amount to wei (smallest unit).
        /// Example: 100 tokens with 18 decimals = 100 * 10^18
        /// </summary>
        private BigInteger ToWei(float tokenAmount)
        {
            // Use decimal for precision, then convert to BigInteger
            decimal amount = (decimal)tokenAmount;
            decimal multiplier = (decimal)Math.Pow(10, tokenDecimals);
            return new BigInteger(amount * multiplier);
        }

        /// <summary>
        /// Convert wei (smallest unit) back to whole token amount.
        /// </summary>
        private float FromWei(BigInteger weiAmount)
        {
            // Divide by 10^decimals
            decimal divisor = (decimal)Math.Pow(10, tokenDecimals);
            decimal result = (decimal)weiAmount / divisor;
            return (float)result;
        }

        /// <summary>
        /// Shorten an address for logging: "0xABCD...1234"
        /// </summary>
        private string ShortenAddress(string address)
        {
            if (string.IsNullOrEmpty(address) || address.Length < 10)
                return address;
            return $"{address.Substring(0, 6)}...{address.Substring(address.Length - 4)}";
        }
    }

    // =========================================================================
    // Data Classes — used to pass reward info around
    // =========================================================================

    /// <summary>
    /// Input: describes one player who should receive a reward.
    /// Pass a list of these to DistributeRewards().
    /// </summary>
    [Serializable]
    public class PlayerRewardInfo
    {
        [Tooltip("The player's wallet address (0x...).")]
        public string walletAddress;

        [Tooltip("The player's final placement (1 = first place, 2 = second, etc.).")]
        public int placement;
    }

    /// <summary>
    /// Output: the result of minting tokens to one player.
    /// Returned in the OnRewardsDistributed event.
    /// </summary>
    [Serializable]
    public class RewardResult
    {
        public string walletAddress;
        public int placement;
        public float tokenAmount;
        public bool success;
        public string message;
    }

    /// <summary>
    /// Configurable reward tier — set these in the Inspector.
    /// Each entry maps a placement label to a token amount.
    /// </summary>
    [Serializable]
    public class PlacementReward
    {
        [Tooltip("Label for this placement (shown in UI). E.g. '1st Place', '2nd Place'.")]
        public string placementLabel = "1st Place";

        [Tooltip("How many tokens this placement earns. Whole numbers (e.g. 100 = 100 tokens).")]
        public float tokenAmount = 0;

        [Header("Bonus Rewards (non-token, display only for now)")]
        [Tooltip("XP earned for this placement.")]
        public int xpAmount = 0;

        [Tooltip("Energy earned for this placement.")]
        public int energyAmount = 0;

        [Tooltip("Gems earned for this placement.")]
        public int gemsAmount = 0;

        [Tooltip("Coins earned for this placement.")]
        public int coinsAmount = 0;
    }
}
