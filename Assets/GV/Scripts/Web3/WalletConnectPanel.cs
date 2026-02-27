using UnityEngine;
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

        [Header("Post-Connect")]
        [Tooltip("If set, this GameObject will be activated after wallet connects (e.g. a 'Play' button or lobby panel).")]
        [SerializeField] private GameObject showAfterConnect;

        private void OnEnable()
        {
            // Listen for Web3Manager events
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

        // --- Event Handlers ---

        private void HandleWalletConnected(string address)
        {
            SetConnectingState(false);
            SetStatus($"Connected: {Web3Manager.Instance.GetShortAddress()}");
            Debug.Log($"[WalletConnectPanel] Wallet connected: {address}");

            // Hide this panel and show the next step (e.g. Play button or lobby)
            if (panel != null)
            {
                panel.SetActive(false);
            }

            if (showAfterConnect != null)
            {
                showAfterConnect.SetActive(true);
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
            if (emailInputField != null) emailInputField.interactable = !connecting;
        }
    }
}
