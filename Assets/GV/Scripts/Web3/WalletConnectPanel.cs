using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using Thirdweb;

namespace GV.Web3
{
    /// <summary>
    /// The wallet connection UI panel.
    ///
    /// What this does:
    /// - Shows a panel with options to connect a wallet (email, Google, Discord, or guest)
    /// - Handles the email input field and connect button
    /// - Shows loading state while connecting
    /// - Shows error messages if something goes wrong
    /// - Hides itself once the wallet is connected
    ///
    /// How to set it up in Unity:
    /// 1. Create a Canvas → Panel (this is the "panel" object)
    /// 2. Add child objects: Title text, Email input field, Connect buttons, Status text
    /// 3. Drag them into the Inspector slots on this script
    /// 4. Attach this script to the panel GameObject
    ///
    /// This works with the SCK Menu system — you can hook it into Menu.onOpened/onClosed events.
    /// </summary>
    public class WalletConnectPanel : MonoBehaviour
    {
        [Header("UI References")]
        [Tooltip("The root panel object — gets shown/hidden.")]
        [SerializeField] private GameObject panel;

        [Tooltip("Where the player types their email.")]
        [SerializeField] private TMP_InputField emailInputField;

        [Tooltip("Shows status messages like 'Connecting...' or errors.")]
        [SerializeField] private TMP_Text statusText;

        [Tooltip("The title text at the top of the panel.")]
        [SerializeField] private TMP_Text titleText;

        [Header("Buttons")]
        [Tooltip("Connects using the typed email.")]
        [SerializeField] private Button connectEmailButton;

        [Tooltip("Connects using Google account.")]
        [SerializeField] private Button connectGoogleButton;

        [Tooltip("Connects using Discord account.")]
        [SerializeField] private Button connectDiscordButton;

        [Tooltip("Connects as a guest (temporary, good for testing).")]
        [SerializeField] private Button connectGuestButton;

        [Tooltip("Connects an external wallet like MetaMask, Coinbase Wallet, etc.")]
        [SerializeField] private Button connectExternalWalletButton;

        [Header("Post-Connect")]
        [Tooltip("If set, this GameObject will be activated after wallet connects (e.g. ship selection or room lobby panel).")]
        [SerializeField] private GameObject showAfterConnect;

        [Tooltip("The button that loads the gameplay scene after wallet is connected.")]
        [SerializeField] private Button playButton;

        [Header("Room Lobby")]
        [Tooltip("The room lobby panel (Create Room / Join Room). Shown after wallet connects. Assign the same LobbyPanel that NetworkManager uses.")]
        [SerializeField] private GameObject roomLobbyPanel;

        [Header("Scene Loading")]
        [Tooltip("The gameplay scene to load when the player clicks Play.")]
        [SerializeField] private string gameplaySceneName = "MP_Mechanics_6";

        [Tooltip("If true, automatically loads the gameplay scene after wallet connects (skips Play button).")]
        [SerializeField] private bool autoLoadAfterConnect = false;

        private void OnEnable()
        {
            // Listen for Web3Manager events
            if (Web3Manager.Instance != null)
            {
                Web3Manager.Instance.OnWalletConnected += HandleWalletConnected;
                Web3Manager.Instance.OnError += HandleError;
            }

            // Listen for ship selection — Play button only appears after a ship is confirmed
            if (ShipNFTManager.Instance != null)
            {
                ShipNFTManager.Instance.OnShipSelected += HandleShipSelected;
            }

            // Wire up buttons (safe to call multiple times — we remove listeners first)
            if (connectEmailButton != null)
            {
                connectEmailButton.onClick.RemoveAllListeners();
                connectEmailButton.onClick.AddListener(OnConnectEmailClicked);
            }
            if (connectGoogleButton != null)
            {
                connectGoogleButton.onClick.RemoveAllListeners();
                connectGoogleButton.onClick.AddListener(OnConnectGoogleClicked);
            }
            if (connectDiscordButton != null)
            {
                connectDiscordButton.onClick.RemoveAllListeners();
                connectDiscordButton.onClick.AddListener(OnConnectDiscordClicked);
            }
            if (connectGuestButton != null)
            {
                connectGuestButton.onClick.RemoveAllListeners();
                connectGuestButton.onClick.AddListener(OnConnectGuestClicked);
            }
            if (connectExternalWalletButton != null)
            {
                connectExternalWalletButton.onClick.RemoveAllListeners();
                connectExternalWalletButton.onClick.AddListener(OnConnectExternalWalletClicked);
            }

            if (playButton != null)
            {
                playButton.onClick.RemoveAllListeners();
                playButton.onClick.AddListener(OnPlayClicked);
                playButton.gameObject.SetActive(false); // Hidden until wallet connects
            }

            SetStatus("");
        }

