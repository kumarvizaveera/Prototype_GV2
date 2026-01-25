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

    [System.Serializable]
    public struct WeightedOutcome
    {
        public int value;
        public bool isForward;
        [Range(0f, 100f)] public float probability;
    }

    [Header("Weighted Dice Outcomes")]
    public WeightedOutcome[] outcomes = new WeightedOutcome[]
    {
        new WeightedOutcome { value = 4, isForward = true, probability = 35f },
        new WeightedOutcome { value = 8, isForward = true, probability = 25f },
        new WeightedOutcome { value = 12, isForward = true, probability = 10f },
        new WeightedOutcome { value = 4, isForward = false, probability = 20f },
        new WeightedOutcome { value = 8, isForward = false, probability = 10f },
    };

    [Header("Visual (Emission)")]
    public Renderer[] glowRenderers;
    public Color forwardGreen = Color.green;
    public Color backwardRed = Color.red;
    [Min(0f)] public float emissionIntensity = 2.5f;
    public string emissionColorProperty = "_EmissionColor";

    [Header("Proximity Emission Boost (Crystal)")]
    [Tooltip("If enabled, emission intensity increases as the target gets closer.")]
    public bool proximityBoost = false;

    [Tooltip("Target to measure distance from (aircraft). Leave empty to auto-find by tag.")]
    public Transform proximityTarget;

    [Tooltip("Auto-find target by tag when 'proximityTarget' is empty.")]
    public bool autoFindTargetByTag = true;

    [Tooltip("Only auto-find in Edit Mode if you explicitly allow it.")]
    public bool findTargetInEditMode = false;

    [Tooltip("Tag used to find the target at runtime (e.g., Player).")]
    public string proximityTargetTag = "Player";

    [Tooltip("Distance where boost is strongest.")]
    [Min(0.01f)] public float nearDistance = 2f;

    [Tooltip("Distance where boost is 1x (no boost).")]
    [Min(0.01f)] public float farDistance = 30f;

    [Tooltip("Maximum multiplier applied to 'emissionIntensity' at/inside nearDistance.")]
    [Min(1f)] public float maxMultiplier = 4f;

    [Tooltip("How quickly intensity responds to distance changes.")]
    [Min(0f)] public float responseSpeed = 10f;

    [Tooltip("Optional: If assigned, only these renderers get proximity boost. If empty, uses glowRenderers.")]
    public Renderer[] proximityBoostRenderers;

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

    float _proximityMul = 1f;
    float _lastAppliedIntensity = -9999f;
    Color _lastAppliedBaseCol = new Color(999, 999, 999, 999);

    public bool IsForward => isForward;
    public int DiceValue => diceValue;

    void OnEnable()
    {
        if (_mpb == null) _mpb = new MaterialPropertyBlock();
        EnableEmissionKeyword();

        TryAutoFindTarget();

        if (Application.isPlaying && rollOnStart) Roll();
        ApplyVisuals(force: true);
        FaceAllTMP();
    }

    void Update()
    {
        // Keep these running in both modes so the visuals stay correct.
        TryAutoFindTarget();
        UpdateProximityMultiplier();

        if (!Application.isPlaying)
        {
            ApplyEmissionOnlyIfChanged();
            ApplyTMP();
            return;
        }

        if (autoRoll)
        {
            _t += Time.deltaTime;
            if (_t >= rollInterval)
            {
                _t = 0f;
                Roll(); // Roll applies visuals
                return;
            }
        }

        ApplyEmissionOnlyIfChanged();
    }

    void LateUpdate()
    {
        FaceAllTMP();
    }

    [ContextMenu("Roll Now")]
    public void Roll()
    {
        if (outcomes == null || outcomes.Length == 0) return;

        float totalWeight = 0f;
        for (int i = 0; i < outcomes.Length; i++) totalWeight += outcomes[i].probability;

        float rnd = Random.Range(0f, totalWeight);
        float current = 0f;

        for (int i = 0; i < outcomes.Length; i++)
        {
            current += outcomes[i].probability;
            if (rnd <= current)
            {
                isForward = outcomes[i].isForward;
                diceValue = outcomes[i].value;
                break;
            }
        }

        ApplyVisuals(force: true);
    }

    public void SetState(bool forward, int value)
    {
        isForward = forward;
        diceValue = value; // Direct assignment; validation is up to caller or we rely on it being correct visuals
        ApplyVisuals(force: true);
    }

    // Runtime-safe assignment (useful if you spawn aircraft / additive scenes)
    public void SetProximityTarget(Transform target)
    {
        proximityTarget = target;
    }

    void ApplyVisuals(bool force)
    {
        ApplyTMP();
        ApplyEmission(force: force);
    }

    void ApplyTMP()
    {
        string s = showSign ? ((isForward ? "+" : "-") + diceValue) : diceValue.ToString();
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
    }

    void ApplyEmissionOnlyIfChanged()
    {
        Color baseCol = isForward ? forwardGreen : backwardRed;
        float intensityNow = GetCurrentIntensity();

        if (Mathf.Abs(intensityNow - _lastAppliedIntensity) < 0.0001f &&
            baseCol == _lastAppliedBaseCol)
            return;

        ApplyEmission(force: true);
    }

    void ApplyEmission(bool force)
    {
        Color ringBaseCol = isForward ? forwardGreen : backwardRed;
        float intensity = GetCurrentIntensity();

        _lastAppliedIntensity = intensity;
        _lastAppliedBaseCol = ringBaseCol;

        Color emissionCol = ringBaseCol * intensity;

        if (_mpb == null) _mpb = new MaterialPropertyBlock();

        var rs = GetEmissionRenderers();
        if (rs == null) return;

        for (int i = 0; i < rs.Length; i++)
        {
            var r = rs[i];
            if (!r) continue;

            r.GetPropertyBlock(_mpb);
            _mpb.SetColor(emissionColorProperty, emissionCol);
            r.SetPropertyBlock(_mpb);
        }
    }

    Renderer[] GetEmissionRenderers()
    {
        if (proximityBoostRenderers != null && proximityBoostRenderers.Length > 0)
            return proximityBoostRenderers;

        return glowRenderers;
    }

    float GetCurrentIntensity()
    {
        if (!proximityBoost) return emissionIntensity;
        return emissionIntensity * Mathf.Max(1f, _proximityMul);
    }

    void UpdateProximityMultiplier()
    {
        if (!proximityBoost)
        {
            _proximityMul = 1f;
            return;
        }

        if (!proximityTarget)
        {
            _proximityMul = 1f;
            return;
        }

        float nd = Mathf.Max(0.01f, nearDistance);
        float fd = Mathf.Max(nd, farDistance);

        float dist = Vector3.Distance(transform.position, proximityTarget.position);

        // t = 1 when near/inside, 0 when far/outside
        float t = Mathf.InverseLerp(fd, nd, dist);
        float targetMul = Mathf.Lerp(1f, Mathf.Max(1f, maxMultiplier), t);

        float dt = Application.isPlaying ? Time.deltaTime : 0.016f;
        float k = 1f - Mathf.Exp(-responseSpeed * dt);
        _proximityMul = Mathf.Lerp(_proximityMul, targetMul, k);
    }

    void TryAutoFindTarget()
    {
        if (proximityTarget != null) return;
        if (!autoFindTargetByTag) return;
        if (string.IsNullOrEmpty(proximityTargetTag)) return;

        // Avoid cross-scene / prefab-stage serialization problems in edit mode by default.
        if (!Application.isPlaying && !findTargetInEditMode) return;

        GameObject go = null;
        try { go = GameObject.FindGameObjectWithTag(proximityTargetTag); }
        catch { /* tag might not exist */ }

        if (go) proximityTarget = go.transform;
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

            Vector3 dir = camPos - tr.position; // text -> camera
            if (faceYawOnly) dir.y = 0f;

            if (dir.sqrMagnitude < 0.000001f) continue;

            Quaternion rot = Quaternion.LookRotation(dir.normalized, Vector3.up);
            if (invertFacing) rot *= Quaternion.Euler(0f, 180f, 0f);

            tr.rotation = rot;
        }
    }

    Camera GetBestCamera()
    {
        if (Application.isPlaying)
        {
            var main = Camera.main;
            if (main && main.isActiveAndEnabled) return main;
        }

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

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            var sv = UnityEditor.SceneView.lastActiveSceneView;
            if (sv != null && sv.camera != null) return sv.camera;
        }
#endif

        if (Camera.current) return Camera.current;
        return null;
    }

    void EnableEmissionKeyword()
    {
        var rs = GetEmissionRenderers();
        if (rs == null) return;

        for (int i = 0; i < rs.Length; i++)
        {
            var r = rs[i];
            if (!r || r.sharedMaterial == null) continue;
            r.sharedMaterial.EnableKeyword("_EMISSION");
        }
    }

    void OnValidate()
    {
        if (outcomes == null || outcomes.Length == 0)
        {
            // Fallback default
            outcomes = new WeightedOutcome[]
            {
                new WeightedOutcome { value = 4, isForward = true, probability = 100f }
            };
        }

        if (nearDistance < 0.01f) nearDistance = 0.01f;
        if (farDistance < nearDistance) farDistance = nearDistance;

        if (_mpb == null) _mpb = new MaterialPropertyBlock();
        EnableEmissionKeyword();

        TryAutoFindTarget();
        UpdateProximityMultiplier();

        ApplyVisuals(force: true);
        FaceAllTMP();
    }
}
