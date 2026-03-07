using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using GV.UI;

namespace GV.Web3
{
    /// <summary>
    /// The ship selection screen — shows after wallet connect, before character selection.
    ///
    /// Click-order selection:
    /// - 1st click assigns Primary ship (badge shows "Primary" on the card)
    /// - 2nd click assigns Secondary ship (badge shows "Secondary" on the card)
    /// - Clicking a ship that's already assigned removes it (and shifts Secondary → Primary if needed)
    /// - Ships must be different types (different meshRootIndex)
    /// - Confirm button enables once both are picked
    /// </summary>
    public class ShipSelectionUI : MonoBehaviour
    {
        [Header("Panel")]
        [SerializeField] private GameObject panel;

        [Header("Ship Card Prefab")]
        [SerializeField] private GameObject shipCardPrefab;
        [SerializeField] private Transform cardContainer;

        [Header("Info Display")]
        [SerializeField] private TMP_Text selectedShipNameText;
        [SerializeField] private TMP_Text selectedShipDescText;
        [SerializeField] private TMP_Text selectedShipRarityText;

        [Header("Buttons")]
        [SerializeField] private Button confirmButton;

        [Header("Lore Popup")]
        [Tooltip("Drag the GameObject with ShipLorePopup. Auto-shows on icon click.")]
        [SerializeField] private ShipLorePopup lorePopup;

        [Header("Loading State")]
        [SerializeField] private GameObject loadingIndicator;
        [SerializeField] private TMP_Text statusText;

        // Track spawned cards
        private List<GameObject> _spawnedCards = new List<GameObject>();

        // Two-slot state: [0] = Primary, [1] = Secondary
        private ShipDefinition[] _slotShips = new ShipDefinition[2];

        private void Awake()
        {
            gameObject.SetActive(false);
        }

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
                confirmButton.interactable = false;
            }

            // Reset slots
            _slotShips[0] = null;
            _slotShips[1] = null;
            UpdateSlotDisplay();

