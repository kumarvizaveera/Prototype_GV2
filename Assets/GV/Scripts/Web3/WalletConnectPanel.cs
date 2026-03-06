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

        [Header("Character Selection")]
        [Tooltip("The character selection panel. Shown after ship is confirmed, before room lobby.")]
        [SerializeField] private GameObject characterSelectionPanel;

        [Header("Room Lobby")]
        [Tooltip("The room lobby panel (Create Room / Join Room). Shown after character is confirmed. Assign the same LobbyPanel that NetworkManager uses.")]
        [SerializeField] private GameObject roomLobbyPanel;

        [Header("Scene Loading")]
        [Tooltip("The gameplay scene to load when the player clicks Play.")]
        [SerializeField] private string gameplaySceneName = "MP_Mechanics_6";

        [Tooltip("If true, automatically loads the gameplay scene after wallet connects (skips Play button).")]
        [SerializeField] private bool autoLoadAfterConnect = false;

        private void Start()
        {
            // These events must survive gameObject.SetActive(false) — they fire
            // AFTER the wallet panel hides (ship selection, room connect).
            // Using Start/OnDestroy instead of OnEnable/OnDisable keeps them alive.
            if (ShipNFTManager.Instance != null)
                ShipNFTManager.Instance.OnShipSelected += HandleShipSelected;

            if (CharacterNFTManager.Instance != null)
                CharacterNFTManager.Instance.OnCharacterSelected += HandleCharacterSelected;

            if (Network.NetworkManager.Instance != null)
                Network.NetworkManager.Instance.OnConnectedEvent += HandleRoomConnected;
        }

        private void OnEnable()
        {
            // Listen for Web3Manager events (only needed while panel is visible)
            if (Web3Manager.Instance != null)
            {
                Web3Manager.Instance.OnWalletConnected += HandleWalletConnected;
                Web3Manager.Instance.OnError += HandleError;
            }

            // Wire up buttons (safe to call multiple times — we remove listeners first)
            if (connectEmailButton != null)
            {
                connectEmailButton.onClick.RemoveAllListeners();
                connectEmailButton.onClick.AddListener(OnConnectEmailClicked);
                connectEmailButton.gameObject.SetActive(false); // Hidden until player types in email field
            }

            // Show "Send OTP" button as soon as the player starts typing an email
            if (emailInputField != null)
            {
                emailInputField.onValueChanged.RemoveAllListeners();
                emailInputField.onValueChanged.AddListener(OnEmailInputChanged);
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
                playButton.gameObject.SetActive(false); // Hidden until room connects
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
        }

        private void OnDestroy()
        {
            if (ShipNFTManager.Instance != null)
                ShipNFTManager.Instance.OnShipSelected -= HandleShipSelected;

            if (CharacterNFTManager.Instance != null)
                CharacterNFTManager.Instance.OnCharacterSelected -= HandleCharacterSelected;

            if (Network.NetworkManager.Instance != null)
                Network.NetworkManager.Instance.OnConnectedEvent -= HandleRoomConnected;
        }

        // --- Input Handlers ---

        private void OnEmailInputChanged(string text)
        {
            // Show "Send OTP" button only when there's text in the email field
            if (connectEmailButton != null)
                connectEmailButton.gameObject.SetActive(!string.IsNullOrEmpty(text));
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
            // Use NetworkManager's gameplay scene — single source of truth
            if (Network.NetworkManager.Instance != null)
            {
                Network.NetworkManager.Instance.LoadGameplay();
            }
            else
            {
                LoadGameplayScene();
            }
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

            // Ensure the room lobby panel is hidden while picking ships
            if (roomLobbyPanel != null)
            {
                roomLobbyPanel.SetActive(false);
            }

            // Fully deactivate the wallet connect panel — wallet is done, ship selection takes over.
            // Using gameObject.SetActive(false) instead of Show(false) so the entire panel
            // is removed from the UI, preventing any leftover Image from blocking raycasts
            // on panels that appear later (lobby, connected panel, etc.)
            gameObject.SetActive(false);
        }

        /// <summary>
        /// Called when the player confirms a ship in ShipSelectionUI.
        /// Now we show the Character Selection screen (not room lobby yet).
        /// </summary>
        private void HandleShipSelected(ShipDefinition ship)
        {
            Debug.Log($"[WalletConnectPanel] HandleShipSelected called — ship: {ship.displayName}");
            SetStatus($"Ship: {ship.displayName} | Choose your character.");

            // Hide ship selection
            if (showAfterConnect != null)
            {
                showAfterConnect.SetActive(false);
            }

            // Show character selection panel (NOT room lobby yet)
            if (characterSelectionPanel != null)
            {
                characterSelectionPanel.SetActive(true);
                Debug.Log("[WalletConnectPanel] Showing character selection panel.");
            }
            else
            {
                // No character panel assigned — skip straight to lobby
                Debug.LogWarning("[WalletConnectPanel] characterSelectionPanel is NOT assigned in Inspector! " +
                    "Skipping character selection, going straight to room lobby.");
                ShowRoomLobby();
            }
        }

        /// <summary>
        /// Called when the player confirms a character in CharacterSelectionUI.
        /// Now we can show the Room Lobby.
        /// </summary>
        private void HandleCharacterSelected(CharacterDefinition character)
        {
            Debug.Log($"[WalletConnectPanel] Character selected: {character.displayName}");
            SetStatus($"Character: {character.displayName} | Proceeding to Room Lobby.");

            // Hide character selection panel
            if (characterSelectionPanel != null)
            {
                characterSelectionPanel.SetActive(false);
            }

            ShowRoomLobby();
        }

        /// <summary>
        /// Shows the room lobby panel via NetworkManager.
        /// Extracted to avoid duplication between HandleShipSelected fallback and HandleCharacterSelected.
        /// </summary>
        private void ShowRoomLobby()
        {
            Debug.Log($"[WalletConnectPanel] ShowRoomLobby called. roomLobbyPanel={(roomLobbyPanel != null ? "assigned" : "NULL")}, " +
                $"NetworkManager={(Network.NetworkManager.Instance != null ? "exists" : "NULL")}");

            if (roomLobbyPanel != null)
            {
                if (Network.NetworkManager.Instance != null)
                {
                    Network.NetworkManager.Instance.ShowLobbyUI();
                }
                else
                {
                    roomLobbyPanel.SetActive(true);
                }
                Debug.Log("[WalletConnectPanel] Room lobby panel is now active.");
            }
            else
            {
                Debug.LogError("[WalletConnectPanel] roomLobbyPanel is NULL! Cannot show room lobby. " +
                    "Assign the LobbyPanel in the Inspector.");
            }
        }

        private void HandleRoomConnected(Fusion.NetworkRunner runner)
        {
            Debug.Log("[WalletConnectPanel] Room connection established. Showing Play button.");
            if (playButton != null)
            {
                playButton.gameObject.SetActive(true);
            }

            if (autoLoadAfterConnect)
            {
                // Go straight to gameplay (skip play button — for quick testing)
                LoadGameplayScene();
            }
        }

        private void HandleError(string errorMessage)
        {
            SetConnectingState(false);
            SetStatus(errorMessage);
        }

        // --- UI Helpers ---

        /// <summary>
        /// Shows/hides the wallet connection UI elements, keeping the main panel active so children remain visible.
        /// </summary>
        public void Show(bool visible = true)
        {
            if (emailInputField != null) emailInputField.gameObject.SetActive(visible);
            if (connectEmailButton != null) connectEmailButton.gameObject.SetActive(visible);
            if (connectGoogleButton != null) connectGoogleButton.gameObject.SetActive(visible);
            if (connectDiscordButton != null) connectDiscordButton.gameObject.SetActive(visible);
            if (connectGuestButton != null) connectGuestButton.gameObject.SetActive(visible);
            if (connectExternalWalletButton != null) connectExternalWalletButton.gameObject.SetActive(visible);
            if (titleText != null) titleText.gameObject.SetActive(visible);

            // Disable raycast blocking on the panel's background Image so it doesn't
            // eat clicks meant for UI behind it (like the room lobby buttons)
            if (panel != null)
            {
                var panelImage = panel.GetComponent<UnityEngine.UI.Image>();
                if (panelImage != null) panelImage.raycastTarget = visible;
            }

            // Note: We leave statusText active so it can show "Connected" or "Ship Selected"
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
