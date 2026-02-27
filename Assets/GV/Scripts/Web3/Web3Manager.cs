using System;
using System.Threading.Tasks;
using UnityEngine;
using Thirdweb;
using Thirdweb.Unity;

namespace GV.Web3
{
    /// <summary>
    /// Central manager for all blockchain/Web3 functionality.
    ///
    /// What this does:
    /// - Connects the player's wallet (email or social login — no MetaMask needed)
    /// - Keeps track of the connected wallet
    /// - Fetches and caches the player's AVAX balance
    /// - Fires events so UI panels can react to wallet state changes
    ///
    /// This is a singleton — only one exists, and it survives scene changes.
    /// It must initialize BEFORE NetworkManager tries to start a match.
    /// </summary>
    public class Web3Manager : MonoBehaviour
    {
        public static Web3Manager Instance { get; private set; }

        // --- Configuration ---
        [Header("Blockchain Settings")]
        [Tooltip("Avalanche Fuji Testnet. Do NOT change to mainnet.")]
        [SerializeField] private ulong chainId = 43113;

        [Header("Wallet Settings")]
        [Tooltip("How the player logs in. Email = type an email and get a code. Social = Google/Discord/etc.")]
        [SerializeField] private AuthProvider defaultAuthProvider = AuthProvider.Google;

        // --- State ---
        // The connected wallet (null if not connected yet)
        private IThirdwebWallet _wallet;

        // Cached wallet info
        private string _walletAddress = "";
        private string _balanceFormatted = "0.00";
        private bool _isConnecting = false;

        // --- Public Properties (read-only from outside) ---
        /// <summary>Is a wallet currently connected?</summary>
        public bool IsWalletConnected => _wallet != null;

        /// <summary>The connected wallet address (empty string if not connected).</summary>
        public string WalletAddress => _walletAddress;

        /// <summary>The AVAX balance formatted for display (e.g. "1.2345 AVAX").</summary>
        public string BalanceFormatted => _balanceFormatted;

        /// <summary>Is the wallet currently in the process of connecting?</summary>
        public bool IsConnecting => _isConnecting;

        /// <summary>The chain ID we're targeting (43113 = Fuji testnet).</summary>
        public ulong ChainId => chainId;

        // --- Events (UI panels listen to these) ---
        /// <summary>Fired when a wallet successfully connects. Passes the wallet address.</summary>
        public event Action<string> OnWalletConnected;

        /// <summary>Fired when the wallet disconnects.</summary>
        public event Action OnWalletDisconnected;

        /// <summary>Fired when the balance updates. Passes the formatted balance string.</summary>
        public event Action<string> OnBalanceUpdated;

        /// <summary>Fired when something goes wrong. Passes the error message.</summary>
        public event Action<string> OnError;

        // --- Singleton Setup ---
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[Web3Manager] Duplicate detected, destroying this one.");
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            Debug.Log("[Web3Manager] Initialized. Chain: Avalanche Fuji (43113)");
        }

        // --- Public Methods (called by UI buttons) ---

        /// <summary>
        /// Connect a wallet using email login.
        /// The player types their email, gets a verification code, and they're in.
        /// No crypto knowledge needed.
        /// </summary>
        /// <param name="email">The player's email address.</param>
        public async void ConnectWithEmail(string email)
        {
            if (string.IsNullOrEmpty(email))
            {
                OnError?.Invoke("Please enter an email address.");
                return;
            }

            Debug.Log($"[Web3Manager] Connecting with email: {email}");
            await ConnectWallet(new InAppWalletOptions(email: email));
        }

        /// <summary>
        /// Connect a wallet using social login (Google, Discord, etc).
        /// Opens a browser popup where the player signs in with their existing account.
        /// </summary>
        /// <param name="provider">Which social platform to use (Google, Discord, etc).</param>
        public async void ConnectWithSocial(AuthProvider provider = AuthProvider.Google)
        {
            Debug.Log($"[Web3Manager] Connecting with social: {provider}");
            await ConnectWallet(new InAppWalletOptions(authprovider: provider));
        }

        /// <summary>
        /// Connect using the default auth provider set in the Inspector.
        /// </summary>
        public async void ConnectWithDefault()
        {
            Debug.Log($"[Web3Manager] Connecting with default provider: {defaultAuthProvider}");
            await ConnectWallet(new InAppWalletOptions(authprovider: defaultAuthProvider));
        }

        /// <summary>
        /// Connect as a guest (temporary wallet, good for testing).
        /// The wallet goes away when the player closes the game.
        /// </summary>
        public async void ConnectAsGuest()
        {
            Debug.Log("[Web3Manager] Connecting as guest...");
            await ConnectWallet(new InAppWalletOptions(authprovider: AuthProvider.Guest));
        }

