using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

[ExecuteAlways]
[RequireComponent(typeof(SplineContainer))]
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class SplineEnergyTrackGlow : MonoBehaviour
{
    [Header("Spline")]
    public int splineIndex = 0;

    [Header("Track Mesh (Ribbon)")]
    [Min(8)] public int samples = 256;
    [Min(0.01f)] public float width = 6f;

    [Tooltip("Offsets the ribbon along spline up-vector (in WORLD space).")]
    public float verticalOffset = 0f;

    public bool doubleSided = true;

    [Header("Material")]
    public Material trackMaterial;

    [Header("Glow Colors")]
    public Color baseGlowColor = Color.cyan;
    public Color highlightGlowColor = Color.magenta;
    [Min(0f)] public float emissionIntensity = 5f;
    public Vector2 emissionMaskTiling = new Vector2(10f, 1f);

    [Header("Dynamic Highlight Window (meters)")]
    public Transform aircraft;
    [Min(0f)] public float highlightLengthBehind = 40f;
    [Min(0f)] public float highlightLengthAhead = 5f;
    [Min(0f)] public float edgeSoftnessMeters = 2f;

    [Header("Highlight Window Offsets (meters)")]
    [Tooltip("Adds a distance offset to the START of the window. Positive moves the START forward (ahead), negative moves it backward (behind).")]
    public float startOffsetMeters = 0f;

    [Tooltip("Adds a distance offset to the END of the window. Positive moves the END forward (farther ahead), negative moves it backward.")]
    public float endOffsetMeters = 0f;

    [Header("Nearest Point Settings")]
    [Range(2, 32)] public int nearestResolution = 8;
    [Range(1, 8)] public int nearestIterations = 2;

    // Shader property IDs
    static readonly int BaseColorID = Shader.PropertyToID("_BaseColor");
    static readonly int PulseColorID = Shader.PropertyToID("_PulseColor");
    static readonly int IntensityID = Shader.PropertyToID("_Intensity");
    static readonly int MaskTilingID = Shader.PropertyToID("_MaskTiling");
    static readonly int WindowStartID = Shader.PropertyToID("_WindowStart");
    static readonly int WindowEndID = Shader.PropertyToID("_WindowEnd");
    static readonly int EdgeSoftnessID = Shader.PropertyToID("_EdgeSoftness");
    static readonly int IsLoopID = Shader.PropertyToID("_IsLoop");

    SplineContainer _container;
    MeshFilter _mf;
    MeshRenderer _mr;
    Mesh _mesh;
    Material _runtimeMat;

    float _smoothStartU, _smoothEndU;

    void OnEnable()
    {
        Cache();
        EnsureMaterial();
        RebuildMesh();
        PushStaticMaterialProps();
    }

    void OnDisable()
    {
        if (Application.isPlaying)
        {
            if (_runtimeMat != null) Destroy(_runtimeMat);
            if (_mesh != null) Destroy(_mesh);
        }
    }

    void OnValidate()
    {
        Cache();
        EnsureMaterial();
        RebuildMesh();
        PushStaticMaterialProps();
    }

    void Cache()
    {
        _container = GetComponent<SplineContainer>();
        _mf = GetComponent<MeshFilter>();
        _mr = GetComponent<MeshRenderer>();
    }

    void EnsureMaterial()
    {
        if (trackMaterial == null) return;

        if (Application.isPlaying)
        {
            if (_runtimeMat == null || _runtimeMat.shader != trackMaterial.shader)
            {
                _runtimeMat = new Material(trackMaterial);
                _runtimeMat.name = trackMaterial.name + " (Runtime)";
            }
            _mr.sharedMaterial = _runtimeMat;
        }
        else
        {
            _mr.sharedMaterial = trackMaterial;
        }
    }

    void PushStaticMaterialProps()
    {
        var mat = _mr != null ? _mr.sharedMaterial : null;
        if (mat == null) return;

        mat.SetColor(BaseColorID, baseGlowColor);
        mat.SetColor(PulseColorID, highlightGlowColor);
        mat.SetFloat(IntensityID, emissionIntensity);
        mat.SetVector(MaskTilingID, new Vector4(emissionMaskTiling.x, emissionMaskTiling.y, 0, 0));
    }

    public void RebuildMesh()
    {
        if (_container == null || _container.Splines == null || _container.Splines.Count == 0)
            return;

        splineIndex = Mathf.Clamp(splineIndex, 0, _container.Splines.Count - 1);

        if (_mesh == null)
        {
            _mesh = new Mesh { name = "SplineEnergyTrackMesh" };
            _mesh.MarkDynamic();
            _mf.sharedMesh = _mesh;
        }
        else
        {
            _mesh.Clear();
        }

        float totalLenWorld = Mathf.Max(0.0001f, _container.CalculateLength(splineIndex));

        int stride = doubleSided ? 4 : 2;
        int vertCount = samples * stride;

        var verts = new List<Vector3>(vertCount);
        var uvs = new List<Vector2>(vertCount);
        var normals = new List<Vector3>(vertCount);

        float accumWorld = 0f;
        Vector3 prevWorldPos = Vector3.zero;

        Transform tr = transform;

        for (int i = 0; i < samples; i++)
        {
            float t = (samples <= 1) ? 0f : (float)i / (samples - 1);

            if (!_container.Evaluate(splineIndex, t, out float3 pW3, out float3 tanW3, out float3 upW3))
                continue;

            Vector3 pWorld = (Vector3)pW3;
            Vector3 tanWorld = ((Vector3)tanW3).normalized;
            Vector3 upWorld = ((Vector3)upW3).normalized;

            pWorld += upWorld * verticalOffset;

            if (i > 0) accumWorld += Vector3.Distance(prevWorldPos, pWorld);
            float u = accumWorld / totalLenWorld;

            Vector3 pLocal = tr.InverseTransformPoint(pWorld);
            Vector3 tanLocal = tr.InverseTransformDirection(tanWorld).normalized;
            Vector3 upLocal = tr.InverseTransformDirection(upWorld).normalized;

            Vector3 rightLocal = Vector3.Cross(upLocal, tanLocal);
            if (rightLocal.sqrMagnitude < 1e-8f)
                rightLocal = Vector3.right;
            rightLocal.Normalize();

            Vector3 L = pLocal - rightLocal * (width * 0.5f);
            Vector3 R = pLocal + rightLocal * (width * 0.5f);

            // front
            verts.Add(L); verts.Add(R);
            uvs.Add(new Vector2(u, 0f)); uvs.Add(new Vector2(u, 1f));
            normals.Add(upLocal); normals.Add(upLocal);

            if (doubleSided)
            {
                // back
                verts.Add(R); verts.Add(L);
                uvs.Add(new Vector2(u, 0f)); uvs.Add(new Vector2(u, 1f));
                normals.Add(-upLocal); normals.Add(-upLocal);
            }

            prevWorldPos = pWorld;
        }

        var tris = new List<int>((samples - 1) * (doubleSided ? 12 : 6));
        for (int i = 0; i < samples - 1; i++)
        {
            int i0 = i * stride;
            int i1 = i0 + stride;

            // front quad
            tris.Add(i0 + 0); tris.Add(i1 + 0); tris.Add(i0 + 1);
            tris.Add(i0 + 1); tris.Add(i1 + 0); tris.Add(i1 + 1);

            if (doubleSided)
            {
                // back quad
                tris.Add(i0 + 2); tris.Add(i0 + 3); tris.Add(i1 + 2);
                tris.Add(i1 + 2); tris.Add(i0 + 3); tris.Add(i1 + 3);
            }
        }

        _mesh.SetVertices(verts);
        _mesh.SetNormals(normals);
        _mesh.SetUVs(0, uvs);
        _mesh.SetTriangles(tris, 0);
        _mesh.RecalculateBounds();
    }

    void Update()
    {
        PushStaticMaterialProps();

        var mat = _mr != null ? _mr.sharedMaterial : null;
        if (mat == null || aircraft == null || _container == null || _container.Splines.Count == 0)
            return;

        splineIndex = Mathf.Clamp(splineIndex, 0, _container.Splines.Count - 1);
        var spline = _container.Splines[splineIndex];
        bool isLoop = spline.Closed;

        float totalLenWorld = Mathf.Max(0.0001f, _container.CalculateLength(splineIndex));

        float3 localAircraft = (float3)transform.InverseTransformPoint(aircraft.position);
        SplineUtility.GetNearestPoint(spline, localAircraft, out _, out float tNorm, nearestResolution, nearestIterations);
        tNorm = Mathf.Clamp01(tNorm);

        // Approx distance along track (meters)
        float distApprox = tNorm * totalLenWorld;

        // NEW: apply independent offsets to start/end
        float startDist = distApprox - highlightLengthBehind + startOffsetMeters;
        float endDist   = distApprox + highlightLengthAhead + endOffsetMeters;

        if (isLoop)
        {
            startDist = Mod(startDist, totalLenWorld);
            endDist = Mod(endDist, totalLenWorld);
        }
        else
        {
            startDist = Mathf.Clamp(startDist, 0f, totalLenWorld);
            endDist = Mathf.Clamp(endDist, 0f, totalLenWorld);

            // Keep valid ordering on non-loop splines
            if (endDist < startDist) endDist = startDist;
        }

        float startU = startDist / totalLenWorld;
        float endU = endDist / totalLenWorld;
        float edgeU = Mathf.Clamp01(edgeSoftnessMeters / totalLenWorld);

        float lerp = Application.isPlaying ? (1f - Mathf.Exp(-12f * Time.deltaTime)) : 1f;
        _smoothStartU = Mathf.Lerp(_smoothStartU, startU, lerp);
        _smoothEndU = Mathf.Lerp(_smoothEndU, endU, lerp);

        mat.SetFloat(WindowStartID, _smoothStartU);
        mat.SetFloat(WindowEndID, _smoothEndU);
        mat.SetFloat(EdgeSoftnessID, edgeU);
        mat.SetFloat(IsLoopID, isLoop ? 1f : 0f);
    }

    static float Mod(float x, float m)
    {
        if (m <= 0f) return 0f;
        float r = x % m;
        if (r < 0f) r += m;
        return r;
    }
}
