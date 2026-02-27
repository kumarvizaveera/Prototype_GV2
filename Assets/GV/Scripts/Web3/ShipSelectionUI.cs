using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace GV.Web3
{
    /// <summary>
    /// The ship selection screen — shows after wallet connect, before entering a match.
    ///
    /// What this does:
    /// - Displays all available ships as selectable cards
    /// - Ships you own are bright and clickable
    /// - Ships you don't own are greyed out with a "locked" look
    /// - The default (free) ship is always available
    /// - When you pick a ship, it tells ShipNFTManager your choice
    ///
    /// How to set it up:
    /// 1. Create a Canvas → Panel for the selection screen
    /// 2. Inside it, add a container (e.g. HorizontalLayoutGroup) for ship cards
    /// 3. Create a "ship card" prefab with: name text, rarity text, description text,
    ///    icon image, a select button, and a lock overlay
    /// 4. Drag references into the Inspector slots
    /// 5. The "Confirm" button proceeds to gameplay after selection
    ///
    /// This panel hooks into WalletConnectPanel — it appears after wallet connect
    /// and the "Play" button now goes through ship selection first.
    /// </summary>
    public class ShipSelectionUI : MonoBehaviour
    {
        [Header("Panel")]
        [Tooltip("The root panel object — shown/hidden.")]
        [SerializeField] private GameObject panel;

        [Header("Ship Card Prefab")]
        [Tooltip("A prefab for each ship card in the grid. Must have ShipCardUI component.")]
        [SerializeField] private GameObject shipCardPrefab;

        [Tooltip("The parent container where ship cards get spawned (e.g. a HorizontalLayoutGroup).")]
        [SerializeField] private Transform cardContainer;

        [Header("Info Display")]
        [Tooltip("Shows the currently selected ship's name.")]
        [SerializeField] private TMP_Text selectedShipNameText;

        [Tooltip("Shows the currently selected ship's description.")]
        [SerializeField] private TMP_Text selectedShipDescText;

        [Tooltip("Shows the currently selected ship's rarity.")]
        [SerializeField] private TMP_Text selectedShipRarityText;

        [Header("Buttons")]
        [Tooltip("Confirms the selection and proceeds to gameplay.")]
        [SerializeField] private Button confirmButton;

        [Header("Loading State")]
        [Tooltip("Shown while NFT data is being loaded from the blockchain.")]
        [SerializeField] private GameObject loadingIndicator;

        [Tooltip("Status text (e.g. 'Loading your ships...').")]
        [SerializeField] private TMP_Text statusText;

        // Track spawned cards so we can clean them up
        private List<GameObject> _spawnedCards = new List<GameObject>();

        // Currently highlighted card
        private ShipDefinition _highlightedShip;

        private void OnEnable()
        {
            if (ShipNFTManager.Instance != null)
            {
                ShipNFTManager.Instance.OnShipsFetched += HandleShipsFetched;
                ShipNFTManager.Instance.OnFetchError += HandleFetchError;
            }

            if (confirmButton != null)
            {
                confirmButton.onClick.RemoveAllListeners();
                confirmButton.onClick.AddListener(OnConfirmClicked);
                confirmButton.interactable = false; // Disabled until a ship is selected
            }

            // If ships were already fetched (e.g. navigating back), populate immediately
            if (ShipNFTManager.Instance != null && ShipNFTManager.Instance.HasFetchedOnce)
            {
                PopulateShipCards();
                if (loadingIndicator != null) loadingIndicator.SetActive(false);
            }
            else
            {
                // Show loading state
                if (loadingIndicator != null) loadingIndicator.SetActive(true);
                SetStatus("Loading your ships...");
            }
        }

        private void OnDisable()
        {
            if (ShipNFTManager.Instance != null)
            {
                ShipNFTManager.Instance.OnShipsFetched -= HandleShipsFetched;
                ShipNFTManager.Instance.OnFetchError -= HandleFetchError;
            }
        }

        // --- Public Methods ---

        /// <summary>Show or hide the selection panel.</summary>
        public void Show(bool visible = true)
        {
            if (panel != null) panel.SetActive(visible);
        }

        // --- Event Handlers ---

        private void HandleShipsFetched(List<ShipDefinition> ownedShips)
        {
            if (loadingIndicator != null) loadingIndicator.SetActive(false);
            SetStatus($"You own {ownedShips.Count} ship(s). Pick one!");
            PopulateShipCards();
        }

        private void HandleFetchError(string error)
        {
            if (loadingIndicator != null) loadingIndicator.SetActive(false);
            SetStatus("Couldn't load ship data. You can still use the default ship.");
            PopulateShipCards(); // Still show what we can (at least the default)
        }

        // --- Card Management ---

        /// <summary>
        /// Creates a card in the UI for each configured ship.
        /// Owned ships are selectable, unowned ships are greyed out.
        /// </summary>
        private void PopulateShipCards()
        {
            // Clear old cards
            foreach (var card in _spawnedCards)
            {
                if (card != null) Destroy(card);
            }
            _spawnedCards.Clear();

            if (ShipNFTManager.Instance == null) return;

            var allShips = ShipNFTManager.Instance.AllShips;

            foreach (var ship in allShips)
            {
                if (shipCardPrefab == null || cardContainer == null)
                {
                    Debug.LogWarning("[ShipSelectionUI] Missing card prefab or container. " +
                        "Assign them in the Inspector.");
                    break;
                }

                var cardObj = Instantiate(shipCardPrefab, cardContainer);
                _spawnedCards.Add(cardObj);

                var cardUI = cardObj.GetComponent<ShipCardUI>();
                if (cardUI != null)
                {
                    bool owned = ShipNFTManager.Instance.OwnsShip(ship);
                    cardUI.Setup(ship, owned, OnShipCardClicked);
                }
                else
                {
                    Debug.LogWarning("[ShipSelectionUI] Ship card prefab is missing ShipCardUI component!");
                }
            }

            // Auto-select the default ship or currently selected ship
            var currentSelection = ShipNFTManager.Instance.SelectedShip;
            if (currentSelection != null)
            {
                HighlightShip(currentSelection);
            }
        }

        /// <summary>
        /// Called when the player clicks on a ship card.
        /// </summary>
        private void OnShipCardClicked(ShipDefinition ship)
        {
            if (ship == null) return;

            if (!ShipNFTManager.Instance.OwnsShip(ship))
            {
                SetStatus($"You don't own this ship yet.");
                return;
            }

            HighlightShip(ship);
        }

        private void HighlightShip(ShipDefinition ship)
        {
            _highlightedShip = ship;

            if (selectedShipNameText != null) selectedShipNameText.text = ship.displayName;
            if (selectedShipDescText != null) selectedShipDescText.text = ship.description;
            if (selectedShipRarityText != null) selectedShipRarityText.text = ship.rarity.ToString();

            if (confirmButton != null) confirmButton.interactable = true;

            // Update card visuals to show which is selected
            foreach (var cardObj in _spawnedCards)
            {
                var cardUI = cardObj.GetComponent<ShipCardUI>();
                if (cardUI != null)
                {
                    cardUI.SetSelected(cardUI.Ship == ship);
                }
            }
        }

        private void OnConfirmClicked()
        {
            if (_highlightedShip == null) return;

            bool success = ShipNFTManager.Instance.SelectShip(_highlightedShip);
            if (success)
            {
                Debug.Log($"[ShipSelectionUI] Confirmed ship: {_highlightedShip.displayName}");
                // Hide the panel — WalletConnectPanel or another script handles loading the next scene
                Show(false);
            }
        }

        private void SetStatus(string message)
        {
            if (statusText != null) statusText.text = message;
        }
    }
}
