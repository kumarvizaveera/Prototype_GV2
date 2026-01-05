using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;
using UnityEngine.Rendering;

#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
[RequireComponent(typeof(SplineContainer))]
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class SplineAerialRoadMesh : MonoBehaviour
{
    [Header("Spline")]
    [Min(0)] public int splineIndex = 0;

    [Header("Shape")]
    [Min(8)] public int samples = 256;

    [Tooltip("For tunnel: this is the DIAMETER (radius = width * 0.5).")]
    [Min(0.01f)] public float width = 6f;

    [Tooltip("Offsets the tunnel center along the spline up-vector (world space).")]
    public float verticalOffset = 0f;

    [Header("Tunnel")]
    [Min(3)] public int radialSegments = 24;

    [Tooltip("If true, the tunnel is visible from INSIDE (triangles face inward).")]
    public bool inwardFacing = true;

    [Header("UVs")]
    [Tooltip("X = around circumference (0..1), Y = tiling per meter along length.")]
    public Vector2 uvTiling = new Vector2(1f, 0.25f);

    [Header("Options")]
    public bool doubleSided = false;

    [Tooltip("If enabled, rebuilds every frame while playing (useful if the spline moves).")]
    public bool liveUpdateInPlayMode = false;

    [Header("Local Mesh Scale")]
    [Tooltip("Scales the GENERATED mesh per-axis around EACH ring center (keeps alignment to spline).")]
    public Vector3 meshScale = Vector3.one;

    Mesh _mesh;
    MeshFilter _mf;
    SplineContainer _container;

    void Reset() { Ensure(); Rebuild(); }
    void OnEnable() { Ensure(); Rebuild(); }

    void OnDisable()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying && _mesh != null)
        {
            DestroyImmediate(_mesh);
            _mesh = null;
        }
