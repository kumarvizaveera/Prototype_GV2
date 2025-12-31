// CheckpointLabeler.cs
// Attach this to your CheckpointsParent (the object that has all checkpoints as children)

using System.Collections.Generic;
using UnityEngine;
using TMPro;

[ExecuteAlways]
public class CheckpointLabeler : MonoBehaviour
{
    [Header("Source")]
    [Tooltip("If empty, this GameObject is used as the parent.")]
    public Transform checkpointsParent;

    [Header("Label Prefab (optional)")]
    [Tooltip("Prefab can be: 3D TextMeshPro OR UI TextMeshProUGUI (World-Space Canvas).")]
    public GameObject labelPrefab;

    [Tooltip("Wrapper child name created under each checkpoint.")]
    public string labelChildName = "__CheckpointLabel";

    [Tooltip("Content child name under the wrapper (holds the prefab / TMP components).")]
    public string labelContentName = "__LabelContent";

    [Tooltip("Offset from checkpoint (local space).")]
    public Vector3 localOffset = new Vector3(0f, 2f, 0f);

    [Header("Rotation Offset")]
    [Tooltip("Applies an extra local rotation to the label content (Euler angles).")]
    public bool overrideRotationOffset = false;
    public Vector3 rotationOffsetEuler = Vector3.zero;

    [Header("Numbering")]
    public int startNumber = 1;
    [Tooltip("Examples: \"0\", \"00\", \"000\"")]
    public string numberFormat = "0";
    public string prefix = "";
    public string suffix = "";

    [Header("Style Override (Font + Size)")]
    public bool overrideFontAsset = true;
    public TMP_FontAsset fontAsset;

    public bool overrideFontMaterial = false;
    public Material fontMaterial;

    public bool overrideFontSize = true;
    public float fontSize = 3f;

    public bool autoSize = false;
    public float autoSizeMin = 1f;
    public float autoSizeMax = 8f;

    [Header("Text Box / Wrapping Fix")]
    [Tooltip("Prevents digits stacking vertically when font size increases.")]
    public bool overrideWrapping = true;

    [Tooltip("Recommended OFF so characters don't wrap onto new lines.")]
    public bool enableWordWrapping = false;

    [Tooltip("Recommended: Overflow so text won't wrap/clip.")]
    public TextOverflowModes overflowMode = TextOverflowModes.Overflow;

    [Tooltip("Applies alignment on every update.")]
    public bool overrideAlignment = true;
    public TextAlignmentOptions alignment = TextAlignmentOptions.Center;

    [Tooltip("Auto resize the RectTransform to fit the text (best for TMPUGUI/world-space canvas).")]
    public bool autoResizeRectToText = true;

    [Tooltip("Padding added when auto-resizing (x=width, y=height).")]
    public Vector2 rectPadding = new Vector2(20f, 10f);

    [Tooltip("Minimum rect size when auto-resizing.")]
    public Vector2 minRectSize = new Vector2(50f, 50f);

    [Header("Apply Targets")]
    [Tooltip("Apply changes to all TMP_Text under the content (recommended).")]
    public bool applyToAllTMPTexts = true;

    [Tooltip("If both TMPUGUI and 3D TMP exist, keep TMPUGUI and remove 3D TMP to avoid double text.")]
    public bool preferUGUIIfPresent = true;

    [Header("Transform / Size")]
    public float labelScale = 1f;
    public bool compensateParentScale = true;

    [Header("Facing Camera")]
    public bool faceCamera = true;
    public Camera cameraOverride;

    [Header("Advanced")]
    public bool onlyDirectChildren = true;

    void OnEnable() => Regenerate();
    void OnValidate() => Regenerate();

    [ContextMenu("Clear Labels")]
    public void ClearLabels()
    {
        Transform root = checkpointsParent != null ? checkpointsParent : transform;
        if (!root) return;

        var toDelete = new List<GameObject>();

        if (onlyDirectChildren)
        {
            for (int i = 0; i < root.childCount; i++)
            {
                Transform cp = root.GetChild(i);
                Transform lbl = cp.Find(labelChildName);
                if (lbl != null) toDelete.Add(lbl.gameObject);
            }
        }
        else
        {
            foreach (Transform cp in root.GetComponentsInChildren<Transform>(true))
            {
                if (cp == root) continue;
                Transform lbl = cp.Find(labelChildName);
                if (lbl != null) toDelete.Add(lbl.gameObject);
            }
        }

        foreach (var go in toDelete) DestroySafe(go);
    }

