using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
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
        private System.Action<CharacterDefinition> _onIconClick;
        private GameObject _iconClickOverlay; // transparent overlay ON TOP of selectButton for icon clicks

        /// <summary>
        /// Set up the card with character data.
        /// Called by CharacterSelectionUI when populating the grid.
        /// </summary>
        /// <param name="character">The character definition.</param>
        /// <param name="owned">True if the player owns this character (NFT balance > 0 or default).</param>
        /// <param name="locked">True if the character is locked regardless of ownership.</param>
        /// <param name="onClick">Callback when the select button is clicked.</param>
        /// <param name="onIconClick">Callback when the icon is clicked (lore popup). Works for ALL characters.</param>
        public void Setup(CharacterDefinition character, bool owned, bool locked,
            System.Action<CharacterDefinition> onClick,
            System.Action<CharacterDefinition> onIconClick = null)
        {
            Character = character;
            IsLocked = locked;
            _onClick = onClick;
            _onIconClick = onIconClick;

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

                // Create a transparent clickable overlay ON TOP of the entire card hierarchy
                // so it sits above selectButton and intercepts icon-area clicks.
                // Always interactable — works even for locked/unowned characters.
                if (_onIconClick != null)
                {
                    BuildIconClickOverlay();
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
        /// Creates a transparent overlay button covering the icon area,
        /// parented as the LAST child of the card so it renders on top of selectButton.
        /// </summary>
        private void BuildIconClickOverlay()
        {
            if (_iconClickOverlay != null) Destroy(_iconClickOverlay);
            if (iconImage == null) return;

            // Create overlay as last child of card root (on top of everything)
            _iconClickOverlay = new GameObject("IconClickOverlay");
            _iconClickOverlay.transform.SetParent(transform, false);
            _iconClickOverlay.transform.SetAsLastSibling(); // ensures it's on top

            // Match the icon's position and size
            var overlayRect = _iconClickOverlay.AddComponent<RectTransform>();
            var iconRect = iconImage.rectTransform;

            // Copy anchors, pivot, position, size from iconImage
            overlayRect.anchorMin = iconRect.anchorMin;
            overlayRect.anchorMax = iconRect.anchorMax;
            overlayRect.pivot = iconRect.pivot;
            overlayRect.anchoredPosition = iconRect.anchoredPosition;
            overlayRect.sizeDelta = iconRect.sizeDelta;
            overlayRect.offsetMin = iconRect.offsetMin;
            overlayRect.offsetMax = iconRect.offsetMax;

            // If icon is nested (not direct child of card root), convert position
            if (iconImage.transform.parent != transform)
            {
                // Get icon's world corners and map to card's local space
                Vector3[] corners = new Vector3[4];
                iconRect.GetWorldCorners(corners);

                var cardRect = GetComponent<RectTransform>();
                Vector2 localMin, localMax;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    cardRect, RectTransformUtility.WorldToScreenPoint(null, corners[0]), null, out localMin);
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    cardRect, RectTransformUtility.WorldToScreenPoint(null, corners[2]), null, out localMax);

                overlayRect.anchorMin = new Vector2(0.5f, 0.5f);
                overlayRect.anchorMax = new Vector2(0.5f, 0.5f);
                overlayRect.pivot = new Vector2(0.5f, 0.5f);
                overlayRect.anchoredPosition = (localMin + localMax) * 0.5f;
                overlayRect.sizeDelta = new Vector2(localMax.x - localMin.x, localMax.y - localMin.y);
            }

            // Transparent Image so it receives raycasts
            var img = _iconClickOverlay.AddComponent<Image>();
            img.color = new Color(0, 0, 0, 0); // fully transparent
            img.raycastTarget = true;

            // Button — always interactable
            var btn = _iconClickOverlay.AddComponent<Button>();
            btn.transition = Selectable.Transition.None; // no visual change
            btn.onClick.AddListener(() => _onIconClick?.Invoke(Character));
            btn.interactable = true;
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
