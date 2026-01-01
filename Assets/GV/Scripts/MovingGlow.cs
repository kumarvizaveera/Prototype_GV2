using UnityEngine;
using UnityEngine.Splines;

[ExecuteAlways]
[DisallowMultipleComponent]
public class SplineEmissionWindowTexture : MonoBehaviour
{
    [Header("Renderer / Material")]
    public MeshRenderer roadRenderer;

    [Tooltip("If empty, uses roadRenderer.sharedMaterial.")]
    public Material targetMaterial;

    [Tooltip("Use MaterialPropertyBlock (recommended).")]
    public bool usePropertyBlock = true;

    [Header("Spline (for finding u along track)")]
    public SplineContainer splineContainer;
    public int splineIndex = 0;

    [Header("Aircraft")]
    public Transform aircraft;

    [Header("Window Distances (meters along road)")]
    [Min(0f)] public float aheadMeters = 25f;
    [Min(0f)] public float behindMeters = 10f;
    public float centerOffsetMeters = 0f;
    [Min(0f)] public float featherMeters = 2f;

    [Header("Emission Colors")]
    public Color baseGlow = Color.cyan;
    public Color highlightGlow = Color.magenta;

    [Tooltip("Overall emission brightness multiplier (can be > 1).")]
    [Min(0f)] public float intensity = 5f;

    [Header("Emission Texture")]
    [Range(64, 4096)] public int resolution = 512;

    [Tooltip("Update only if aircraft moves enough along the track (0..1).")]
    [Range(0f, 0.02f)] public float uUpdateThreshold = 0.0015f;

    [Header("Shader Property Names (URP Lit defaults)")]
    public string emissionMapProp = "_EmissionMap";
    public string emissionColorProp = "_EmissionColor";

    Texture2D _tex;
    Color[] _pixels;
    MaterialPropertyBlock _mpb;

    // Cached polyline for nearest-u search (built from spline evaluation)
    Vector3[] _centersLocal;
    float[] _cumLen;
    float _totalLen;

    float _lastU = -999f;

    void OnEnable()
    {
        if (_mpb == null) _mpb = new MaterialPropertyBlock();
        EnsureRefs();
        BuildPolylineCache();
        EnsureTexture();
        ForceEnableEmissionKeyword();
        PushTexture(true);
    }

    void OnValidate()
    {
        resolution = Mathf.Clamp(resolution, 64, 4096);
        EnsureRefs();
        BuildPolylineCache();
        EnsureTexture();
        ForceEnableEmissionKeyword();
        PushTexture(true);
    }

    void Update()
    {
        EnsureRefs();
        if (roadRenderer == null) return;

        // Rebuild cache if spline count changed / index out of range
        if (splineContainer != null && splineContainer.Splines.Count > 0)
            splineIndex = Mathf.Clamp(splineIndex, 0, splineContainer.Splines.Count - 1);

        // If no spline or aircraft, keep base emission
        if (splineContainer == null || splineContainer.Splines.Count == 0 || aircraft == null)
        {
            PushTexture(false);
            return;
        }

        BuildPolylineCacheIfNeeded();

        float u = FindNearestU01(aircraft.position);
        if (_lastU < -1f || Mathf.Abs(u - _lastU) >= uUpdateThreshold)
        {
            _lastU = u;
            PushTexture(true);
        }
    }

    void EnsureRefs()
    {
        if (roadRenderer == null) roadRenderer = GetComponent<MeshRenderer>();
        if (splineContainer == null) splineContainer = GetComponent<SplineContainer>();
        if (_mpb == null) _mpb = new MaterialPropertyBlock();
    }

    void EnsureTexture()
    {
        if (_tex != null && _tex.width == resolution) return;

        // HDR-capable texture so emission can exceed 1.0
        _tex = new Texture2D(resolution, 1, TextureFormat.RGBAHalf, false, true);
        _tex.name = "EmissionWindow_1D";
        _tex.wrapMode = TextureWrapMode.Repeat;   // for closed splines (repeat is fine; we clamp in logic for open)
        _tex.filterMode = FilterMode.Bilinear;

        _pixels = new Color[resolution];
    }

    void ForceEnableEmissionKeyword()
    {
        var mat = GetMaterial();
        if (mat == null) return;

        // Works for URP Lit and Standard
        mat.EnableKeyword("_EMISSION");

        // Set a sane emission color multiplier (we bake colors into texture)
        if (mat.HasProperty(emissionColorProp))
            mat.SetColor(emissionColorProp, Color.white);
    }

    Material GetMaterial()
    {
        if (targetMaterial != null) return targetMaterial;
        if (roadRenderer != null) return roadRenderer.sharedMaterial;
        return null;
    }

    void BuildPolylineCacheIfNeeded()
    {
        // Simple heuristic: if cache missing, rebuild
        if (_centersLocal == null || _centersLocal.Length < 16 || _cumLen == null) BuildPolylineCache();
    }

    void BuildPolylineCache()
    {
        _centersLocal = null;
        _cumLen = null;
        _totalLen = 0f;

        if (splineContainer == null || splineContainer.Splines.Count == 0) return;

        var spline = splineContainer.Splines[splineIndex];
        if (spline == null) return;

        // Use resolution-ish samples for decent nearest-u accuracy
        int samples = Mathf.Max(128, resolution);
        _centersLocal = new Vector3[samples + 1];
        _cumLen = new float[samples + 1];

        Vector3 prev = Vector3.zero;
        for (int i = 0; i <= samples; i++)
        {
            float t = i / (float)samples;
            Unity.Mathematics.float3 p, tg, up;
            SplineUtility.Evaluate(spline, t, out p, out tg, out up);

            Vector3 posLocal = (Vector3)p;
            _centersLocal[i] = posLocal;

            if (i == 0) _cumLen[i] = 0f;
            else
            {
                _totalLen += Vector3.Distance(prev, posLocal);
                _cumLen[i] = _totalLen;
            }
            prev = posLocal;
        }

        _totalLen = Mathf.Max(_totalLen, 0.0001f);
    }