        /// <summary>
        /// Disconnect the current wallet.
        /// </summary>
        public void Disconnect()
        {
            if (_wallet == null) return;

            Debug.Log("[Web3Manager] Disconnecting wallet...");
            _wallet = null;
            _walletAddress = "";
            _balanceFormatted = "0.00";
            _isConnecting = false;

            OnWalletDisconnected?.Invoke();
            Debug.Log("[Web3Manager] Wallet disconnected.");
        }

        /// <summary>
        /// Refresh the balance (call this periodically or after transactions).
        /// </summary>
        public async void RefreshBalance()
        {
            if (_wallet == null) return;
            await FetchBalance();
        }

        // --- Internal Logic ---

        /// <summary>
        /// The core wallet connection logic. All the public Connect methods funnel into this.
        ///
        /// How it works:
        /// 1. Creates a WalletOptions object telling Thirdweb HOW to connect (email, Google, etc)
        /// 2. Calls ThirdwebManager.Instance.ConnectWallet() which handles the actual connection
        ///    (this opens a popup for social login, or sends an email code, etc)
        /// 3. Once connected, grabs the wallet address
        /// 4. Fetches the AVAX balance
        /// 5. Fires OnWalletConnected so the UI can update
        /// </summary>
        private async Task ConnectWallet(InAppWalletOptions inAppOptions)
        {
            if (_isConnecting)
            {
                Debug.LogWarning("[Web3Manager] Already connecting, please wait...");
                return;
            }

            _isConnecting = true;

            try
            {
                // Make sure ThirdwebManager exists (it should be in the Bootstrap scene)
                if (ThirdwebManager.Instance == null)
                {
                    throw new Exception(
                        "ThirdwebManager not found! Make sure the ThirdwebManager prefab is in your Bootstrap scene " +
                        "and has a valid ClientId set in the Inspector."
                    );
                }

                // Create the connection options
                // InAppWallet = Thirdweb's managed wallet (email/social login, no browser extension needed)
                var walletOptions = new WalletOptions(
                    provider: WalletProvider.InAppWallet,
                    chainId: chainId,
                    inAppWalletOptions: inAppOptions
                );

                // This is the actual connection call — it may show a popup or send a code
                _wallet = await ThirdwebManager.Instance.ConnectWallet(walletOptions);

                // Get the wallet address (a long hex string like 0xABC123...)
                _walletAddress = await _wallet.GetAddress();
                Debug.Log($"[Web3Manager] Wallet connected: {_walletAddress}");

                // Fetch the balance right away
                await FetchBalance();

                // Tell everyone the wallet is ready
                OnWalletConnected?.Invoke(_walletAddress);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Web3Manager] Connection failed: {ex.Message}");
                OnError?.Invoke($"Wallet connection failed: {ex.Message}");
                _wallet = null;
                _walletAddress = "";
            }
            finally
            {
                _isConnecting = false;
            }
        }

        /// <summary>
        /// Fetches the AVAX balance from the blockchain.
        /// This is a "read" operation — it doesn't cost anything or change anything.
        /// </summary>
        private async Task FetchBalance()
        {
            if (_wallet == null) return;

            try
            {
                // GetBalance returns the balance in "wei" (the smallest unit of AVAX)
                // Utils.ToEth converts it to a readable number like "1.2345"
                var balanceWei = await _wallet.GetBalance(chainId);
                var chainMeta = await Utils.GetChainMetadata(ThirdwebManager.Instance.Client, chainId);
                string symbol = chainMeta?.NativeCurrency?.Symbol ?? "AVAX";

                // ToEth(balance, decimals, addCommas) — converts from wei to human-readable
                _balanceFormatted = $"{Utils.ToEth(balanceWei.ToString(), 4, false)} {symbol}";

                Debug.Log($"[Web3Manager] Balance: {_balanceFormatted}");
                OnBalanceUpdated?.Invoke(_balanceFormatted);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Web3Manager] Failed to fetch balance: {ex.Message}");
                _balanceFormatted = "? AVAX";
                OnBalanceUpdated?.Invoke(_balanceFormatted);
            }
        }

        // --- Utility ---

        /// <summary>
        /// Returns a shortened version of the wallet address for display.
        /// Example: "0xABCD...1234" instead of the full 42-character address.
        /// </summary>
        public string GetShortAddress()
        {
            if (string.IsNullOrEmpty(_walletAddress) || _walletAddress.Length < 10)
                return _walletAddress;

            return $"{_walletAddress.Substring(0, 6)}...{_walletAddress.Substring(_walletAddress.Length - 4)}";
        }
    }
}
