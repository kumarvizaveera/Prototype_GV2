using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace GV.Web3
{
    /// <summary>
    /// The character selection screen — shows after ship selection, before entering a room.
    ///
    /// What this does:
    /// - Displays characters that belong to the selected ship's roster
    /// - Characters you own are bright and clickable
    /// - Characters you don't own are greyed out with a "locked" look
    /// - The default (free) character is always available
    /// - When you pick a character, it tells CharacterNFTManager your choice
    ///
    /// How to set it up:
    /// 1. Create a Canvas → Panel for the selection screen (clone ShipSelectionUI panel)
    /// 2. Inside it, add a container (e.g. HorizontalLayoutGroup) for character cards
    /// 3. Create a "character card" prefab with: name text, rarity text, description text,
    ///    icon image, a select button, and a lock overlay (clone ShipCardUI prefab)
    /// 4. Drag references into the Inspector slots
    /// 5. The "Confirm" button proceeds to the room lobby after selection
    ///
    /// Per-ship filtering:
    /// Only characters whose shipRosterIndex matches the selected ship's meshRootIndex
    /// are shown. E.g., if the player picked the Spaceship (meshRootIndex=0),
    /// only Roster A characters appear.
    /// </summary>
    public class CharacterSelectionUI : MonoBehaviour
    {
        [Header("Panel")]
        [Tooltip("The root panel object — shown/hidden.")]
        [SerializeField] private GameObject panel;

        [Header("Character Card Prefab")]
        [Tooltip("A prefab for each character card in the grid. Must have CharacterCardUI component.")]
        [SerializeField] private GameObject characterCardPrefab;

        [Tooltip("The parent container where character cards get spawned (e.g. a HorizontalLayoutGroup).")]
        [SerializeField] private Transform cardContainer;

        [Header("Info Display")]
        [Tooltip("Shows the currently selected character's name.")]
        [SerializeField] private TMP_Text selectedCharNameText;

        [Tooltip("Shows the currently selected character's description.")]
        [SerializeField] private TMP_Text selectedCharDescText;

        [Tooltip("Shows the currently selected character's rarity.")]
        [SerializeField] private TMP_Text selectedCharRarityText;

        [Header("Buttons")]
        [Tooltip("Confirms the selection and proceeds to the room lobby.")]
        [SerializeField] private Button confirmButton;

        [Header("Loading State")]
        [Tooltip("Shown while NFT data is being loaded from the blockchain.")]
        [SerializeField] private GameObject loadingIndicator;

        [Tooltip("Status text (e.g. 'Loading your characters...').")]
        [SerializeField] private TMP_Text statusText;

        // Track spawned cards so we can clean them up
        private List<GameObject> _spawnedCards = new List<GameObject>();

        // Currently highlighted character
        private CharacterDefinition _highlightedCharacter;

        // NOTE: Unlike ShipSelectionUI, we do NOT call gameObject.SetActive(false) in Awake().
        // The panel should start disabled in the scene hierarchy (uncheck the checkbox in Inspector).
        // If we self-hide in Awake(), it fights with WalletConnectPanel trying to SetActive(true),
        // because Unity runs Awake() on the first activation, immediately undoing the show.

        private void OnEnable()
        {
            Debug.Log("[CharacterSelectionUI] OnEnable called. Checking setup...");

            // --- Diagnostic: log what's assigned and what's missing ---
            bool hasManager = CharacterNFTManager.Instance != null;
            Debug.Log($"[CharacterSelectionUI] CharacterNFTManager: {(hasManager ? "FOUND" : "MISSING — add CharacterNFTManager component to a GameObject in the scene!")}");
            Debug.Log($"[CharacterSelectionUI] characterCardPrefab: {(characterCardPrefab != null ? "assigned" : "MISSING")}");
            Debug.Log($"[CharacterSelectionUI] cardContainer: {(cardContainer != null ? "assigned" : "MISSING")}");
            Debug.Log($"[CharacterSelectionUI] confirmButton: {(confirmButton != null ? "assigned" : "MISSING")}");
            Debug.Log($"[CharacterSelectionUI] statusText: {(statusText != null ? "assigned" : "MISSING")}");

            if (hasManager)
            {
                CharacterNFTManager.Instance.OnCharactersFetched += HandleCharactersFetched;
                CharacterNFTManager.Instance.OnFetchError += HandleFetchError;

                int characterCount = CharacterNFTManager.Instance.AllCharacters.Count;
                Debug.Log($"[CharacterSelectionUI] Total characters configured: {characterCount}");

                if (characterCount == 0)
                {
                    Debug.LogWarning("[CharacterSelectionUI] No characters configured in CharacterNFTManager! " +
                        "Add character definitions in the Inspector.");
                }
            }

            if (confirmButton != null)
            {
                confirmButton.onClick.RemoveAllListeners();
                confirmButton.onClick.AddListener(OnConfirmClicked);
                confirmButton.interactable = false; // Disabled until a character is selected
            }

            // If characters were already fetched (e.g. navigating back), populate immediately
            if (hasManager && CharacterNFTManager.Instance.HasFetchedOnce)
            {
                Debug.Log("[CharacterSelectionUI] Characters already fetched, populating cards now.");
                PopulateCharacterCards();
                if (loadingIndicator != null) loadingIndicator.SetActive(false);
            }
            else
            {
                // Show loading state — waiting for CharacterNFTManager to finish fetching
                if (loadingIndicator != null) loadingIndicator.SetActive(true);
                SetStatus(hasManager ? "Loading your characters..." : "Character system not ready — add CharacterNFTManager to the scene.");
                Debug.Log($"[CharacterSelectionUI] Waiting for character data... hasFetched={hasManager && CharacterNFTManager.Instance.HasFetchedOnce}");
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

        // --- Public Methods ---

        /// <summary>Show or hide the selection panel.</summary>
        public void Show(bool visible = true)
        {
            gameObject.SetActive(visible);
        }

        // --- Event Handlers ---

        private void HandleCharactersFetched(List<CharacterDefinition> ownedCharacters)
        {
            if (loadingIndicator != null) loadingIndicator.SetActive(false);

            // Count owned characters for the current ship roster
            int rosterIndex = GetCurrentShipRosterIndex();
            int ownedForRoster = 0;
            foreach (var c in ownedCharacters)
            {
                if (c.shipRosterIndex == rosterIndex) ownedForRoster++;
            }

            SetStatus($"You own {ownedForRoster} character(s) for this ship. Pick one!");
            PopulateCharacterCards();
        }

        private void HandleFetchError(string error)
        {
            if (loadingIndicator != null) loadingIndicator.SetActive(false);
            SetStatus("Couldn't load character data. You can still use the default character.");
            PopulateCharacterCards(); // Still show what we can (at least the default)
        }

        // --- Card Management ---

        /// <summary>
        /// Creates a card in the UI for each character that matches the selected ship's roster.
        /// Owned characters are selectable, unowned characters are greyed out.
        /// </summary>
        private void PopulateCharacterCards()
        {
            // Clear old cards
            foreach (var card in _spawnedCards)
            {
                if (card != null) Destroy(card);
            }
            _spawnedCards.Clear();

            if (CharacterNFTManager.Instance == null) return;

            int rosterIndex = GetCurrentShipRosterIndex();
            var rosterCharacters = CharacterNFTManager.Instance.GetCharactersForShip(rosterIndex);

            foreach (var character in rosterCharacters)
            {
                if (characterCardPrefab == null || cardContainer == null)
                {
                    Debug.LogWarning("[CharacterSelectionUI] Missing card prefab or container. " +
                        "Assign them in the Inspector.");
                    break;
                }

                var cardObj = Instantiate(characterCardPrefab, cardContainer);
                _spawnedCards.Add(cardObj);

                var cardUI = cardObj.GetComponent<CharacterCardUI>();
                if (cardUI != null)
                {
                    bool owned = CharacterNFTManager.Instance.OwnsCharacter(character);
                    cardUI.Setup(character, owned, character.isLocked, OnCharacterCardClicked);
                }
                else
                {
                    Debug.LogWarning("[CharacterSelectionUI] Character card prefab is missing CharacterCardUI component!");
                }
            }

            // Auto-select the default character for this roster or currently selected character
            var currentSelection = CharacterNFTManager.Instance.SelectedCharacter;
            if (currentSelection != null && currentSelection.shipRosterIndex == rosterIndex && !currentSelection.isLocked)
            {
                HighlightCharacter(currentSelection);
            }
            else
            {
                // Auto-select the default for this roster
                var defaultChar = CharacterNFTManager.Instance.GetDefaultCharacter(rosterIndex);
                if (defaultChar != null)
                {
                    HighlightCharacter(defaultChar);
                }
            }
        }

        /// <summary>
        /// Called when the player clicks on a character card.
        /// </summary>
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

            HighlightCharacter(character);
        }

        private void HighlightCharacter(CharacterDefinition character)
        {
            _highlightedCharacter = character;

            if (selectedCharNameText != null) selectedCharNameText.text = character.displayName;
            if (selectedCharDescText != null) selectedCharDescText.text = character.description;
            if (selectedCharRarityText != null) selectedCharRarityText.text = character.rarity.ToString();

            if (confirmButton != null) confirmButton.interactable = true;

            // Update card visuals to show which is selected
            foreach (var cardObj in _spawnedCards)
            {
                var cardUI = cardObj.GetComponent<CharacterCardUI>();
                if (cardUI != null)
                {
                    cardUI.SetSelected(cardUI.Character == character);
                }
            }
        }

        private void OnConfirmClicked()
        {
            if (_highlightedCharacter == null) return;

            bool success = CharacterNFTManager.Instance.SelectCharacter(_highlightedCharacter);
            if (success)
            {
                Debug.Log($"[CharacterSelectionUI] Confirmed character: {_highlightedCharacter.displayName}");
                // Hide the panel — WalletConnectPanel handles showing the room lobby
                Show(false);
            }
        }

        private void SetStatus(string message)
        {
            if (statusText != null) statusText.text = message;
        }

        /// <summary>
        /// Gets the mesh root index of the currently selected ship.
        /// Used to filter characters to the correct roster.
        /// </summary>
        private int GetCurrentShipRosterIndex()
        {
            if (ShipNFTManager.Instance != null && ShipNFTManager.Instance.SelectedShip != null)
            {
                return ShipNFTManager.Instance.SelectedShip.meshRootIndex;
            }
            return 0; // Default to Roster A if no ship selected
        }
    }
}
