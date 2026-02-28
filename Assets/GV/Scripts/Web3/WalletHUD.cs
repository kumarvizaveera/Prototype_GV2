using UnityEngine;
using TMPro;

namespace GV.Web3
{
    /// <summary>
    /// Displays the connected wallet address, AVAX balance, and token balance on screen.
    ///
    /// What this does:
    /// - Shows a small overlay with the wallet address (shortened) and balances
    /// - Automatically updates when balances change
    /// - Hides itself when no wallet is connected
    /// - Periodically refreshes balances (every 30 seconds by default)
    /// - Auto-creates a token balance text if one isn't assigned in Inspector
    ///
    /// How to set it up in Unity:
    /// 1. Create a Canvas (or use existing HUD canvas)
    /// 2. Add a Panel in a corner (e.g. top-right) with two TMP_Text children
    /// 3. Drag the texts into addressText and balanceText slots
    /// 4. Attach this script to the panel
    /// 5. Token balance text is auto-created below the AVAX balance if not assigned
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

        [Tooltip("Shows the game token balance (e.g. '150 PRANA'). Auto-created if empty.")]
        [SerializeField] private TMP_Text tokenBalanceText;

        [Header("Settings")]
        [Tooltip("How often to refresh the balance from the blockchain (in seconds).")]
        [SerializeField] private float refreshInterval = 30f;

        private float _lastRefreshTime;
        private bool _subscribedToRewards = false;

        private void Start()
        {
            // Auto-create token balance text if not assigned and we have a balance text to copy from
            if (tokenBalanceText == null && balanceText != null)
            {
                CreateTokenBalanceText();
            }
        }

        private void OnEnable()
        {
            if (Web3Manager.Instance != null)
            {
                Web3Manager.Instance.OnWalletConnected += HandleWalletConnected;
                Web3Manager.Instance.OnWalletDisconnected += HandleWalletDisconnected;
                Web3Manager.Instance.OnBalanceUpdated += HandleBalanceUpdated;

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
                if (hudPanel != null) hudPanel.SetActive(false);
            }

            TrySubscribeToRewards();
        }

        private void OnDisable()
        {
            if (Web3Manager.Instance != null)
            {
                Web3Manager.Instance.OnWalletConnected -= HandleWalletConnected;
                Web3Manager.Instance.OnWalletDisconnected -= HandleWalletDisconnected;
                Web3Manager.Instance.OnBalanceUpdated -= HandleBalanceUpdated;
            }

            if (_subscribedToRewards && BattleRewardManager.Instance != null)
            {
                BattleRewardManager.Instance.OnTokenBalanceUpdated -= HandleTokenBalanceUpdated;
                _subscribedToRewards = false;
            }
        }

        private void Update()
        {
            // Keep trying to subscribe until BattleRewardManager is available
            if (!_subscribedToRewards)
            {
                TrySubscribeToRewards();
            }

            // Periodically refresh both balances so they stay up to date
            if (Web3Manager.Instance != null &&
                Web3Manager.Instance.IsWalletConnected &&
                Time.time - _lastRefreshTime > refreshInterval)
            {
                _lastRefreshTime = Time.time;
                Web3Manager.Instance.RefreshBalance();

                if (BattleRewardManager.Instance != null)
                {
                    BattleRewardManager.Instance.RefreshTokenBalance();
                }
            }
        }

        // --- Setup ---

        private void TrySubscribeToRewards()
        {
            if (_subscribedToRewards) return;

            if (BattleRewardManager.Instance != null)
            {
                BattleRewardManager.Instance.OnTokenBalanceUpdated += HandleTokenBalanceUpdated;
                _subscribedToRewards = true;

                // Grab current balance immediately
                if (tokenBalanceText != null)
                {
                    tokenBalanceText.text = BattleRewardManager.Instance.TokenBalanceFormatted;
                }
            }
        }

        /// <summary>
        /// Auto-creates a TMP_Text for token balance below the AVAX balance text.
        /// Copies the style from balanceText so it looks consistent.
        /// </summary>
        private void CreateTokenBalanceText()
        {
            var go = new GameObject("TokenBalanceText");
            go.transform.SetParent(balanceText.transform.parent, false);

            tokenBalanceText = go.AddComponent<TextMeshProUGUI>();
            tokenBalanceText.fontSize = balanceText.fontSize;
            tokenBalanceText.color = new Color(0.3f, 1f, 0.5f); // green tint to stand out
            tokenBalanceText.alignment = balanceText.alignment;
            tokenBalanceText.enableWordWrapping = false;
            tokenBalanceText.text = "";

            // Position it below the AVAX balance text
            var sourceRect = balanceText.GetComponent<RectTransform>();
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = sourceRect.anchorMin;
            rect.anchorMax = sourceRect.anchorMax;
            rect.sizeDelta = sourceRect.sizeDelta;
            rect.anchoredPosition = sourceRect.anchoredPosition + new Vector2(0, -sourceRect.sizeDelta.y - 5);

            Debug.Log("[WalletHUD] Auto-created token balance text.");
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

            // Show token balance
            if (tokenBalanceText != null && BattleRewardManager.Instance != null)
            {
                tokenBalanceText.text = BattleRewardManager.Instance.TokenBalanceFormatted;
            }

            _lastRefreshTime = Time.time;
        }

        private void HandleWalletDisconnected()
        {
            if (hudPanel != null) hudPanel.SetActive(false);

            if (addressText != null) addressText.text = "";
            if (balanceText != null) balanceText.text = "";
            if (tokenBalanceText != null) tokenBalanceText.text = "";
        }

        private void HandleBalanceUpdated(string formattedBalance)
        {
            if (balanceText != null)
            {
                balanceText.text = formattedBalance;
            }
        }

        private void HandleTokenBalanceUpdated(string formattedBalance)
        {
            if (tokenBalanceText != null)
            {
                tokenBalanceText.text = formattedBalance;
            }
        }
    }
}