        private void OnDisable()
        {
            if (Web3Manager.Instance != null)
            {
                Web3Manager.Instance.OnWalletConnected -= HandleWalletConnected;
                Web3Manager.Instance.OnError -= HandleError;
            }

            if (ShipNFTManager.Instance != null)
            {
                ShipNFTManager.Instance.OnShipSelected -= HandleShipSelected;
            }
        }

        // --- Button Handlers ---

        private void OnConnectEmailClicked()
        {
            if (emailInputField == null || string.IsNullOrEmpty(emailInputField.text))
            {
                SetStatus("Please enter your email address.");
                return;
            }

            SetConnectingState(true);
            SetStatus("Sending verification code...");
            Web3Manager.Instance.ConnectWithEmail(emailInputField.text.Trim());
        }

        private void OnConnectGoogleClicked()
        {
            SetConnectingState(true);
            SetStatus("Opening Google sign-in...");
            Web3Manager.Instance.ConnectWithSocial(AuthProvider.Google);
        }

        private void OnConnectDiscordClicked()
        {
            SetConnectingState(true);
            SetStatus("Opening Discord sign-in...");
            Web3Manager.Instance.ConnectWithSocial(AuthProvider.Discord);
        }

        private void OnConnectGuestClicked()
        {
            SetConnectingState(true);
            SetStatus("Creating guest wallet...");
            Web3Manager.Instance.ConnectAsGuest();
        }

        private void OnConnectExternalWalletClicked()
        {
            SetConnectingState(true);
            SetStatus("Opening wallet connection...");
            Web3Manager.Instance.ConnectWithExternalWallet();
        }

        private void OnPlayClicked()
        {
            LoadGameplayScene();
        }

        private void LoadGameplayScene()
        {
            if (!string.IsNullOrEmpty(gameplaySceneName))
            {
                Debug.Log($"[WalletConnectPanel] Loading gameplay scene: {gameplaySceneName}");
                SceneManager.LoadScene(gameplaySceneName);
            }
            else
            {
                Debug.LogError("[WalletConnectPanel] No gameplay scene name set!");
            }
        }

        // --- Event Handlers ---

        private void HandleWalletConnected(string address)
        {
            SetConnectingState(false);
            string shortAddr = Web3Manager.Instance.GetShortAddress();
            string balance = Web3Manager.Instance.BalanceFormatted;
            SetStatus($"Connected: {shortAddr}\nBalance: {balance}");
            Debug.Log($"[WalletConnectPanel] Wallet connected: {address}");

            // Show the ship selection panel (ShipNFTManager auto-fetches NFTs on wallet connect)
            if (showAfterConnect != null)
            {
                showAfterConnect.SetActive(true);
            }

            // Show the room lobby panel (Create Room / Join Room)
            if (roomLobbyPanel != null)
            {
                roomLobbyPanel.SetActive(true);
                Debug.Log("[WalletConnectPanel] Showing room lobby panel");
            }

            // Hide the wallet connect panel — wallet is done, room UI takes over
            Show(false);

            if (autoLoadAfterConnect)
            {
                // Go straight to gameplay (skip ship selection — for quick testing)
                LoadGameplayScene();
            }
            // Play button stays hidden — it appears after the player picks a ship
            // (handled by HandleShipSelected below)
        }

        /// <summary>
        /// Called when the player confirms a ship in ShipSelectionUI.
        /// Now we can show the Play button.
        /// </summary>
        private void HandleShipSelected(ShipDefinition ship)
        {
            Debug.Log($"[WalletConnectPanel] Ship selected: {ship.displayName}");
            SetStatus($"Ship: {ship.displayName} | Ready to play!");

            if (playButton != null)
            {
                playButton.gameObject.SetActive(true);
            }
        }

        private void HandleError(string errorMessage)
        {
            SetConnectingState(false);
            SetStatus(errorMessage);
        }

        // --- UI Helpers ---

        /// <summary>
        /// Shows/hides the panel.
        /// </summary>
        public void Show(bool visible = true)
        {
            if (panel != null)
            {
                panel.SetActive(visible);
            }
        }

        private void SetStatus(string message)
        {
            if (statusText != null)
            {
                statusText.text = message;
            }
        }

        /// <summary>
        /// Disables/enables buttons while connecting so the player can't spam-click.
        /// </summary>
        private void SetConnectingState(bool connecting)
        {
            if (connectEmailButton != null) connectEmailButton.interactable = !connecting;
            if (connectGoogleButton != null) connectGoogleButton.interactable = !connecting;
            if (connectDiscordButton != null) connectDiscordButton.interactable = !connecting;
            if (connectGuestButton != null) connectGuestButton.interactable = !connecting;
            if (connectExternalWalletButton != null) connectExternalWalletButton.interactable = !connecting;
            if (emailInputField != null) emailInputField.interactable = !connecting;
        }
    }
}
