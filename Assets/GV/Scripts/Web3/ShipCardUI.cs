using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace GV.Web3
{
    /// <summary>
    /// A single ship card in the selection screen.
    /// Attach this to your ship card prefab.
    ///
    /// The prefab should contain:
    /// - A name text (TMP_Text)
    /// - A rarity text (TMP_Text)
    /// - An icon image (Image)
    /// - A select button (Button) — covers the whole card
    /// - A lock overlay (GameObject) — shown when the ship isn't owned or is locked
    /// - A lock reason text (TMP_Text, optional) — shows "Locked" vs "Not Owned" on the overlay
    /// - A selection highlight (GameObject) — shown when this card is the active pick
    /// </summary>
    public class ShipCardUI : MonoBehaviour
    {
        [Header("Card UI Elements")]
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text rarityText;
        [SerializeField] private Image iconImage;
        [SerializeField] private Button selectButton;
        [SerializeField] private GameObject lockOverlay;
        [SerializeField] private GameObject selectionHighlight;

        [Header("Lock Reason (optional)")]
        [Tooltip("Text on the lock overlay explaining why the ship is unavailable. " +
                 "If not assigned, only the lock overlay icon is shown.")]
        [SerializeField] private TMP_Text lockReasonText;

        [Header("Colors")]
        [SerializeField] private Color ownedColor = Color.white;
        [SerializeField] private Color lockedColor = new Color(0.4f, 0.4f, 0.4f, 1f);

        /// <summary>The ship this card represents.</summary>
        public ShipDefinition Ship { get; private set; }

        /// <summary>True if this ship is locked by design (not just unowned).</summary>
        public bool IsLocked { get; private set; }

        private System.Action<ShipDefinition> _onClick;

        /// <summary>
        /// Set up the card with ship data.
        /// Called by ShipSelectionUI when populating the grid.
        /// </summary>
        /// <param name="ship">The ship definition.</param>
        /// <param name="owned">True if the player owns this ship (NFT balance > 0 or default).</param>
        /// <param name="locked">True if the ship is locked regardless of ownership.</param>
        /// <param name="onClick">Callback when the card is clicked.</param>
        public void Setup(ShipDefinition ship, bool owned, bool locked, System.Action<ShipDefinition> onClick)
        {
            Ship = ship;
            IsLocked = locked;
            _onClick = onClick;

            bool available = owned && !locked;

            if (nameText != null) nameText.text = ship.displayName;
            if (rarityText != null) rarityText.text = ship.rarity.ToString();

            if (iconImage != null)
            {
                if (ship.icon != null)
                {
                    iconImage.sprite = ship.icon;
                    iconImage.enabled = true;
                }
                else
                {
                    iconImage.enabled = false;
                }
            }

            // Lock overlay — visible if the ship isn't available (locked or not owned)
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

            // Wire up the button — still clickable so we can show a message, but visually dimmed
            if (selectButton != null)
            {
                selectButton.onClick.RemoveAllListeners();
                selectButton.onClick.AddListener(() => _onClick?.Invoke(Ship));
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
    }
}