    [ContextMenu("Regenerate Labels")]
    public void Regenerate()
    {
        Transform root = checkpointsParent != null ? checkpointsParent : transform;
        if (!root) return;

        Camera cam = cameraOverride != null ? cameraOverride : Camera.main;

        List<Transform> checkpoints = new List<Transform>();
        if (onlyDirectChildren)
        {
            for (int i = 0; i < root.childCount; i++)
                checkpoints.Add(root.GetChild(i));
        }
        else
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t != root) checkpoints.Add(t);
        }

        for (int i = 0; i < checkpoints.Count; i++)
        {
            Transform cp = checkpoints[i];
            if (!cp) continue;

            // 1) Wrapper under checkpoint
            Transform wrapperTr = cp.Find(labelChildName);
            GameObject wrapperGO;
            if (wrapperTr == null)
            {
                wrapperGO = new GameObject(labelChildName);
                wrapperTr = wrapperGO.transform;
                wrapperTr.SetParent(cp, false);
            }
            else
            {
                wrapperGO = wrapperTr.gameObject;
            }

            wrapperTr.localPosition = localOffset;
            wrapperTr.localRotation = Quaternion.identity;

            // Wrapper scale compensation
            if (compensateParentScale)
            {
                Vector3 s = cp.lossyScale;
                float sx = Mathf.Abs(s.x) < 1e-6f ? 1f : s.x;
                float sy = Mathf.Abs(s.y) < 1e-6f ? 1f : s.y;
                float sz = Mathf.Abs(s.z) < 1e-6f ? 1f : s.z;

                wrapperTr.localScale = new Vector3(1f / sx, 1f / sy, 1f / sz) * labelScale;
            }
            else
            {
                wrapperTr.localScale = Vector3.one * labelScale;
            }

            // 2) Content under wrapper
            Transform contentTr = wrapperTr.Find(labelContentName);

            if (contentTr == null)
            {
                if (labelPrefab != null)
                {
                    GameObject contentInstance = Instantiate(labelPrefab, wrapperTr);
                    contentInstance.name = labelContentName;
                    contentTr = contentInstance.transform;
                }
                else
                {
                    GameObject contentGO = new GameObject(labelContentName);
                    contentTr = contentGO.transform;
                    contentTr.SetParent(wrapperTr, false);
                    contentGO.AddComponent<TextMeshPro>();
                }
            }

            contentTr.localPosition = Vector3.zero;
            contentTr.localScale = Vector3.one;
            contentTr.localRotation = overrideRotationOffset ? Quaternion.Euler(rotationOffsetEuler) : Quaternion.identity;

            // 3) TMP targets
            TMP_Text[] tmps = contentTr.GetComponentsInChildren<TMP_Text>(true);

            if (tmps == null || tmps.Length == 0)
            {
                var added = contentTr.gameObject.AddComponent<TextMeshPro>();
                tmps = new TMP_Text[] { added };
            }

            if (preferUGUIIfPresent)
            {
                bool hasUGUI = false;
                for (int t = 0; t < tmps.Length; t++)
                    if (tmps[t] is TextMeshProUGUI) { hasUGUI = true; break; }

                if (hasUGUI)
                {
                    var tmp3ds = contentTr.GetComponentsInChildren<TextMeshPro>(true);
                    foreach (var tmp3d in tmp3ds)
                        DestroySafe(tmp3d);

                    tmps = contentTr.GetComponentsInChildren<TMP_Text>(true);
                    if (tmps == null || tmps.Length == 0) continue;
                }
            }

            if (!applyToAllTMPTexts && tmps.Length > 1)
                tmps = new TMP_Text[] { tmps[0] };

            // 4) Apply text + style
            int num = startNumber + i;
            string text = prefix + num.ToString(numberFormat) + suffix;

            for (int t = 0; t < tmps.Length; t++)
            {
                TMP_Text tmp = tmps[t];
                if (!tmp) continue;

                tmp.text = text;

                if (overrideFontAsset && fontAsset != null)
                    tmp.font = fontAsset;

                if (overrideFontMaterial && fontMaterial != null)
                    tmp.fontMaterial = fontMaterial;

                if (overrideFontSize)
                {
                    tmp.enableAutoSizing = autoSize;
                    if (autoSize)
                    {
                        tmp.fontSizeMin = autoSizeMin;
                        tmp.fontSizeMax = autoSizeMax;
                    }
                    else
                    {
                        tmp.fontSize = fontSize;
                    }
                }

                // ---- WRAPPING FIX (prevents 1 / 0 / 6 vertical stacking) ----
                if (overrideWrapping)
                {
                    tmp.enableWordWrapping = enableWordWrapping;
                    tmp.overflowMode = overflowMode;
                }

                if (overrideAlignment)
                    tmp.alignment = alignment;

                // Resize rect so it can fit multi-digit numbers at larger font sizes
                if (autoResizeRectToText)
                {
                    // Ensure TMP has updated geometry before measuring
                    tmp.ForceMeshUpdate();

                    Vector2 preferred = tmp.GetPreferredValues(tmp.text);
                    float w = Mathf.Max(minRectSize.x, preferred.x + rectPadding.x);
                    float h = Mathf.Max(minRectSize.y, preferred.y + rectPadding.y);

                    // TMP_Text has rectTransform for both TMP and TMPUGUI
                    RectTransform rt = tmp.rectTransform;
                    if (rt != null)
                        rt.sizeDelta = new Vector2(w, h);
                }
            }

            // Billboard (on wrapper; rotation offset stays on content)
            if (faceCamera)
            {
                var bb = wrapperGO.GetComponent<BillboardToCamera>();
                if (!bb) bb = wrapperGO.AddComponent<BillboardToCamera>();
                bb.targetCamera = cam;
            }
            else
            {
                var bb = wrapperGO.GetComponent<BillboardToCamera>();
                if (bb) DestroySafe(bb);
            }
        }
    }

    void DestroySafe(Object o)
    {
#if UNITY_EDITOR
        if (!Application.isPlaying) DestroyImmediate(o);
        else Destroy(o);
#else
        Destroy(o);
#endif
    }
}
