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
    public Color tmpForwardGreen = Color.green;
    public Color tmpBackwardRed = Color.red;

    [Header("Dice Range")]
    [Min(1)] public int minValue = 1;
    [Min(1)] public int maxValue = 6;

    [Header("Backward (Minus) Restriction")]
    public bool restrictBackwardMax = true;
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

    [Header("TMP Face Camera (Auto)")]
    public bool faceCamera = true;
    [Tooltip("If true, rotates only around world Y (keeps text upright).")]
    public bool faceYawOnly = true;
    [Tooltip("Flip if text appears backwards.")]
    public bool invertFacing = false;

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
        FaceAllTMP();
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

    // Run AFTER other scripts so it doesn't get overwritten.
    void LateUpdate()
    {
        FaceAllTMP();
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
        string s = showSign ? ((isForward ? "+" : "-") + diceValue) : diceValue.ToString();

        Color ringBaseCol = isForward ? forwardGreen : backwardRed;
        Color tmpCol = isForward ? tmpForwardGreen : tmpBackwardRed;

        if (tmpTexts != null)
        {
            for (int i = 0; i < tmpTexts.Length; i++)
            {
                var t = tmpTexts[i];
                if (!t) continue;

                t.text = s;
                if (colorizeTMP) t.color = tmpCol;

#if UNITY_EDITOR
                t.ForceMeshUpdate();
#endif
            }
        }

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

    void FaceAllTMP()
    {
        if (!faceCamera || tmpTexts == null || tmpTexts.Length == 0) return;

        Camera cam = GetBestCamera();
        if (!cam) return;

        Vector3 camPos = cam.transform.position;

        for (int i = 0; i < tmpTexts.Length; i++)
        {
            var t = tmpTexts[i];
            if (!t) continue;

            Transform tr = t.transform;

            Vector3 dir = camPos - tr.position;   // text -> camera
            if (faceYawOnly) dir.y = 0f;

            if (dir.sqrMagnitude < 0.000001f) continue;

            Quaternion rot = Quaternion.LookRotation(dir.normalized, Vector3.up);
            if (invertFacing) rot *= Quaternion.Euler(0f, 180f, 0f);

            tr.rotation = rot;
        }
    }

    Camera GetBestCamera()
    {
        // 1) Play Mode: Main Camera if tagged
        if (Application.isPlaying)
        {
            var main = Camera.main;
            if (main && main.isActiveAndEnabled) return main;
        }

        // 2) Any enabled camera (highest depth wins)
        Camera best = null;
        float bestDepth = float.NegativeInfinity;

        int n = Camera.allCamerasCount;
        if (n > 0)
        {
            var cams = new Camera[n];
            Camera.GetAllCameras(cams);
            for (int i = 0; i < cams.Length; i++)
            {
                var c = cams[i];
                if (!c || !c.isActiveAndEnabled) continue;
                if (c.depth >= bestDepth)
                {
                    bestDepth = c.depth;
                    best = c;
                }
            }
            if (best) return best;
        }

        // 3) Edit Mode fallback: SceneView camera
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            var sv = UnityEditor.SceneView.lastActiveSceneView;
            if (sv != null && sv.camera != null) return sv.camera;
        }
#endif

        // 4) Last resort
        if (Camera.current) return Camera.current;
        return null;
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
        FaceAllTMP();
    }
}