    float FindNearestU01(Vector3 worldPos)
    {
        // Find nearest point on cached polyline in LOCAL space of splineContainer
        Vector3 pLocal = splineContainer.transform.InverseTransformPoint(worldPos);

        float bestD2 = float.PositiveInfinity;
        float bestU01 = 0f;

        for (int i = 0; i < _centersLocal.Length - 1; i++)
        {
            Vector3 a = _centersLocal[i];
            Vector3 b = _centersLocal[i + 1];
            Vector3 ab = b - a;
            float ab2 = ab.sqrMagnitude;
            if (ab2 < 1e-8f) continue;

            float s = Vector3.Dot(pLocal - a, ab) / ab2;
            s = Mathf.Clamp01(s);

            Vector3 q = a + ab * s;
            float d2 = (pLocal - q).sqrMagnitude;

            if (d2 < bestD2)
            {
                bestD2 = d2;
                float segLen = _cumLen[i + 1] - _cumLen[i];
                float lenAt = _cumLen[i] + segLen * s;
                bestU01 = lenAt / _totalLen;
            }
        }

        bool closed = splineContainer.Splines[splineIndex].Closed;
        return closed ? Mathf.Repeat(bestU01, 1f) : Mathf.Clamp01(bestU01);
    }

    void PushTexture(bool animatedWindow)
    {
        EnsureTexture();

        var mat = GetMaterial();
        if (mat == null || roadRenderer == null) return;

        bool closed = false;
        if (splineContainer != null && splineContainer.Splines.Count > 0)
            closed = splineContainer.Splines[splineIndex].Closed;

        // If we can't animate (no aircraft/spline), fill with base glow only
        float centerU = (animatedWindow ? _lastU : 0f);
        float totalLen = Mathf.Max(_totalLen, 0.0001f);

        float ahead01 = Mathf.Clamp01(aheadMeters / totalLen);
        float behind01 = Mathf.Clamp01(behindMeters / totalLen);
        float feather01 = Mathf.Clamp01(featherMeters / totalLen);

        // Apply center offset in u-space
        if (animatedWindow)
        {
            centerU += (centerOffsetMeters / totalLen);
            centerU = closed ? Mathf.Repeat(centerU, 1f) : Mathf.Clamp01(centerU);
        }

        float start = centerU - behind01;
        float end = centerU + ahead01;

        // Precompute linear HDR colors
        Color baseC = baseGlow.linear * intensity;
        Color hiC = highlightGlow.linear * intensity;

        for (int i = 0; i < resolution; i++)
        {
            float u = (resolution == 1) ? 0f : (i / (float)(resolution - 1));

            float m = 0f;
            if (animatedWindow)
            {
                if (!closed)
                {
                    float s = Mathf.Clamp01(start);
                    float e = Mathf.Clamp01(end);
                    m = WindowMask(u, s, e, feather01);
                }
                else
                {
                    // Wrap support: if start/end cross boundary, treat as two intervals
                    float s = start;
                    float e = end;

                    if ((behind01 + ahead01) >= 1f)
                    {
                        m = 1f;
                    }
                    else
                    {
                        if (s >= 0f && e <= 1f)
                        {
                            m = WindowMask(u, s, e, feather01);
                        }
                        else
                        {
                            float eA = Mathf.Repeat(e, 1f);      // [0..eA]
                            float sB = Mathf.Repeat(s, 1f);      // [sB..1]
                            float mA = WindowMask(u, 0f, eA, feather01);
                            float mB = WindowMask(u, sB, 1f, feather01);
                            m = Mathf.Max(mA, mB);
                        }
                    }
                }
            }

            // Lerp base->highlight by mask
            _pixels[i] = Color.Lerp(baseC, hiC, m);
        }

        _tex.SetPixels(_pixels);
        _tex.Apply(false, false);

        if (usePropertyBlock)
        {
            roadRenderer.GetPropertyBlock(_mpb);
            _mpb.SetTexture(emissionMapProp, _tex);
            _mpb.SetColor(emissionColorProp, Color.white); // multiply = 1
            roadRenderer.SetPropertyBlock(_mpb);
        }
        else
        {
            if (mat.HasProperty(emissionMapProp)) mat.SetTexture(emissionMapProp, _tex);
            if (mat.HasProperty(emissionColorProp)) mat.SetColor(emissionColorProp, Color.white);
        }
    }

    static float WindowMask(float u, float start, float end, float feather)
    {
        // Assumes start <= end and both in [0..1]
        if (end <= start) return 0f;

        if (feather <= 0f)
            return (u >= start && u <= end) ? 1f : 0f;

        float inEdge = Smooth01((u - start) / feather);
        float outEdge = 1f - Smooth01((u - end) / feather);
        return Mathf.Clamp01(inEdge * outEdge);
    }

    static float Smooth01(float x)
    {
        x = Mathf.Clamp01(x);
        return x * x * (3f - 2f * x); // smoothstep
    }
}