#endif
    }

    void OnValidate() { Ensure(); Rebuild(); }

    void Update()
    {
        if (Application.isPlaying && liveUpdateInPlayMode)
            Rebuild();
    }

    [ContextMenu("Rebuild Road Mesh")]
    public void Rebuild()
    {
        Ensure();

        if (_container == null || _container.Splines == null || _container.Splines.Count == 0)
            return;

        splineIndex = Mathf.Clamp(splineIndex, 0, _container.Splines.Count - 1);

        int s = Mathf.Max(samples, 2);
        int rSeg = Mathf.Max(radialSegments, 3);
        int ringVertCount = rSeg + 1; // seam vertex
        float radius = width * 0.5f;

        int vertCount = s * ringVertCount;
        var verts = new Vector3[vertCount];
        var normals = new Vector3[vertCount];
        var uvs = new Vector2[vertCount];

        int segCount = s - 1;
        int quadCount = segCount * rSeg;
        int trisPerQuad = doubleSided ? 12 : 6;
        var tris = new int[quadCount * trisPerQuad];

        float distance = 0f;
        Vector3 prevPWorld = Vector3.zero;
        Vector3 prevRight = Vector3.right;

        // For normal correction under non-uniform scale
        Vector3 invS = new Vector3(
            meshScale.x != 0f ? 1f / meshScale.x : 0f,
            meshScale.y != 0f ? 1f / meshScale.y : 0f,
            meshScale.z != 0f ? 1f / meshScale.z : 0f
        );

        for (int i = 0; i < s; i++)
        {
            float t = (s == 1) ? 0f : (i / (float)(s - 1));

            if (!_container.Evaluate(splineIndex, t, out float3 pos, out float3 tangent, out float3 up))
                continue;

            Vector3 p = (Vector3)pos;
            Vector3 tan = ((Vector3)tangent).normalized;
            Vector3 upv = ((Vector3)up).normalized;

            Vector3 right = Vector3.Cross(upv, tan);
            if (right.sqrMagnitude < 1e-8f)
                right = Vector3.Cross(transform.up, tan);
            if (right.sqrMagnitude < 1e-8f)
                right = prevRight;

            right.Normalize();
            upv = Vector3.Cross(tan, right).normalized;
            prevRight = right;

            p += upv * verticalOffset;

            if (i > 0) distance += Vector3.Distance(prevPWorld, p);
            prevPWorld = p;

            float v = distance * uvTiling.y;

            // Ring center in LOCAL space (this is what must stay aligned to spline)
            Vector3 centerLocal = transform.InverseTransformPoint(p);

            // Ring frame axes in LOCAL space
            Vector3 rightLocal = transform.InverseTransformDirection(right).normalized;
            Vector3 upLocal = transform.InverseTransformDirection(upv).normalized;

            int baseVi = i * ringVertCount;

            for (int j = 0; j <= rSeg; j++)
            {
                float u01 = j / (float)rSeg;
                float ang = u01 * Mathf.PI * 2f;

                // Offset from center, expressed in LOCAL space ring frame
                Vector3 dirLocal = (Mathf.Cos(ang) * rightLocal) + (Mathf.Sin(ang) * upLocal);
                Vector3 offsetLocal = dirLocal * radius;

                // Scale ONLY the offset (keeps center pinned to spline)
                offsetLocal = Vector3.Scale(offsetLocal, meshScale);

                int vi = baseVi + j;
                verts[vi] = centerLocal + offsetLocal;

                // Normal (start from unscaled direction, then correct for non-uniform scale)
                Vector3 nLocal = inwardFacing ? -dirLocal : dirLocal;
                nLocal = Vector3.Scale(nLocal, invS).normalized;
                normals[vi] = nLocal;

                uvs[vi] = new Vector2(u01 * uvTiling.x, v);
            }
        }

        int ti = 0;
        for (int i = 0; i < segCount; i++)
        {
            int ring0 = i * ringVertCount;
            int ring1 = (i + 1) * ringVertCount;

            for (int j = 0; j < rSeg; j++)
            {
                int a = ring0 + j;
                int b = ring0 + j + 1;
                int c = ring1 + j;
                int d = ring1 + j + 1;

                if (inwardFacing)
                {
                    tris[ti++] = a; tris[ti++] = c; tris[ti++] = b;
                    tris[ti++] = b; tris[ti++] = c; tris[ti++] = d;
                }
                else
                {
                    tris[ti++] = a; tris[ti++] = b; tris[ti++] = c;
                    tris[ti++] = b; tris[ti++] = d; tris[ti++] = c;
                }

                if (doubleSided)
                {
                    if (inwardFacing)
                    {
                        tris[ti++] = a; tris[ti++] = b; tris[ti++] = c;
                        tris[ti++] = b; tris[ti++] = d; tris[ti++] = c;
                    }
                    else
                    {
                        tris[ti++] = a; tris[ti++] = c; tris[ti++] = b;
                        tris[ti++] = b; tris[ti++] = c; tris[ti++] = d;
                    }
                }
            }
        }

        EnsureMesh();
        _mesh.Clear(false);

        if (verts.Length > 65000)
            _mesh.indexFormat = IndexFormat.UInt32;

        _mesh.vertices = verts;
        _mesh.normals = normals;
        _mesh.uv = uvs;
        _mesh.triangles = tris;
        _mesh.RecalculateBounds();

#if UNITY_EDITOR
        if (!Application.isPlaying)
            SceneView.RepaintAll();
#endif
    }

    void Ensure()
    {
        if (_container == null) _container = GetComponent<SplineContainer>();
        if (_mf == null) _mf = GetComponent<MeshFilter>();
        EnsureMesh();
    }

    void EnsureMesh()
    {
        if (_mesh != null) return;

        _mesh = new Mesh { name = "SplineAerialRoadMesh (Generated)" };
        _mesh.MarkDynamic();
        _mesh.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;

        if (Application.isPlaying) _mf.mesh = _mesh;
        else _mf.sharedMesh = _mesh;
    }
}