            if (ShipNFTManager.Instance != null && ShipNFTManager.Instance.HasFetchedOnce)
            {
                PopulateShipCards();
                if (loadingIndicator != null) loadingIndicator.SetActive(false);
            }
            else
            {
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

        public void Show(bool visible = true)
        {
            gameObject.SetActive(visible);
        }

        // --- Slot Display ---

        private void UpdateSlotDisplay()
        {
            // Confirm only when both slots are filled
            bool bothFilled = _slotShips[0] != null && _slotShips[1] != null;
            if (confirmButton != null) confirmButton.interactable = bothFilled;

            // Update card slot badges
            RefreshCardSlotBadges();
        }

        private void RefreshCardSlotBadges()
        {
            foreach (var cardObj in _spawnedCards)
            {
                var cardUI = cardObj.GetComponent<ShipCardUI>();
                if (cardUI == null) continue;

                string badge = "";
                bool isPrimary = _slotShips[0] != null && cardUI.Ship == _slotShips[0];
                bool isSecondary = _slotShips[1] != null && cardUI.Ship == _slotShips[1];

                if (isPrimary) badge = "Primary";
                if (isSecondary) badge = "Secondary";

                cardUI.SetSelected(isPrimary || isSecondary);
                cardUI.SetSlotBadge(badge);
            }
        }

        // --- Event Handlers ---

        private void HandleShipsFetched(List<ShipDefinition> ownedShips)
        {
            if (loadingIndicator != null) loadingIndicator.SetActive(false);
            SetStatus($"You own {ownedShips.Count} ship(s). Pick two different types!");
            PopulateShipCards();
        }

        private void HandleFetchError(string error)
        {
            if (loadingIndicator != null) loadingIndicator.SetActive(false);
            SetStatus("Couldn't load ship data. You can still use the default ship.");
            PopulateShipCards();
        }

        // --- Card Management ---

        private void PopulateShipCards()
        {
            foreach (var card in _spawnedCards)
            {
                if (card != null) Destroy(card);
            }
            _spawnedCards.Clear();

            if (ShipNFTManager.Instance == null) return;

            var allShips = ShipNFTManager.Instance.AllShips;

            foreach (var ship in allShips)
            {
                if (shipCardPrefab == null || cardContainer == null) break;

                var cardObj = Instantiate(shipCardPrefab, cardContainer);
                _spawnedCards.Add(cardObj);

                var cardUI = cardObj.GetComponent<ShipCardUI>();
                if (cardUI != null)
                {
                    bool owned = ShipNFTManager.Instance.OwnsShip(ship);
                    cardUI.Setup(ship, owned, ship.isLocked, OnShipCardClicked, OnShipIconClicked);
                }
            }

            UpdateSlotDisplay();
        }

        private void OnShipCardClicked(ShipDefinition ship)
        {
            if (ship == null) return;

            if (ship.isLocked)
            {
                SetStatus($"{ship.displayName} is locked.");
                return;
            }

            if (!ShipNFTManager.Instance.OwnsShip(ship))
            {
                SetStatus($"You don't own this ship yet.");
                return;
            }

            // If this ship is already Primary, deselect it and shift Secondary → Primary
            if (_slotShips[0] == ship)
            {
                _slotShips[0] = _slotShips[1]; // Secondary becomes Primary (or null)
                _slotShips[1] = null;
                SetStatus(_slotShips[0] != null
                    ? $"{_slotShips[0].displayName} moved to Primary. Pick a Secondary ship."
                    : "Ship removed. Pick your Primary ship.");
                UpdateSlotDisplay();
                return;
            }

            // If this ship is already Secondary, just remove it
            if (_slotShips[1] == ship)
            {
                _slotShips[1] = null;
                SetStatus("Secondary ship removed. Pick another.");
                UpdateSlotDisplay();
                return;
            }

            // New ship — assign to first empty slot
            if (_slotShips[0] == null)
            {
                // Assigning Primary
                _slotShips[0] = ship;

                if (selectedShipNameText != null) selectedShipNameText.text = ship.displayName;
                if (selectedShipDescText != null) selectedShipDescText.text = ship.description;
                if (selectedShipRarityText != null) selectedShipRarityText.text = ship.rarity.ToString();

                SetStatus($"Primary: {ship.displayName}. Now pick your Secondary ship.");
            }
            else if (_slotShips[1] == null)
            {
                // Assigning Secondary — check type conflict
                if (_slotShips[0].meshRootIndex == ship.meshRootIndex)
                {
                    SetStatus("Both ships can't be the same type! Pick a different one.");
                    return;
                }

                _slotShips[1] = ship;

                if (selectedShipNameText != null) selectedShipNameText.text = ship.displayName;
                if (selectedShipDescText != null) selectedShipDescText.text = ship.description;
                if (selectedShipRarityText != null) selectedShipRarityText.text = ship.rarity.ToString();

                SetStatus("Both ships selected! Press Confirm to continue.");
            }
            else
            {
                // Both slots full — replace Secondary with this new pick
                if (_slotShips[0].meshRootIndex == ship.meshRootIndex)
                {
                    SetStatus("Both ships can't be the same type! Pick a different one.");
                    return;
                }

                _slotShips[1] = ship;

                if (selectedShipNameText != null) selectedShipNameText.text = ship.displayName;
                if (selectedShipDescText != null) selectedShipDescText.text = ship.description;
                if (selectedShipRarityText != null) selectedShipRarityText.text = ship.rarity.ToString();

                SetStatus($"Secondary replaced with {ship.displayName}. Press Confirm to continue.");
            }

            UpdateSlotDisplay();
        }

        private void OnConfirmClicked()
        {
            if (_slotShips[0] == null || _slotShips[1] == null) return;

            var manager = ShipNFTManager.Instance;
            bool ok0 = manager.SelectShipForSlot(0, _slotShips[0]);
            bool ok1 = manager.SelectShipForSlot(1, _slotShips[1]);

            if (ok0 && ok1)
            {
                manager.ConfirmShips();
                Show(false);
            }
        }

        // --- Icon Click → Lore Popup (works for ALL ships, even locked/unowned) ---

        private void OnShipIconClicked(ShipDefinition ship)
        {
            if (ship == null) return;

            if (lorePopup != null)
                lorePopup.ShowShipByName(ship.displayName);
        }

        private void SetStatus(string message)
        {
            if (statusText != null) statusText.text = message;
        }
    }
}
