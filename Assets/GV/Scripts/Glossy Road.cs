using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Procedurally builds a ribbon "road" mesh along a Unity SplineContainer.
/// Attach to the SAME GameObject that has SplineContainer.
/// Requires MeshFilter + MeshRenderer.
/// </summary>
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
    [Min(0.01f)] public float width = 6f;

    [Tooltip("Offsets the road along the spline up-vector (world space).")]
    public float verticalOffset = 0f;

    [Header("UVs")]
    [Tooltip("X = across width (0..1), Y = tiling per meter along length.")]
    public Vector2 uvTiling = new Vector2(1f, 0.25f);

    [Header("Options")]
    public bool doubleSided = false;

    [Tooltip("If enabled, rebuilds every frame while playing (useful if the spline moves).")]
    public bool liveUpdateInPlayMode = false;

    Mesh _mesh;
    MeshFilter _mf;
    SplineContainer _container;

    void Reset()
    {
        Ensure();
        Rebuild();
    }

    void OnEnable()
    {
        Ensure();
        Rebuild();
    }

    void OnDisable()
    {
#if UNITY_EDITOR
        // Clean up the generated mesh in edit mode to avoid leaks in the editor.
        if (!Application.isPlaying && _mesh != null)
        {
            DestroyImmediate(_mesh);
            _mesh = null;
        }
#endif
    }

    void OnValidate()
    {
        Ensure();
        Rebuild();
    }

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

        int vertCount = s * 2;
        var verts = new Vector3[vertCount];
        var normals = new Vector3[vertCount];
        var uvs = new Vector2[vertCount];

        int segCount = s - 1;
        int trisPerSeg = doubleSided ? 12 : 6;
        var tris = new int[segCount * trisPerSeg];

        float distance = 0f;

        Vector3 prevPWorld = Vector3.zero;
        Vector3 prevRight = Vector3.right;

        for (int i = 0; i < s; i++)
        {
            float t = (s == 1) ? 0f : (i / (float)(s - 1));

            // Fast path: get pos/tangent/up in one call. :contentReference[oaicite:1]{index=1}
            if (!_container.Evaluate(splineIndex, t, out float3 pos, out float3 tangent, out float3 up))
                continue;

            Vector3 p = (Vector3)pos;
            Vector3 tan = ((Vector3)tangent).normalized;
            Vector3 upv = ((Vector3)up).normalized;

            // Build a stable-ish frame: right = up x tangent
            Vector3 right = Vector3.Cross(upv, tan);
            if (right.sqrMagnitude < 1e-8f)
                right = Vector3.Cross(transform.up, tan);
            if (right.sqrMagnitude < 1e-8f)
                right = prevRight;

            right.Normalize();
            upv = Vector3.Cross(tan, right).normalized;
            prevRight = right;

            // Offset along up vector (world)
            p += upv * verticalOffset;

            if (i > 0) distance += Vector3.Distance(prevPWorld, p);
            prevPWorld = p;

            float halfW = width * 0.5f;

            // Store vertices in LOCAL space of this object
            int vi = i * 2;
            verts[vi + 0] = transform.InverseTransformPoint(p - right * halfW);
            verts[vi + 1] = transform.InverseTransformPoint(p + right * halfW);

            // Normals in LOCAL space
            Vector3 nLocal = transform.InverseTransformDirection(upv).normalized;
            normals[vi + 0] = nLocal;
            normals[vi + 1] = nLocal;

            float v = distance * uvTiling.y;
            uvs[vi + 0] = new Vector2(0f * uvTiling.x, v);
            uvs[vi + 1] = new Vector2(1f * uvTiling.x, v);
        }

        int ti = 0;
        for (int i = 0; i < segCount; i++)
        {
            int a = i * 2 + 0;
            int b = i * 2 + 1;
            int c = i * 2 + 2;
            int d = i * 2 + 3;

            // Top face
            tris[ti++] = a; tris[ti++] = c; tris[ti++] = b;
            tris[ti++] = b; tris[ti++] = c; tris[ti++] = d;

            if (doubleSided)
            {
                // Bottom face (reverse winding)
                tris[ti++] = a; tris[ti++] = b; tris[ti++] = c;
                tris[ti++] = b; tris[ti++] = d; tris[ti++] = c;
            }
        }

        EnsureMesh();
        _mesh.Clear(false);
        _mesh.vertices = verts;
        _mesh.normals = normals;
        _mesh.uv = uvs;
        _mesh.triangles = tris;
        _mesh.RecalculateBounds();

#if UNITY_EDITOR
        // Refresh scene view in edit mode
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

        _mesh = new Mesh
        {
            name = "SplineAerialRoadMesh (Generated)"
        };
        _mesh.MarkDynamic();

        // Don’t save this mesh as an asset; regenerate from spline.
        _mesh.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;

        // Use sharedMesh in edit mode, mesh in play mode to avoid instancing surprises.
        if (Application.isPlaying)
            _mf.mesh = _mesh;
        else
            _mf.sharedMesh = _mesh;
    }
}
