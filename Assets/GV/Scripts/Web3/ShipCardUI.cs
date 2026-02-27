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
    /// - A lock overlay (GameObject) — shown when the ship isn't owned
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

        [Header("Colors")]
        [SerializeField] private Color ownedColor = Color.white;
        [SerializeField] private Color lockedColor = new Color(0.4f, 0.4f, 0.4f, 1f);

        /// <summary>The ship this card represents.</summary>
        public ShipDefinition Ship { get; private set; }

        private System.Action<ShipDefinition> _onClick;

        /// <summary>
        /// Set up the card with ship data.
        /// Called by ShipSelectionUI when populating the grid.
        /// </summary>
        public void Setup(ShipDefinition ship, bool owned, System.Action<ShipDefinition> onClick)
        {
            Ship = ship;
            _onClick = onClick;

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

            // Lock overlay — visible if the player doesn't own this ship
            if (lockOverlay != null) lockOverlay.SetActive(!owned);

            // Dim the card if not owned
            if (nameText != null) nameText.color = owned ? ownedColor : lockedColor;
            if (rarityText != null) rarityText.color = owned ? ownedColor : lockedColor;

            // Wire up the button
            if (selectButton != null)
            {
                selectButton.onClick.RemoveAllListeners();
                selectButton.onClick.AddListener(() => _onClick?.Invoke(Ship));
                selectButton.interactable = owned;
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
