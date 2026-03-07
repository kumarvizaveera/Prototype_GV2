using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using GV.UI;

namespace GV.Web3
{
    /// <summary>
    /// Character selection screen — shows after ship selection, before room lobby.
    ///
    /// All characters from both rosters are displayed in a single container.
    /// Click-order selection per roster:
    ///   - 1st click on a roster's character = "Primary" (badge shows "Primary")
    ///   - 2nd click on a roster's character = "Secondary" (badge shows "Secondary")
    ///   - Clicking an already-assigned character deselects it (shifts Secondary → Primary)
    ///   - Player must pick 2 from each roster (4 total) before Confirm is enabled
    ///
    /// Slot mapping (matches CharacterNFTManager):
    ///   [0] = Ship 0 / Roster A - Primary
    ///   [1] = Ship 0 / Roster A - Secondary
    ///   [2] = Ship 1 / Roster B - Primary
    ///   [3] = Ship 1 / Roster B - Secondary
    /// </summary>
    public class CharacterSelectionUI : MonoBehaviour
    {
        [Header("Panel")]
        [SerializeField] private GameObject panel;

        [Header("Character Card Prefab")]
        [SerializeField] private GameObject characterCardPrefab;

        [Header("Card Rows")]
        [Tooltip("Row container for Ship 0 (Spaceship) characters. Use a HorizontalLayoutGroup.")]
        [SerializeField] private Transform rowShip0;

        [Tooltip("Row container for Ship 1 (Vimana) characters. Use a HorizontalLayoutGroup.")]
        [SerializeField] private Transform rowShip1;

        [Header("Info Display")]
        [SerializeField] private TMP_Text selectedCharNameText;
        [SerializeField] private TMP_Text selectedCharDescText;
        [SerializeField] private TMP_Text selectedCharRarityText;

        [Header("Buttons")]
        [SerializeField] private Button confirmButton;

        [Header("Lore Popup")]
        [Tooltip("Drag the GameObject with CharacterLorePopup. Auto-shows on card click.")]
        [SerializeField] private CharacterLorePopup lorePopup;

        [Header("Loading State")]
        [SerializeField] private GameObject loadingIndicator;
        [SerializeField] private TMP_Text statusText;

        // Track all spawned cards
        private List<GameObject> _spawnedCards = new List<GameObject>();

        // 4 slots: [0]=A-Primary, [1]=A-Secondary, [2]=B-Primary, [3]=B-Secondary
        private CharacterDefinition[] _slots = new CharacterDefinition[4];

        private void OnEnable()
        {
            if (CharacterNFTManager.Instance != null)
            {
                CharacterNFTManager.Instance.OnCharactersFetched += HandleCharactersFetched;
                CharacterNFTManager.Instance.OnFetchError += HandleFetchError;
            }

            if (confirmButton != null)
            {
                confirmButton.onClick.RemoveAllListeners();
                confirmButton.onClick.AddListener(OnConfirmClicked);
                confirmButton.interactable = false;
            }

            // Reset
            _slots[0] = null; _slots[1] = null; _slots[2] = null; _slots[3] = null;
            UpdateSlotDisplay();

            if (CharacterNFTManager.Instance != null && CharacterNFTManager.Instance.HasFetchedOnce)
            {
                PopulateAllCards();
                if (loadingIndicator != null) loadingIndicator.SetActive(false);
            }
            else
            {
                if (loadingIndicator != null) loadingIndicator.SetActive(true);
                SetStatus("Loading your characters...");
            }
        }

        private void OnDisable()
        {
            if (CharacterNFTManager.Instance != null)
            {
                CharacterNFTManager.Instance.OnCharactersFetched -= HandleCharactersFetched;
                CharacterNFTManager.Instance.OnFetchError -= HandleFetchError;
            }
        }

        public void Show(bool visible = true)
        {
            gameObject.SetActive(visible);
        }

        // --- Slot Display ---

        private void UpdateSlotDisplay()
        {
            bool allFilled = _slots[0] != null && _slots[1] != null && _slots[2] != null && _slots[3] != null;
            if (confirmButton != null) confirmButton.interactable = allFilled;

            RefreshCardBadges();
        }

        private void RefreshCardBadges()
        {
            foreach (var cardObj in _spawnedCards)
            {
                var cardUI = cardObj.GetComponent<CharacterCardUI>();
                if (cardUI == null) continue;

                // Figure out which roster pair this card belongs to
                int baseSlot = cardUI.Character.shipRosterIndex == 0 ? 0 : 2;

                string badge = "";
                bool isPrimary = _slots[baseSlot] != null && cardUI.Character == _slots[baseSlot];
                bool isSecondary = _slots[baseSlot + 1] != null && cardUI.Character == _slots[baseSlot + 1];

                if (isPrimary && isSecondary) badge = "Primary + Secondary";
                else if (isPrimary) badge = "Primary";
                else if (isSecondary) badge = "Secondary";

                cardUI.SetSelected(isPrimary || isSecondary);
                cardUI.SetSlotBadge(badge);
            }
        }

        // --- Event Handlers ---

