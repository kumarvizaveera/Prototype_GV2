using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace GV.UI
{
    /// <summary>
    /// Two-level character lore popup — auto-builds its own UI from code.
    ///
    /// Level 1:  Name, tagline, classification, origin, powers + 4 detail buttons
    /// Level 2:  Detail panel (backstory / strengths / weaknesses / rivals) on button click
    ///
    /// How to set it up:
    /// 1. Add this script to any GameObject in your character selection scene
    /// 2. That's it! It auto-creates its own Canvas and UI elements
    /// 3. From your selection screen, call ShowCharacter(loreData) when a character is picked
    ///
    /// Matches PostMatchRewardUI visual style (dark panel, gold accents, overlay canvas).
    /// </summary>
    public class CharacterLorePopup : MonoBehaviour
    {
        [Header("Lore Data (assign all characters)")]
        [Tooltip("Array of CharacterLoreData assets — one per character, in roster order.")]
        public CharacterLoreData[] loreDatas;

        // --- Auto-built references ---
        private GameObject _popupRoot;

        // Level 1
        private TMP_Text _nameText;
        private TMP_Text _taglineText;
        private TMP_Text _classText;
        private TMP_Text _originText;
        private TMP_Text _powersText;
        private Button   _btnBackstory;
        private Button   _btnStrengths;
        private Button   _btnWeaknesses;
        private Button   _btnRivals;

        // Level 2
        private GameObject _detailPanel;
        private TMP_Text   _detailTitle;
        private TMP_Text   _detailBody;

        // Active highlight tracking
        private Image _activeBtnImage;

        // Currently displayed data
        private CharacterLoreData _current;
        private bool _uiBuilt = false;

        // ════════════════════════════════════════════
        //  COLORS — matching PostMatchRewardUI style
        // ════════════════════════════════════════════

        private static readonly Color COL_OVERLAY      = new Color(0f, 0f, 0f, 0.6f);
        private static readonly Color COL_PANEL_BG     = new Color(0.08f, 0.08f, 0.12f, 0.95f);
        private static readonly Color COL_DETAIL_BG    = new Color(0.06f, 0.06f, 0.10f, 0.98f);
        private static readonly Color COL_GOLD         = new Color(0.9f, 0.7f, 0.2f, 1f);
        private static readonly Color COL_GOLD_DIM     = new Color(0.9f, 0.7f, 0.2f, 0.8f);
        private static readonly Color COL_WHITE        = Color.white;
        private static readonly Color COL_GREY_LIGHT   = new Color(0.78f, 0.78f, 0.82f, 1f);
        private static readonly Color COL_GREY         = new Color(0.6f, 0.6f, 0.65f, 1f);
        private static readonly Color COL_BTN_NORMAL   = new Color(0.18f, 0.18f, 0.24f, 1f);
        private static readonly Color COL_BTN_HOVER    = new Color(0.25f, 0.25f, 0.32f, 1f);
        private static readonly Color COL_BTN_ACTIVE   = new Color(0.35f, 0.28f, 0.12f, 1f);
        private static readonly Color COL_CLOSE_BG     = new Color(0.2f, 0.2f, 0.25f, 1f);
        private static readonly Color COL_CYAN         = new Color(0.4f, 0.85f, 0.95f, 1f);
        private static readonly Color COL_GREEN        = new Color(0.3f, 1f, 0.5f, 1f);
        private static readonly Color COL_RED_SOFT     = new Color(1f, 0.45f, 0.4f, 1f);
        private static readonly Color COL_PURPLE       = new Color(0.85f, 0.5f, 1f, 1f);

        // ════════════════════════════════════════════
        //  LIFECYCLE
        // ════════════════════════════════════════════

        private void Start()
        {
            if (!_uiBuilt) BuildUI();
            _popupRoot.SetActive(false);
        }

        // ════════════════════════════════════════════
        //  PUBLIC API
        // ════════════════════════════════════════════

        /// <summary>
        /// Show the lore popup for a character by index (matches loreDatas array order).
        /// Call from your character selection card click handler.
        /// </summary>
        public void ShowCharacterByIndex(int index)
        {
            if (loreDatas == null || index < 0 || index >= loreDatas.Length) return;
            ShowCharacter(loreDatas[index]);
        }

        /// <summary>
        /// Show the lore popup by matching character name against loreDatas array.
        /// Call from CharacterSelectionUI when a card is clicked.
        /// </summary>
        public void ShowCharacterByName(string characterName)
        {
            if (loreDatas == null || string.IsNullOrEmpty(characterName)) return;
            foreach (var data in loreDatas)
            {
                if (data != null && data.characterName == characterName)
                {
                    ShowCharacter(data);
                    return;
                }
            }
            Debug.LogWarning($"[CharacterLorePopup] No lore data found for '{characterName}'.");
        }

        /// <summary>
        /// Show the lore popup for a specific CharacterLoreData asset.
        /// </summary>
        public void ShowCharacter(CharacterLoreData data)
        {
            if (data == null) return;
            if (!_uiBuilt) BuildUI();

            _current = data;

            // Populate Level 1
            _nameText.text    = data.characterName;
            _taglineText.text = $"\"{data.tagline}\"";
            _classText.text   = $"{data.rarity}  ·  {data.role}";

            string cyanHex = ColorUtility.ToHtmlStringRGB(COL_CYAN);
            _originText.text  = $"{data.regionLabel}: <color=#{cyanHex}>{data.regionName}</color>    " +
                                $"{data.factionLabel}: <color=#{cyanHex}>{data.factionName}</color>";
            _powersText.text  = $"{data.powerClass}  ·  {data.terrainMastery}  ·  {data.tharaResonance}";

            // Hide detail, clear button highlight, show popup
            _detailPanel.SetActive(false);
            ClearActiveButton();
            _popupRoot.SetActive(true);
        }

        /// <summary>
        /// Hide the entire popup.
        /// </summary>
        public void Hide()
        {
            if (_popupRoot != null) _popupRoot.SetActive(false);
            _current = null;
        }

        // ════════════════════════════════════════════
        //  BUTTON HANDLERS
        // ════════════════════════════════════════════

        private void OnBackstory()
        {
            if (_current == null) return;
            ShowDetail("BACKSTORY", _current.backstory, _btnBackstory);
        }

        private void OnStrengths()
        {
            if (_current == null) return;
            ShowDetail("STRENGTHS", FormatBullets(_current.strengths, COL_GREEN), _btnStrengths);
        }

        private void OnWeaknesses()
        {
            if (_current == null) return;
            ShowDetail("WEAKNESSES", FormatBullets(_current.weaknesses, COL_RED_SOFT), _btnWeaknesses);
        }

        private void OnRivals()
        {
            if (_current == null) return;
            ShowDetail("RIVALS", FormatBullets(_current.rivals, COL_PURPLE), _btnRivals);
        }

        private void OnBack()
        {
            _detailPanel.SetActive(false);
            ClearActiveButton();
        }

        // ════════════════════════════════════════════
        //  HELPERS
        // ════════════════════════════════════════════

        private void ShowDetail(string title, string body, Button sourceBtn)
        {
            _detailTitle.text = title;
            _detailBody.text  = body;
            _detailPanel.SetActive(true);

            // Highlight the active button, dim the rest
            ClearActiveButton();
            if (sourceBtn != null)
            {
                _activeBtnImage = sourceBtn.GetComponent<Image>();
                if (_activeBtnImage != null) _activeBtnImage.color = COL_BTN_ACTIVE;
            }
        }

        private void ClearActiveButton()
        {
            if (_activeBtnImage != null)
            {
                _activeBtnImage.color = COL_BTN_NORMAL;
                _activeBtnImage = null;
            }
        }

        /// <summary>
        /// Turns newline-separated text into TMP rich-text bullets with color.
        /// </summary>
        private static string FormatBullets(string raw, Color bulletColor)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            string hex = ColorUtility.ToHtmlStringRGB(bulletColor);
            string[] lines = raw.Split('\n');
            var sb = new System.Text.StringBuilder();
            foreach (string line in lines)
            {
                string trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;
                sb.Append($"<color=#{hex}>-</color>  {trimmed}\n\n");
            }
            return sb.ToString().TrimEnd('\n');
        }

        // ════════════════════════════════════════════
        //  AUTO-BUILD UI
        // ════════════════════════════════════════════

        private void BuildUI()
        {
            _uiBuilt = true;

            // ── Canvas (ScreenSpaceOverlay, above reward popup) ──
            var canvasGO = new GameObject("LorePopupCanvas");
            canvasGO.transform.SetParent(transform);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 110;
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode        = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            canvasGO.AddComponent<GraphicRaycaster>();

            _popupRoot = canvasGO;

            // ── Full-screen dark overlay ──
            var overlay = CreatePanel(canvasGO.transform, "Overlay", COL_OVERLAY);
            StretchFull(overlay);

            // ══════════════════════════════════════
            //  LEVEL 1 — MAIN INFO PANEL
            // ══════════════════════════════════════

            var panel = CreatePanel(canvasGO.transform, "LorePanel", COL_PANEL_BG);
            var panelRect = panel.GetComponent<RectTransform>();
            CenterRect(panelRect, 560, 420);

            // Gold border
            var outline = panel.AddComponent<Outline>();
            outline.effectColor    = COL_GOLD_DIM;
            outline.effectDistance = new Vector2(2, -2);

            // ── Character Name ──
            _nameText = CreateText(panel.transform, "CharName", "CHARACTER", 30, COL_GOLD,
                new Vector2(0, 170), new Vector2(500, 45));
            _nameText.fontStyle = FontStyles.Bold;

            // ── Tagline ──
            _taglineText = CreateText(panel.transform, "Tagline", "\"...\"", 18, COL_GREY_LIGHT,
                new Vector2(0, 138), new Vector2(500, 35));
            _taglineText.fontStyle = FontStyles.Italic;

            // ── Divider 1 ──
            CreateDivider(panel.transform, "Divider1", 118);

            // ── Classification: "Epic · Balanced Striker" ──
            _classText = CreateText(panel.transform, "Classification", "Epic · Balanced Striker", 22, COL_WHITE,
                new Vector2(0, 90), new Vector2(500, 35));
            _classText.fontStyle = FontStyles.Bold;

            // ── Origin: "Kingdom: Nandana  Faction: Devas" ──
            _originText = CreateText(panel.transform, "Origin", "Kingdom: ... | Faction: ...", 19, COL_GREY_LIGHT,
                new Vector2(0, 58), new Vector2(500, 32));
            _originText.richText = true;

            // ── Powers ──
            _powersText = CreateText(panel.transform, "Powers",
                "Supreme Astra · Dual Terrain · Normal Thara", 16, COL_GREY,
                new Vector2(0, 28), new Vector2(520, 30));

            // ── Divider 2 ──
            CreateDivider(panel.transform, "Divider2", 6);

            // ── 4 Detail buttons in a row ──
            float btnY   = -35f;
            float btnW   = 110f;
            float btnH   = 38f;
            float gap    = 8f;
            float totalW = btnW * 4 + gap * 3;
            float startX = -totalW / 2f + btnW / 2f;

            _btnBackstory  = CreateDetailButton(panel.transform, "Backstory",  startX + (btnW + gap) * 0, btnY, btnW, btnH);
            _btnStrengths  = CreateDetailButton(panel.transform, "Strengths",  startX + (btnW + gap) * 1, btnY, btnW, btnH);
            _btnWeaknesses = CreateDetailButton(panel.transform, "Weaknesses", startX + (btnW + gap) * 2, btnY, btnW, btnH);
            _btnRivals     = CreateDetailButton(panel.transform, "Rivals",     startX + (btnW + gap) * 3, btnY, btnW, btnH);

            _btnBackstory.onClick.AddListener(OnBackstory);
            _btnStrengths.onClick.AddListener(OnStrengths);
            _btnWeaknesses.onClick.AddListener(OnWeaknesses);
            _btnRivals.onClick.AddListener(OnRivals);

            // ── Close button ──
            var closeBtn = CreateStyledButton(panel.transform, "CLOSE", COL_CLOSE_BG,
                new Vector2(0, -170), new Vector2(140, 40), 18);
            closeBtn.onClick.AddListener(Hide);

            // ══════════════════════════════════════
            //  LEVEL 2 — DETAIL PANEL (appears below buttons)
            // ══════════════════════════════════════

            _detailPanel = CreatePanel(panel.transform, "DetailPanel", COL_DETAIL_BG);
            var dpRect = _detailPanel.GetComponent<RectTransform>();
            dpRect.anchorMin        = new Vector2(0.5f, 0.5f);
            dpRect.anchorMax        = new Vector2(0.5f, 0.5f);
            dpRect.sizeDelta        = new Vector2(500, 250);
            dpRect.anchoredPosition = new Vector2(0, -110);

            // Gold border on detail
            var dpOutline = _detailPanel.AddComponent<Outline>();
            dpOutline.effectColor    = COL_GOLD_DIM;
            dpOutline.effectDistance = new Vector2(1, -1);

            // Detail title
            _detailTitle = CreateText(_detailPanel.transform, "DetailTitle", "BACKSTORY", 22, COL_GOLD,
                new Vector2(0, 100), new Vector2(460, 35));
            _detailTitle.fontStyle = FontStyles.Bold;

            // Close (✕) button — top-right of detail panel
            var backBtn = CreateStyledButton(_detailPanel.transform, "\u2715", COL_BTN_NORMAL,
                new Vector2(215, 100), new Vector2(50, 30), 18);
            backBtn.onClick.AddListener(OnBack);

            // ── Scrollable body ──
            BuildScrollableBody(_detailPanel.transform);

            // Start hidden
            _detailPanel.SetActive(false);

            Debug.Log("[CharacterLorePopup] Auto-built two-level lore popup UI.");
        }

        /// <summary>
        /// Simple body text inside the detail panel — fixed rect, word wrap, no ScrollRect.
        /// </summary>
        private void BuildScrollableBody(Transform parent)
        {
            // Body text — fills most of the detail panel below the title
            _detailBody = CreateText(parent, "DetailBody", "", 16, COL_GREY_LIGHT,
                new Vector2(0, -20), new Vector2(460, 170));
            _detailBody.alignment          = TextAlignmentOptions.TopLeft;
            _detailBody.enableWordWrapping = true;
            _detailBody.overflowMode       = TextOverflowModes.Ellipsis;
            _detailBody.richText           = true;
        }

        // ════════════════════════════════════════════
        //  UI BUILDER HELPERS
        // ════════════════════════════════════════════

        private static GameObject CreatePanel(Transform parent, string name, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<Image>().color = color;
            return go;
        }

        private static void StretchFull(GameObject go)
        {
            var r = go.GetComponent<RectTransform>();
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = Vector2.zero;
            r.offsetMax = Vector2.zero;
        }

        private static void CenterRect(RectTransform r, float w, float h)
        {
            r.anchorMin        = new Vector2(0.5f, 0.5f);
            r.anchorMax        = new Vector2(0.5f, 0.5f);
            r.sizeDelta        = new Vector2(w, h);
            r.anchoredPosition = Vector2.zero;
        }

        private static TMP_Text CreateText(Transform parent, string name, string content,
            int fontSize, Color color, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text               = content;
            tmp.fontSize           = fontSize;
            tmp.color              = color;
            tmp.alignment          = TextAlignmentOptions.Center;
            tmp.enableWordWrapping = false;
            tmp.richText           = true;

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin        = new Vector2(0.5f, 0.5f);
            rect.anchorMax        = new Vector2(0.5f, 0.5f);
            rect.sizeDelta        = size;
            rect.anchoredPosition = position;

            return tmp;
        }

        private static void CreateDivider(Transform parent, string name, float yPos)
        {
            var div = CreatePanel(parent, name, COL_GOLD_DIM);
            var r = div.GetComponent<RectTransform>();
            r.anchorMin        = new Vector2(0.5f, 0.5f);
            r.anchorMax        = new Vector2(0.5f, 0.5f);
            r.sizeDelta        = new Vector2(480, 1);
            r.anchoredPosition = new Vector2(0, yPos);
        }

        private Button CreateDetailButton(Transform parent, string label,
            float x, float y, float w, float h)
        {
            var go = new GameObject("Btn_" + label);
            go.transform.SetParent(parent, false);

            var img = go.AddComponent<Image>();
            img.color = COL_BTN_NORMAL;

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin        = new Vector2(0.5f, 0.5f);
            rect.anchorMax        = new Vector2(0.5f, 0.5f);
            rect.sizeDelta        = new Vector2(w, h);
            rect.anchoredPosition = new Vector2(x, y);

            var btn = go.AddComponent<Button>();
            SetButtonColors(btn, COL_BTN_NORMAL, COL_BTN_HOVER);

            var text = CreateText(go.transform, "Label", label, 15, COL_WHITE,
                Vector2.zero, new Vector2(w, h));
            text.fontStyle = FontStyles.Bold;

            return btn;
        }

        private static Button CreateStyledButton(Transform parent, string label, Color bgColor,
            Vector2 position, Vector2 size, int fontSize)
        {
            var go = new GameObject("Btn_" + label.Replace("\u2715", "Close"));
            go.transform.SetParent(parent, false);

            var img = go.AddComponent<Image>();
            img.color = bgColor;

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin        = new Vector2(0.5f, 0.5f);
            rect.anchorMax        = new Vector2(0.5f, 0.5f);
            rect.sizeDelta        = size;
            rect.anchoredPosition = position;

            var btn = go.AddComponent<Button>();
            SetButtonColors(btn, bgColor, COL_BTN_HOVER);

            var text = CreateText(go.transform, "Label", label, fontSize, COL_WHITE,
                Vector2.zero, size);
            text.fontStyle = FontStyles.Bold;

            return btn;
        }

        private static void SetButtonColors(Button btn, Color normal, Color highlighted)
        {
            var colors = btn.colors;
            colors.normalColor      = normal;
            colors.highlightedColor = highlighted;
            colors.pressedColor     = highlighted;
            colors.selectedColor    = normal;
            btn.colors = colors;
        }
    }
}
