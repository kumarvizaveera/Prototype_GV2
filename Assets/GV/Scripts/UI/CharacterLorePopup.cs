using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace GV.UI
{
    /// <summary>
    /// Two-level character lore popup.
    /// Level 1: Basic info panel (name, region, rarity, role, powers) + 4 detail buttons.
    /// Level 2: Detail panel (backstory / strengths / weaknesses / rivals) shown on button click.
    ///
    /// SETUP:
    /// 1. Create a Canvas with a panel as the popup root (assign to popupRoot).
    /// 2. Inside, place TMP texts for each basic field.
    /// 3. Add 4 buttons (Backstory, Strengths, Weaknesses, Rivals).
    /// 4. Add a second child panel (detailPanel) with a title TMP and body TMP.
    /// 5. Add a back/close button on the detail panel.
    /// 6. Call ShowCharacter(loreData) from your selection screen when a character is selected.
    /// </summary>
    public class CharacterLorePopup : MonoBehaviour
    {
        [Header("Popup Root")]
        [Tooltip("The entire popup. Toggled on/off.")]
        public GameObject popupRoot;

        [Header("Level 1 — Basic Info")]
        public TMP_Text nameText;
        public TMP_Text taglineText;
        public TMP_Text classificationText;   // "Epic | Balanced Striker"
        public TMP_Text originText;            // "Kingdom: Nandana | Faction: Devas"
        public TMP_Text powersText;            // "Supreme Astra Powers, Dual Terrain..."

        [Header("Level 1 — Detail Buttons")]
        public Button backstoryButton;
        public Button strengthsButton;
        public Button weaknessesButton;
        public Button rivalsButton;

        [Header("Level 2 — Detail Panel")]
        [Tooltip("Child panel that slides in when a detail button is pressed.")]
        public GameObject detailPanel;
        public TMP_Text detailTitleText;
        public TMP_Text detailBodyText;
        public Button detailBackButton;

        [Header("Optional — Close")]
        public Button closeButton;

        // Currently displayed data
        private CharacterLoreData _current;

        // ─────────────────────────────────────────────
        //  LIFECYCLE
        // ─────────────────────────────────────────────

        private void Awake()
        {
            // Wire buttons
            if (backstoryButton  != null) backstoryButton.onClick.AddListener(OnBackstory);
            if (strengthsButton  != null) strengthsButton.onClick.AddListener(OnStrengths);
            if (weaknessesButton != null) weaknessesButton.onClick.AddListener(OnWeaknesses);
            if (rivalsButton     != null) rivalsButton.onClick.AddListener(OnRivals);
            if (detailBackButton != null) detailBackButton.onClick.AddListener(HideDetail);
            if (closeButton      != null) closeButton.onClick.AddListener(Hide);

            // Start hidden
            if (popupRoot   != null) popupRoot.SetActive(false);
            if (detailPanel != null) detailPanel.SetActive(false);
        }

        // ─────────────────────────────────────────────
        //  PUBLIC API
        // ─────────────────────────────────────────────

        /// <summary>
        /// Call this from your character selection screen when a character is
        /// selected/hovered. Populates Level 1 and shows the popup.
        /// </summary>
        public void ShowCharacter(CharacterLoreData data)
        {
            if (data == null) return;
            _current = data;

            // Populate Level 1
            SetText(nameText, data.characterName);
            SetText(taglineText, $"\"{data.tagline}\"");
            SetText(classificationText, $"{data.rarity}  |  {data.role}");
            SetText(originText, $"{data.regionLabel}: {data.regionName}  |  {data.factionLabel}: {data.factionName}");
            SetText(powersText, $"{data.powerClass}\n{data.terrainMastery}\n{data.tharaResonance}");

            // Ensure detail panel is hidden, show popup
            if (detailPanel != null) detailPanel.SetActive(false);
            if (popupRoot   != null) popupRoot.SetActive(true);
        }

        /// <summary>
        /// Hides the entire popup.
        /// </summary>
        public void Hide()
        {
            if (popupRoot   != null) popupRoot.SetActive(false);
            if (detailPanel != null) detailPanel.SetActive(false);
            _current = null;
        }

        /// <summary>
        /// Hides only the Level 2 detail panel, returning to Level 1.
        /// </summary>
        public void HideDetail()
        {
            if (detailPanel != null) detailPanel.SetActive(false);
        }

        // ─────────────────────────────────────────────
        //  DETAIL BUTTON HANDLERS
        // ─────────────────────────────────────────────

        private void OnBackstory()
        {
            if (_current == null) return;
            ShowDetail("BACKSTORY", _current.backstory);
        }

        private void OnStrengths()
        {
            if (_current == null) return;
            ShowDetail("STRENGTHS", _current.strengths);
        }

        private void OnWeaknesses()
        {
            if (_current == null) return;
            ShowDetail("WEAKNESSES", _current.weaknesses);
        }

        private void OnRivals()
        {
            if (_current == null) return;
            ShowDetail("RIVALS", _current.rivals);
        }

        // ─────────────────────────────────────────────
        //  INTERNALS
        // ─────────────────────────────────────────────

        private void ShowDetail(string title, string body)
        {
            SetText(detailTitleText, title);
            SetText(detailBodyText, body);
            if (detailPanel != null) detailPanel.SetActive(true);
        }

        private static void SetText(TMP_Text field, string value)
        {
            if (field != null) field.text = value ?? string.Empty;
        }
    }
}
