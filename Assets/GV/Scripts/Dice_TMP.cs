using UnityEngine;
using TMPro;

[ExecuteAlways]
public class DiceRingIndicator : MonoBehaviour
{
    [Header("Text (TMP) - assign ALL duplicated TMPs here")]
    public TMP_Text[] tmpTexts;

    [Tooltip("Show +N for green and -N for red.")]
    public bool showSign = false;

    [Header("TMP Text Color")]
    [Tooltip("If enabled, TMP text color changes to forward/backward color.")]
    public bool colorizeTMP = true;

    [Tooltip("Use these colors for TMP when 'colorizeTMP' is enabled.")]
    public Color tmpForwardGreen = Color.green;
    public Color tmpBackwardRed = Color.red;

    [Header("Dice Range")]
    [Min(1)] public int minValue = 1;
    [Min(1)] public int maxValue = 6;

    [Header("Backward (Minus) Restriction")]
    [Tooltip("If true, backward (red) dice value is capped to 'maxBackwardValue'.")]
    public bool restrictBackwardMax = true;

    [Tooltip("Maximum value allowed when backward (red). Example: 6 means -1..-6 only.")]
    [Min(1)] public int maxBackwardValue = 6;

    [Header("Visual (Emission)")]
    public Renderer[] glowRenderers;
    public Color forwardGreen = Color.green;
    public Color backwardRed = Color.red;
    [Min(0f)] public float emissionIntensity = 2.5f;
    public string emissionColorProperty = "_EmissionColor";

    [Header("Rolling")]
    public bool autoRoll = true;
    [Min(0.05f)] public float rollInterval = 0.5f;
    public bool rollOnStart = true;

    [Header("Read-only (for later teleport logic)")]
    [SerializeField] private bool isForward = true;
    [SerializeField] private int diceValue = 1;

    MaterialPropertyBlock _mpb;
    float _t;

    public bool IsForward => isForward;
    public int DiceValue => diceValue;

    void OnEnable()
    {
        if (_mpb == null) _mpb = new MaterialPropertyBlock();
        EnableEmissionKeyword();

        if (Application.isPlaying && rollOnStart) Roll();
        ApplyVisuals();
    }

    void Update()
    {
        if (!Application.isPlaying)
        {
            ApplyVisuals();
            return;
        }

        if (!autoRoll) return;

        _t += Time.deltaTime;
        if (_t >= rollInterval)
        {
            _t = 0f;
            Roll();
        }
    }

    [ContextMenu("Roll Now")]
    public void Roll()
    {
        isForward = (Random.value >= 0.5f);

        int min = minValue;
        int max = maxValue;

        if (!isForward && restrictBackwardMax)
            max = Mathf.Min(max, maxBackwardValue);

        diceValue = Random.Range(min, max + 1);
        ApplyVisuals();
    }

    public void SetState(bool forward, int value)
    {
        isForward = forward;

        int min = minValue;
        int max = maxValue;

        if (!isForward && restrictBackwardMax)
            max = Mathf.Min(max, maxBackwardValue);

        diceValue = Mathf.Clamp(value, min, max);
        ApplyVisuals();
    }

    void ApplyVisuals()
    {
        // Text string
        string s = showSign ? ((isForward ? "+" : "-") + diceValue) : diceValue.ToString();

        // Colors
        Color ringBaseCol = isForward ? forwardGreen : backwardRed;
        Color tmpCol = isForward ? tmpForwardGreen : tmpBackwardRed;

        // TMP update
        if (tmpTexts != null)
        {
            for (int i = 0; i < tmpTexts.Length; i++)
            {
                var t = tmpTexts[i];
                if (!t) continue;

                t.text = s;

                if (colorizeTMP)
                    t.color = tmpCol;

#if UNITY_EDITOR
                t.ForceMeshUpdate();
#endif
            }
        }

        // Emission update
        Color emissionCol = ringBaseCol * emissionIntensity;

        if (_mpb == null) _mpb = new MaterialPropertyBlock();

        if (glowRenderers != null)
        {
            for (int i = 0; i < glowRenderers.Length; i++)
            {
                var r = glowRenderers[i];
                if (!r) continue;

                r.GetPropertyBlock(_mpb);
                _mpb.SetColor(emissionColorProperty, emissionCol);
                r.SetPropertyBlock(_mpb);
            }
        }
    }

    void EnableEmissionKeyword()
    {
        if (glowRenderers == null) return;

        for (int i = 0; i < glowRenderers.Length; i++)
        {
            var r = glowRenderers[i];
            if (!r || r.sharedMaterial == null) continue;
            r.sharedMaterial.EnableKeyword("_EMISSION");
        }
    }

    void OnValidate()
    {
        if (minValue < 1) minValue = 1;
        if (maxValue < minValue) maxValue = minValue;
        if (maxBackwardValue < 1) maxBackwardValue = 1;

        int max = maxValue;
        if (!isForward && restrictBackwardMax)
            max = Mathf.Min(max, maxBackwardValue);

        diceValue = Mathf.Clamp(diceValue, minValue, max);

        if (_mpb == null) _mpb = new MaterialPropertyBlock();
        EnableEmissionKeyword();
        ApplyVisuals();
    }
}
