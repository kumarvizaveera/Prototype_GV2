using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace GV.Web3
{
    /// <summary>
    /// A single character card in the selection screen.
    /// Attach this to your character card prefab.
    ///
    /// The prefab should contain:
    /// - A name text (TMP_Text)
    /// - A rarity text (TMP_Text)
    /// - An icon image (Image)
    /// - A select button (Button) — covers the whole card
    /// - A lock overlay (GameObject) — shown when the character isn't owned or is locked
    /// - A lock reason text (TMP_Text, optional) — shows "Locked" vs "Not Owned" on the overlay
    /// - A selection highlight (GameObject) — shown when this card is the active pick
    ///
    /// This mirrors ShipCardUI — same structure, different data type.
    /// </summary>
    public class CharacterCardUI : MonoBehaviour
    {
        [Header("Card UI Elements")]
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text rarityText;
        [SerializeField] private Image iconImage;
        [SerializeField] private Button selectButton;
        [SerializeField] private GameObject lockOverlay;
        [SerializeField] private GameObject selectionHighlight;

        [Header("Lock Reason (optional)")]
        [Tooltip("Text on the lock overlay explaining why the character is unavailable.")]
        [SerializeField] private TMP_Text lockReasonText;

        [Header("Slot Badge (optional)")]
        [Tooltip("Text showing which slot(s) this character is assigned to (e.g. '1', '2', '1+2'). Hidden when empty.")]
        [SerializeField] private TMP_Text slotBadgeText;

        [Header("Colors")]
        [SerializeField] private Color ownedColor = Color.white;
        [SerializeField] private Color lockedColor = new Color(0.4f, 0.4f, 0.4f, 1f);

        /// <summary>The character this card represents.</summary>
        public CharacterDefinition Character { get; private set; }

        /// <summary>True if this character is locked by design (not just unowned).</summary>
        public bool IsLocked { get; private set; }

        private System.Action<CharacterDefinition> _onClick;

        /// <summary>
        /// Set up the card with character data.
        /// Called by CharacterSelectionUI when populating the grid.
        /// </summary>
        /// <param name="character">The character definition.</param>
        /// <param name="owned">True if the player owns this character (NFT balance > 0 or default).</param>
        /// <param name="locked">True if the character is locked regardless of ownership.</param>
        /// <param name="onClick">Callback when the card is clicked.</param>
        public void Setup(CharacterDefinition character, bool owned, bool locked, System.Action<CharacterDefinition> onClick)
        {
            Character = character;
            IsLocked = locked;
            _onClick = onClick;

            bool available = owned && !locked;

            if (nameText != null) nameText.text = character.displayName;
            if (rarityText != null) rarityText.text = character.rarity.ToString();

            if (iconImage != null)
            {
                if (character.icon != null)
                {
                    iconImage.sprite = character.icon;
                    iconImage.enabled = true;
                }
                else
                {
                    iconImage.enabled = false;
                }
            }

            // Lock overlay — visible if the character isn't available
            if (lockOverlay != null) lockOverlay.SetActive(!available);

            // Show reason on the lock overlay
            if (lockReasonText != null)
            {
                if (locked)
                    lockReasonText.text = "Locked";
                else if (!owned)
                    lockReasonText.text = "Not Owned";
                else
                    lockReasonText.text = "";
            }

            // Dim the card if not available
            if (nameText != null) nameText.color = available ? ownedColor : lockedColor;
            if (rarityText != null) rarityText.color = available ? ownedColor : lockedColor;

            // Wire up the button
            if (selectButton != null)
            {
                selectButton.onClick.RemoveAllListeners();
                selectButton.onClick.AddListener(() => _onClick?.Invoke(Character));
                selectButton.interactable = available;
            }

            // Hide selection highlight initially
            if (selectionHighlight != null) selectionHighlight.SetActive(false);
        }

        /// <summary>
        /// Show/hide the selection highlight ring.
        /// </summary>
        public void SetSelected(bool selected)
        {
            if (selectionHighlight != null) selectionHighlight.SetActive(selected);
        }

        /// <summary>
        /// Show a slot badge on the card (e.g. "1", "2", "1+2"). Empty string hides it.
        /// </summary>
        public void SetSlotBadge(string badge)
        {
            if (slotBadgeText != null)
            {
                slotBadgeText.text = badge;
                slotBadgeText.gameObject.SetActive(!string.IsNullOrEmpty(badge));
            }
        }
    }
}
