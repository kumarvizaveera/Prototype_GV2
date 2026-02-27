using UnityEngine;
using TMPro;

namespace GV.Web3
{
    /// <summary>
    /// Displays the connected wallet address and AVAX balance on screen.
    ///
    /// What this does:
    /// - Shows a small overlay with the wallet address (shortened) and balance
    /// - Automatically updates when the balance changes
    /// - Hides itself when no wallet is connected
    /// - Periodically refreshes the balance (every 30 seconds by default)
    ///
    /// How to set it up in Unity:
    /// 1. Create a Canvas (or use existing HUD canvas)
    /// 2. Add a Panel in a corner (e.g. top-right) with two TMP_Text children
    /// 3. Drag the texts into addressText and balanceText slots
    /// 4. Attach this script to the panel
    /// </summary>
    public class WalletHUD : MonoBehaviour
    {
        [Header("UI References")]
        [Tooltip("The root HUD object — shown when wallet is connected, hidden otherwise.")]
        [SerializeField] private GameObject hudPanel;

        [Tooltip("Shows the wallet address (shortened like 0xABC...1234).")]
        [SerializeField] private TMP_Text addressText;

        [Tooltip("Shows the AVAX balance.")]
        [SerializeField] private TMP_Text balanceText;

        [Header("Settings")]
        [Tooltip("How often to refresh the balance from the blockchain (in seconds).")]
        [SerializeField] private float refreshInterval = 30f;

        private float _lastRefreshTime;

        private void OnEnable()
        {
            if (Web3Manager.Instance != null)
            {
                Web3Manager.Instance.OnWalletConnected += HandleWalletConnected;
                Web3Manager.Instance.OnWalletDisconnected += HandleWalletDisconnected;
                Web3Manager.Instance.OnBalanceUpdated += HandleBalanceUpdated;

                // If wallet is already connected (e.g. we just loaded into this scene),
                // update the HUD immediately
                if (Web3Manager.Instance.IsWalletConnected)
                {
                    HandleWalletConnected(Web3Manager.Instance.WalletAddress);
                }
                else
                {
                    if (hudPanel != null) hudPanel.SetActive(false);
                }
            }
            else
            {
                // Web3Manager not ready yet — hide for now
                if (hudPanel != null) hudPanel.SetActive(false);
            }
        }

        private void OnDisable()
        {
            if (Web3Manager.Instance != null)
            {
                Web3Manager.Instance.OnWalletConnected -= HandleWalletConnected;
                Web3Manager.Instance.OnWalletDisconnected -= HandleWalletDisconnected;
                Web3Manager.Instance.OnBalanceUpdated -= HandleBalanceUpdated;
            }
        }

        private void Update()
        {
            // Periodically refresh the balance so it stays up to date
            if (Web3Manager.Instance != null &&
                Web3Manager.Instance.IsWalletConnected &&
                Time.time - _lastRefreshTime > refreshInterval)
            {
                _lastRefreshTime = Time.time;
                Web3Manager.Instance.RefreshBalance();
            }
        }

        // --- Event Handlers ---

        private void HandleWalletConnected(string address)
        {
            if (hudPanel != null) hudPanel.SetActive(true);

            if (addressText != null)
            {
                addressText.text = Web3Manager.Instance.GetShortAddress();
            }

            if (balanceText != null)
            {
                balanceText.text = Web3Manager.Instance.BalanceFormatted;
            }

            _lastRefreshTime = Time.time;
        }

        private void HandleWalletDisconnected()
        {
            if (hudPanel != null) hudPanel.SetActive(false);

            if (addressText != null) addressText.text = "";
            if (balanceText != null) balanceText.text = "";
        }

        private void HandleBalanceUpdated(string formattedBalance)
        {
            if (balanceText != null)
            {
                balanceText.text = formattedBalance;
            }
        }
    }
}
