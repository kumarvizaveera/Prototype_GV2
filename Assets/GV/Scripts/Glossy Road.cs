using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Splines;
using Unity.Mathematics;

[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(SplineContainer))]
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class SplineAerialRoad : MonoBehaviour
{
    [Header("Spline")]
    public int splineIndex = 0;

    [Header("Road Mesh (Ribbon)")]
    [Min(8)] public int samples = 256;
    [Min(0.1f)] public float width = 8f;

    [Tooltip("Offsets the road along the spline up-vector (local space).")]
    public float verticalOffset = 0f;

    [Tooltip("If enabled, doubles triangles so it renders from below too.")]
    public bool doubleSided = true;

    [Tooltip("Update mesh in Edit Mode when you move spline points.")]
    public bool liveUpdateInEditMode = true;

    [Header("Material (Assigned to MeshRenderer)")]
    [Tooltip("Use URP Lit / HDRP Lit / Standard. Script will set metallic/smoothness + emission.")]
    public Material baseMaterial;

    [Tooltip("Create a unique material instance on this object (recommended).")]
    public bool materialInstancePerObject = true;

    [Header("Surface Look")]
    [Range(0f, 1f)] public float metallic = 0.9f;

    [Tooltip("0 = rough/matte, 1 = very glossy.")]
    [Range(0f, 1f)] public float gloss = 0.8f;

    [Tooltip("0 = sharp reflections, 1 = blurrier reflections (implemented by reducing smoothness).")]
    [Range(0f, 1f)] public float reflectionBlur = 0.35f;

    [Header("Glow (Emission)")]
    public bool glowEnabled = true;
    public Color glowColor = Color.cyan;
    [Min(0f)] public float glowIntensity = 3f;

    [Tooltip("Usually _EmissionColor.")]
    public string emissionColorProperty = "_EmissionColor";

    [Header("Rendering")]
    public bool castShadows = false;
    public bool receiveShadows = true;

    SplineContainer _container;
    MeshFilter _mf;
    MeshRenderer _mr;
    Mesh _mesh;
    Material _matInstance;

    void OnEnable()
    {
        Cache();
        EnsureMesh();
        EnsureMaterial();
        Rebuild();
        ApplyMaterialLook();
    }

    void OnDisable()
    {
        if (!Application.isPlaying)
        {
            if (_mesh) DestroyImmediate(_mesh);
            if (_matInstance) DestroyImmediate(_matInstance);
        }
    }

    void OnValidate()
    {
        Cache();
        EnsureMesh();
        EnsureMaterial();
        Rebuild();
        ApplyMaterialLook();
    }

    void Update()
    {
        if (!Application.isPlaying && liveUpdateInEditMode)
        {
            Rebuild();
            ApplyMaterialLook();
        }
    }

    void Cache()
    {
        if (!_container) _container = GetComponent<SplineContainer>();
        if (!_mf) _mf = GetComponent<MeshFilter>();
        if (!_mr) _mr = GetComponent<MeshRenderer>();
    }

    void EnsureMesh()
    {
        if (_mesh) return;

        _mesh = new Mesh { name = "SplineAerialRoad_Mesh" };
        _mesh.MarkDynamic();
        _mf.sharedMesh = _mesh;
    }

    void EnsureMaterial()
    {
        if (!_mr) return;

        if (baseMaterial == null)
        {
            // Fallback: try URP Lit, else Standard.
            var urp = Shader.Find("Universal Render Pipeline/Lit");
            var std = Shader.Find("Standard");
            var sh = urp != null ? urp : std;
            baseMaterial = new Material(sh) { name = "SplineAerialRoad_Mat" };
        }

        if (materialInstancePerObject)
        {
            if (_matInstance == null)
                _matInstance = new Material(baseMaterial) { name = baseMaterial.name + " (Instance)" };

            _mr.sharedMaterial = _matInstance;
        }
        else
        {
            _mr.sharedMaterial = baseMaterial;
        }

        _mr.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
        _mr.receiveShadows = receiveShadows;
    }

    void Rebuild()
    {
        if (_container == null || _container.Splines.Count == 0) return;
        splineIndex = Mathf.Clamp(splineIndex, 0, _container.Splines.Count - 1);

        var spline = _container.Splines[splineIndex];
        int segs = Mathf.Max(8, samples);

        int vertCount = (segs + 1) * 2;
        var verts = new Vector3[vertCount];
        var norms = new Vector3[vertCount];
        var uvs = new Vector2[vertCount];

        int triCount = segs * 6 * (doubleSided ? 2 : 1);
        var tris = new int[triCount];

        float halfW = width * 0.5f;

        for (int i = 0; i <= segs; i++)
        {
            float t = i / (float)segs;

            SplineUtility.Evaluate(spline, t, out float3 p3, out float3 tan3, out float3 up3);

            Vector3 p = (Vector3)p3;
            Vector3 forward = ((Vector3)tan3);
            Vector3 up = ((Vector3)up3);

            if (forward.sqrMagnitude < 1e-10f) forward = Vector3.forward;
            forward.Normalize();

            if (up.sqrMagnitude < 1e-10f) up = Vector3.up;
            up.Normalize();

            // Orthonormalize (handle near-parallel up/forward)
            if (Mathf.Abs(Vector3.Dot(up, forward)) > 0.99f)
                up = Vector3.up;

            Vector3 right = Vector3.Cross(up, forward).normalized;
            up = Vector3.Cross(forward, right).normalized;

            Vector3 center = p + up * verticalOffset;

            int vi = i * 2;
            verts[vi + 0] = center - right * halfW;
            verts[vi + 1] = center + right * halfW;

            norms[vi + 0] = up;
            norms[vi + 1] = up;

            uvs[vi + 0] = new Vector2(t, 0f);
            uvs[vi + 1] = new Vector2(t, 1f);
        }

        int ti = 0;
        for (int i = 0; i < segs; i++)
        {
            int i0 = i * 2;
            int i1 = i * 2 + 1;
            int i2 = (i + 1) * 2;
            int i3 = (i + 1) * 2 + 1;

            // Front face
            tris[ti++] = i0; tris[ti++] = i2; tris[ti++] = i1;
            tris[ti++] = i1; tris[ti++] = i2; tris[ti++] = i3;

            if (doubleSided)
            {
                // Back face (reversed winding)
                tris[ti++] = i1; tris[ti++] = i2; tris[ti++] = i0;
                tris[ti++] = i3; tris[ti++] = i2; tris[ti++] = i1;
            }
        }

        _mesh.Clear(false);
        _mesh.vertices = verts;
        _mesh.normals = norms;
        _mesh.uv = uvs;
        _mesh.triangles = tris;
        _mesh.RecalculateBounds();
        _mesh.RecalculateTangents();
    }

    void ApplyMaterialLook()
    {
        if (_mr == null || _mr.sharedMaterial == null) return;
        var m = _mr.sharedMaterial;

        // "Glossy, reflective, blurry":
        // - reflective is mainly metallic + environment reflections/SSR
        // - "blurry" is mainly lower smoothness (higher roughness)
        float finalSmoothness = Mathf.Clamp01(gloss * Mathf.Lerp(1f, 0.25f, reflectionBlur));

        // URP/HDRP Lit
        SetFloatIfExists(m, "_Metallic", metallic);

        // URP smoothness
        if (!SetFloatIfExists(m, "_Smoothness", finalSmoothness))
        {
            // Standard shader smoothness
            SetFloatIfExists(m, "_Glossiness", finalSmoothness);
        }

        // Emission toggle
        if (glowEnabled)
        {
            m.EnableKeyword("_EMISSION");
            SetColorIfExists(m, emissionColorProperty, glowColor * glowIntensity);
        }
        else
        {
            SetColorIfExists(m, emissionColorProperty, Color.black);
            m.DisableKeyword("_EMISSION");
        }
    }

    static bool SetFloatIfExists(Material m, string prop, float v)
    {
        if (m != null && m.HasProperty(prop))
        {
            m.SetFloat(prop, v);
            return true;
        }
        return false;
    }

    static bool SetColorIfExists(Material m, string prop, Color c)
    {
        if (m != null && m.HasProperty(prop))
        {
            m.SetColor(prop, c);
            return true;
        }
        return false;
    }

    // Optional: call from other scripts/UI
    public void SetGlow(bool enabled)
    {
        glowEnabled = enabled;
        ApplyMaterialLook();
    }
}