        private void HandleCharactersFetched(List<CharacterDefinition> ownedCharacters)
        {
            if (loadingIndicator != null) loadingIndicator.SetActive(false);
            SetStatus("Pick 2 characters per ship type (4 total).");
            PopulateAllCards();
        }

        private void HandleFetchError(string error)
        {
            if (loadingIndicator != null) loadingIndicator.SetActive(false);
            SetStatus("Couldn't load character data. Default characters available.");
            PopulateAllCards();
        }

        // --- Card Population ---

        private void PopulateAllCards()
        {
            // Clear old cards
            foreach (var card in _spawnedCards)
            {
                if (card != null) Destroy(card);
            }
            _spawnedCards.Clear();

            if (CharacterNFTManager.Instance == null || characterCardPrefab == null) return;

            // Spawn characters into matching row based on shipRosterIndex
            var allCharacters = CharacterNFTManager.Instance.AllCharacters;

            foreach (var character in allCharacters)
            {
                // Pick the correct row for this character's roster
                Transform targetRow = character.shipRosterIndex == 0 ? rowShip0 : rowShip1;
                if (targetRow == null) continue;

                var cardObj = Instantiate(characterCardPrefab, targetRow);
                _spawnedCards.Add(cardObj);

                var cardUI = cardObj.GetComponent<CharacterCardUI>();
                if (cardUI != null)
                {
                    bool owned = CharacterNFTManager.Instance.OwnsCharacter(character);
                    cardUI.Setup(character, owned, character.isLocked, OnCharacterCardClicked);
                }
            }

            UpdateSlotDisplay();
        }

        // --- Click-Order Selection ---

        private void OnCharacterCardClicked(CharacterDefinition character)
        {
            if (character == null) return;

            if (character.isLocked)
            {
                SetStatus($"{character.displayName} is locked.");
                return;
            }

            if (!CharacterNFTManager.Instance.OwnsCharacter(character))
            {
                SetStatus($"You don't own this character yet.");
                return;
            }

            // Determine which roster pair this character belongs to
            // Roster A = slots 0,1 — Roster B = slots 2,3
            int baseSlot = character.shipRosterIndex == 0 ? 0 : 2;
            int primarySlot = baseSlot;
            int secondarySlot = baseSlot + 1;
            string rosterName = character.shipRosterIndex == 0 ? "Spaceship" : "Vimana";

            // If this character is already Primary in its roster, deselect and shift
            if (_slots[primarySlot] == character)
            {
                _slots[primarySlot] = _slots[secondarySlot]; // Secondary becomes Primary (or null)
                _slots[secondarySlot] = null;
                SetStatus(_slots[primarySlot] != null
                    ? $"{_slots[primarySlot].displayName} moved to Primary ({rosterName}). Pick another."
                    : $"Character removed. Pick a Primary for {rosterName}.");
                UpdateSlotDisplay();
                return;
            }

            // If this character is already Secondary in its roster, just remove it
            if (_slots[secondarySlot] == character)
            {
                _slots[secondarySlot] = null;
                SetStatus($"Secondary removed ({rosterName}). Pick another.");
                UpdateSlotDisplay();
                return;
            }

            // New character — assign to first empty slot in this roster
            if (_slots[primarySlot] == null)
            {
                _slots[primarySlot] = character;
                UpdateInfoDisplay(character);
                SetStatus($"Primary ({rosterName}): {character.displayName}. Pick a Secondary.");
            }
            else if (_slots[secondarySlot] == null)
            {
                _slots[secondarySlot] = character;
                UpdateInfoDisplay(character);

                // Check if all 4 are done
                if (_slots[0] != null && _slots[1] != null && _slots[2] != null && _slots[3] != null)
                    SetStatus("All characters selected! Press Confirm to continue.");
                else
                    SetStatus($"Secondary ({rosterName}): {character.displayName}. Now pick for the other roster.");
            }
            else
            {
                // Both slots full in this roster — replace Secondary
                _slots[secondarySlot] = character;
                UpdateInfoDisplay(character);
                SetStatus($"Secondary ({rosterName}) replaced with {character.displayName}.");
            }

            UpdateSlotDisplay();
        }

        private void UpdateInfoDisplay(CharacterDefinition character)
        {
            if (selectedCharNameText != null) selectedCharNameText.text = character.displayName;
            if (selectedCharDescText != null) selectedCharDescText.text = character.description;
            if (selectedCharRarityText != null) selectedCharRarityText.text = character.rarity.ToString();

            // Show lore popup — matches by character name against loreDatas array
            if (lorePopup != null)
                lorePopup.ShowCharacterByName(character.displayName);
        }

        private void OnConfirmClicked()
        {
            if (_slots[0] == null || _slots[1] == null || _slots[2] == null || _slots[3] == null) return;

            var manager = CharacterNFTManager.Instance;
            bool allOk = true;
            for (int i = 0; i < 4; i++)
            {
                if (!manager.SelectCharacterForSlot(i, _slots[i]))
                {
                    allOk = false;
                    break;
                }
            }

            if (allOk)
            {
                manager.ConfirmCharacters();
                Show(false);
            }
        }

        private void SetStatus(string message)
        {
            if (statusText != null) statusText.text = message;
        }
    }
}
