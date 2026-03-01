using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace GV.Web3
{
    /// <summary>
    /// Shows a reward popup after a match ends.
    ///
    /// What this does:
    /// - When rewards are distributed, shows a centered panel with "1st Place! +100 PRANA"
    /// - Shows bonus rewards (XP, Energy, Gems) below the token reward
    /// - Displays minting status ("Minting tokens..." → "Tokens received!")
    /// - Shows updated total balance
    /// - Has a Close button to dismiss
    ///
    /// How to set it up:
    /// 1. Add this script to any GameObject in the gameplay scene
    /// 2. That's it! It auto-creates its own Canvas and UI elements
    /// 3. OR if you want custom UI, drag your own TMP_Text and Panel references into the Inspector
    /// </summary>
    public class PostMatchRewardUI : MonoBehaviour
    {
        [Header("UI References (auto-created if empty)")]
        [Tooltip("The root panel — shown when rewards are distributed, hidden otherwise.")]
        [SerializeField] private GameObject rewardPanel;

        [Tooltip("Shows the player's placement (e.g. '1st Place!').")]
        [SerializeField] private TMP_Text placementText;

        [Tooltip("Shows how many tokens earned (e.g. '+100 PRANA').")]
        [SerializeField] private TMP_Text rewardAmountText;

        [Tooltip("Shows XP earned (e.g. '+500 XP').")]
        [SerializeField] private TMP_Text xpText;

        [Tooltip("Shows Energy earned (e.g. '+50 Energy').")]
        [SerializeField] private TMP_Text energyText;

        [Tooltip("Shows Gems earned (e.g. '+10 Gems').")]
        [SerializeField] private TMP_Text gemsText;

        [Tooltip("Shows Coins earned (e.g. '+200 Coins').")]
        [SerializeField] private TMP_Text coinsText;

        [Tooltip("Shows status messages (e.g. 'Minting tokens...' or 'Tokens received!').")]
        [SerializeField] private TMP_Text statusText;

        [Tooltip("Shows the updated total balance after rewards.")]
        [SerializeField] private TMP_Text totalBalanceText;

        // Retry subscription — BattleRewardManager may be auto-created after us
        private bool _subscribedToRewards = false;

        private void Start()
        {
            // Auto-create UI if no panel reference is assigned
            if (rewardPanel == null)
            {
                BuildUI();
            }

            // Start hidden
            rewardPanel.SetActive(false);
        }

        private void OnEnable()
        {
            TrySubscribeToRewardManager();
        }

        private void Update()
        {
            // Keep trying until we subscribe (BattleRewardManager may be auto-created later)
            if (!_subscribedToRewards)
            {
                TrySubscribeToRewardManager();
            }
        }

        private void TrySubscribeToRewardManager()
        {
            if (_subscribedToRewards) return;
            if (BattleRewardManager.Instance == null) return;

            BattleRewardManager.Instance.OnRewardsDistributed += HandleRewardsDistributed;
            BattleRewardManager.Instance.OnRewardError += HandleRewardError;
            BattleRewardManager.Instance.OnTokenBalanceUpdated += HandleBalanceUpdated;
            _subscribedToRewards = true;

            Debug.Log("[PostMatchRewardUI] Subscribed to BattleRewardManager events.");
        }

        private void OnDisable()
        {
            if (BattleRewardManager.Instance != null && _subscribedToRewards)
            {
                BattleRewardManager.Instance.OnRewardsDistributed -= HandleRewardsDistributed;
                BattleRewardManager.Instance.OnRewardError -= HandleRewardError;
                BattleRewardManager.Instance.OnTokenBalanceUpdated -= HandleBalanceUpdated;
            }
            _subscribedToRewards = false;
        }

        /// <summary>
        /// Show the reward panel before minting starts (call this when match ends).
        /// Displays a "processing" state while tokens are being minted.
        /// </summary>
        public void ShowProcessing(int playerPlacement)
        {
            if (rewardPanel != null)
                rewardPanel.SetActive(true);

            PopulateRewardDisplay(playerPlacement);

            if (statusText != null)
                statusText.text = "Minting tokens...";

            if (totalBalanceText != null)
                totalBalanceText.text = "";
        }

        /// <summary>
        /// Show the reward panel when no wallet is connected.
        /// Shows placement and potential reward, but explains that wallet is needed.
        /// </summary>
        public void ShowNoWallet(int playerPlacement)
        {
            if (rewardPanel != null)
                rewardPanel.SetActive(true);

            PopulateRewardDisplay(playerPlacement);

            if (statusText != null)
                statusText.text = "Connect wallet to receive rewards!";

            if (totalBalanceText != null)
                totalBalanceText.text = "Start from Bootstrap scene to connect";

            Debug.Log($"[PostMatchRewardUI] Showing no-wallet state for placement {playerPlacement}");
        }

        /// <summary>
        /// Close/dismiss the reward panel. Hooked to the Close button.
        /// </summary>
        public void ClosePanel()
        {
            if (rewardPanel != null)
                rewardPanel.SetActive(false);
        }

        // --- Shared Helper ---

        /// <summary>
        /// Fills in the placement, token reward, and bonus reward text fields.
        /// Used by both ShowProcessing and ShowNoWallet to avoid duplicating logic.
        /// </summary>
        private void PopulateRewardDisplay(int playerPlacement)
        {
            float expectedReward = 0;
            string placementLabel = $"#{playerPlacement}";
            PlacementReward rewardConfig = null;

            if (BattleRewardManager.Instance != null)
            {
                rewardConfig = BattleRewardManager.Instance.GetPlacementReward(playerPlacement);

                if (rewardConfig != null)
                {
                    expectedReward = rewardConfig.tokenAmount;
                    placementLabel = rewardConfig.placementLabel;
                }
            }

            string tokenSymbol = BattleRewardManager.Instance?.TokenSymbol ?? "PRANA";

            if (placementText != null)
                placementText.text = placementLabel;

            if (rewardAmountText != null)
                rewardAmountText.text = $"+{expectedReward} {tokenSymbol}";

            // Bonus rewards — show each one if it's > 0
            // Names come from BattleRewardManager Inspector fields (customizable)
            string r1Name = BattleRewardManager.Instance?.BonusReward1Name ?? "XP";
            string r2Name = BattleRewardManager.Instance?.BonusReward2Name ?? "Energy";
            string r3Name = BattleRewardManager.Instance?.BonusReward3Name ?? "Gems";
            string r4Name = BattleRewardManager.Instance?.BonusReward4Name ?? "Coins";

            if (xpText != null)
                xpText.text = rewardConfig != null && rewardConfig.xpAmount > 0
                    ? $"+{rewardConfig.xpAmount} {r1Name}"
                    : "";

            if (energyText != null)
                energyText.text = rewardConfig != null && rewardConfig.energyAmount > 0
                    ? $"+{rewardConfig.energyAmount} {r2Name}"
                    : "";

            if (gemsText != null)
                gemsText.text = rewardConfig != null && rewardConfig.gemsAmount > 0
                    ? $"+{rewardConfig.gemsAmount} {r3Name}"
                    : "";

            if (coinsText != null)
                coinsText.text = rewardConfig != null && rewardConfig.coinsAmount > 0
                    ? $"+{rewardConfig.coinsAmount} {r4Name}"
                    : "";
        }

        // --- Event Handlers ---

        private void HandleRewardsDistributed(List<RewardResult> results)
        {
            if (rewardPanel != null)
                rewardPanel.SetActive(true);

            string playerAddress = Web3Manager.Instance?.WalletAddress ?? "";
            RewardResult myResult = results.Find(r =>
                r.walletAddress.ToLower() == playerAddress.ToLower());

            if (myResult != null)
            {
                // Update the full display with final results
                PopulateRewardDisplay(myResult.placement);

                string tokenSymbol = BattleRewardManager.Instance?.TokenSymbol ?? "PRANA";

                // Override the token amount text with actual minted amount
                if (rewardAmountText != null)
                    rewardAmountText.text = myResult.tokenAmount > 0
                        ? $"+{myResult.tokenAmount} {tokenSymbol}"
                        : "No reward";

                if (statusText != null)
                    statusText.text = myResult.success
                        ? "Tokens received!"
                        : $"Failed: {myResult.message}";
            }
            else
            {
                if (statusText != null)
                    statusText.text = "Match complete";
            }
        }

        private void HandleRewardError(string errorMessage)
        {
            if (rewardPanel != null)
                rewardPanel.SetActive(true);

            if (statusText != null)
                statusText.text = $"Error: {errorMessage}";
        }

        private void HandleBalanceUpdated(string formattedBalance)
        {
            if (totalBalanceText != null)
                totalBalanceText.text = $"Total: {formattedBalance}";
        }

        // --- Auto-Build UI ---

        /// <summary>
        /// Creates the reward popup UI from code so you don't have to set it up manually.
        /// Makes a Canvas with a centered dark panel, text fields, and a close button.
        /// </summary>
        private void BuildUI()
        {
            // Create a screen-space overlay canvas
            var canvasGO = new GameObject("RewardCanvas");
            canvasGO.transform.SetParent(transform);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100; // on top of everything
            canvasGO.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasGO.GetComponent<CanvasScaler>().referenceResolution = new Vector2(1920, 1080);
            canvasGO.AddComponent<GraphicRaycaster>();

            // Dark semi-transparent background overlay (covers full screen)
            var overlayGO = new GameObject("Overlay");
            overlayGO.transform.SetParent(canvasGO.transform, false);
            var overlayImage = overlayGO.AddComponent<Image>();
            overlayImage.color = new Color(0, 0, 0, 0.6f);
            var overlayRect = overlayGO.GetComponent<RectTransform>();
            overlayRect.anchorMin = Vector2.zero;
            overlayRect.anchorMax = Vector2.one;
            overlayRect.offsetMin = Vector2.zero;
            overlayRect.offsetMax = Vector2.zero;

            // Centered panel (dark box) — taller to fit bonus rewards
            var panelGO = new GameObject("RewardPanel");
            panelGO.transform.SetParent(canvasGO.transform, false);
            var panelImage = panelGO.AddComponent<Image>();
            panelImage.color = new Color(0.08f, 0.08f, 0.12f, 0.95f); // near-black with slight blue
            var panelRect = panelGO.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(500, 470);
            panelRect.anchoredPosition = Vector2.zero;

            // Add rounded corner look via Outline
            var outline = panelGO.AddComponent<Outline>();
            outline.effectColor = new Color(0.9f, 0.7f, 0.2f, 0.8f); // gold border
            outline.effectDistance = new Vector2(2, -2);

            rewardPanel = canvasGO; // hide the whole canvas

            // --- Text elements ---

            // "MATCH COMPLETE" header
            var headerText = CreateText(panelGO.transform, "HeaderText",
                "MATCH COMPLETE", 28, new Color(0.9f, 0.7f, 0.2f), // gold
                new Vector2(0, 180));
            headerText.fontStyle = FontStyles.Bold;

            // Placement (e.g. "1st Place!")
            placementText = CreateText(panelGO.transform, "PlacementText",
                "1st Place!", 42, Color.white,
                new Vector2(0, 120));
            placementText.fontStyle = FontStyles.Bold;

            // Reward amount (e.g. "+100 PRANA")
            rewardAmountText = CreateText(panelGO.transform, "RewardAmountText",
                "+100 PRANA", 34, new Color(0.3f, 1f, 0.5f), // green
                new Vector2(0, 60));
            rewardAmountText.fontStyle = FontStyles.Bold;

            // --- Bonus Rewards Grid (2x2) ---
            // Row 1: XP (left) | Energy (right)
            // Row 2: Gems (left) | Coins (right)

            xpText = CreateText(panelGO.transform, "XPText",
                "+500 XP", 22, new Color(0.5f, 0.8f, 1f), // light blue
                new Vector2(-110, 15));
            xpText.fontStyle = FontStyles.Bold;

            energyText = CreateText(panelGO.transform, "EnergyText",
                "+50 Energy", 22, new Color(1f, 0.85f, 0.3f), // yellow-orange
                new Vector2(110, 15));
            energyText.fontStyle = FontStyles.Bold;

            gemsText = CreateText(panelGO.transform, "GemsText",
                "+10 Gems", 22, new Color(0.85f, 0.4f, 1f), // purple
                new Vector2(-110, -20));
            gemsText.fontStyle = FontStyles.Bold;

            coinsText = CreateText(panelGO.transform, "CoinsText",
                "+200 Coins", 22, new Color(1f, 0.75f, 0.2f), // gold
                new Vector2(110, -20));
            coinsText.fontStyle = FontStyles.Bold;

            // Status (e.g. "Minting tokens..." / "Tokens received!")
            statusText = CreateText(panelGO.transform, "StatusText",
                "Minting tokens...", 20, new Color(0.7f, 0.7f, 0.7f),
                new Vector2(0, -60));

            // Total balance
            totalBalanceText = CreateText(panelGO.transform, "TotalBalanceText",
                "", 18, new Color(0.6f, 0.6f, 0.6f),
                new Vector2(0, -90));

            // Close button
            var btnGO = new GameObject("CloseButton");
            btnGO.transform.SetParent(panelGO.transform, false);
            var btnImage = btnGO.AddComponent<Image>();
            btnImage.color = new Color(0.2f, 0.2f, 0.25f, 1f);
            var btnRect = btnGO.GetComponent<RectTransform>();
            btnRect.anchorMin = new Vector2(0.5f, 0.5f);
            btnRect.anchorMax = new Vector2(0.5f, 0.5f);
            btnRect.sizeDelta = new Vector2(160, 45);
            btnRect.anchoredPosition = new Vector2(0, -170);

            var btn = btnGO.AddComponent<Button>();
            btn.onClick.AddListener(ClosePanel);

            var btnText = CreateText(btnGO.transform, "BtnText",
                "CLOSE", 20, Color.white, Vector2.zero);
            btnText.fontStyle = FontStyles.Bold;

            Debug.Log("[PostMatchRewardUI] Auto-built reward popup UI.");
        }

        private TMP_Text CreateText(Transform parent, string name, string content,
            int fontSize, Color color, Vector2 position)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = content;
            tmp.fontSize = fontSize;
            tmp.color = color;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableWordWrapping = false;

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(460, 50);
            rect.anchoredPosition = position;

            return tmp;
        }
    }
}
