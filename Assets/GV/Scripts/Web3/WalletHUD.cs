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

            // Enforce vertical stacking AFTER token text is created so all 3 are positioned
            EnforceTextLayout();
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
        /// Forces all 3 texts (address, AVAX, PRANA) into a clean vertical stack.
        /// Disables word wrapping, widens text fields to prevent line breaks,
        /// and positions each line with consistent spacing below the previous one.
        /// </summary>
        private void EnforceTextLayout()
        {
            if (addressText == null || balanceText == null) return;

            // Use the largest font size among the texts for spacing
            float fontSize = Mathf.Max(addressText.fontSize, balanceText.fontSize);
            float lineSpacing = fontSize * 1.5f; // generous spacing between lines
            float minWidth = 300f; // wide enough for "0x6dd8...a77c" and "0.0000 AVAX"

            // Fix addressText: disable wrapping, ensure wide enough
            FixTextField(addressText, minWidth);

            // Fix balanceText: disable wrapping, position below address
            FixTextField(balanceText, minWidth);
            var addrRect = addressText.GetComponent<RectTransform>();
            var balRect = balanceText.GetComponent<RectTransform>();
            balRect.anchoredPosition = new Vector2(
                addrRect.anchoredPosition.x,
                addrRect.anchoredPosition.y - lineSpacing
            );

            // Fix tokenBalanceText: disable wrapping, position below balance
            if (tokenBalanceText != null)
            {
                FixTextField(tokenBalanceText, minWidth);
                var tokRect = tokenBalanceText.GetComponent<RectTransform>();
                tokRect.anchoredPosition = new Vector2(
                    addrRect.anchoredPosition.x,
                    addrRect.anchoredPosition.y - lineSpacing * 2
                );
            }

            Debug.Log($"[WalletHUD] EnforceTextLayout: fontSize={fontSize}, lineSpacing={lineSpacing}, " +
                      $"addrY={addrRect.anchoredPosition.y}, balY={balRect.anchoredPosition.y}" +
                      (tokenBalanceText != null ? $", tokY={tokenBalanceText.GetComponent<RectTransform>().anchoredPosition.y}" : ""));
        }

        /// <summary>
        /// Ensures a TMP_Text won't wrap and is wide enough to display wallet info on one line.
        /// </summary>
        private void FixTextField(TMP_Text text, float minWidth)
        {
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Overflow;
            var rect = text.GetComponent<RectTransform>();
            if (rect.sizeDelta.x < minWidth)
            {
                rect.sizeDelta = new Vector2(minWidth, rect.sizeDelta.y);
            }
        }

        /// <summary>
        /// Auto-creates a TMP_Text for token balance.
        /// Styled to match balanceText. Positioned by EnforceTextLayout() afterward.
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
            tokenBalanceText.overflowMode = TextOverflowModes.Overflow;
            tokenBalanceText.text = "";

            // Copy anchoring from balanceText — EnforceTextLayout() will set final position
            var sourceRect = balanceText.GetComponent<RectTransform>();
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = sourceRect.anchorMin;
            rect.anchorMax = sourceRect.anchorMax;
            rect.sizeDelta = new Vector2(300f, sourceRect.sizeDelta.y);

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
