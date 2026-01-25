// GyroRingCapsuleColliderSetup.cs
// Single-script solution: put this on the "Gyro" root.
// It will auto-find "Outer Pivot", "Middle Pivot", "Inner Pivot" (or you can assign manually),
// then generate optimized CapsuleCollider trigger segments around each ring.
//
// Usage:
// 1) Add this script to Gyro root.
// 2) In Inspector, set ringRadius/tubeRadius for Outer/Middle/Inner.
// 3) Component (⋮/gear) -> "Rebuild All Ring Colliders".
//
// Trigger note: Your aircraft must have a Rigidbody for OnTriggerEnter to fire reliably.

using UnityEngine;

[ExecuteAlways]
public class GyroRingCapsuleColliderSetup : MonoBehaviour
{
    public enum Axis { X, Y, Z, NegX, NegY, NegZ }

    [System.Serializable]
    public class Ring
    {
        [Header("Target")]
        public string pivotName = "Outer Pivot";
        public Transform pivot;

        [Header("Shape")]
        [Min(3)] public int segments = 16;                 // 12–24 typical
        [Min(0.01f)] public float ringRadius = 5f;         // center -> tube center
        [Min(0.005f)] public float tubeRadius = 0.25f;     // thickness/2
        [Min(0f)] public float heightPadding = 0.02f;

        [Header("Orientation (Pivot Local Axes)")]
        public Axis planeNormalAxis = Axis.Z;              // perpendicular to ring plane
        public Axis radialStartAxis = Axis.X;              // angle 0 direction in ring plane

        [Header("Collider")]
        public bool isTrigger = true;
        public PhysicsMaterial physicMaterial = null;

        [Header("Layer")]
        public bool inheritPivotLayer = true;
        public int collidersLayer = 0;

        [Header("Generated Root Name")]
        public string generatedRootName = "__RingCapsules";
    }

    [Header("Auto-Find Pivots By Name")]
    public bool autoFindPivots = true;

    [Header("Rings (3 for your gyro)")]
    public Ring outer = new Ring { pivotName = "Outer Pivot", generatedRootName = "__OuterRingCapsules" };
    public Ring middle = new Ring { pivotName = "Middle Pivot", generatedRootName = "__MiddleRingCapsules" };
    public Ring inner = new Ring { pivotName = "Inner Pivot", generatedRootName = "__InnerRingCapsules" };

    [Header("Editor Convenience")]
    public bool autoRebuildInEditor = false;

    void OnValidate()
    {
        if (autoFindPivots) AutoFind();
        if (autoRebuildInEditor) RebuildAll();
    }

    [ContextMenu("Auto-Find Pivots")]
    public void AutoFind()
    {
        outer.pivot = outer.pivot != null ? outer.pivot : FindDeepChild(transform, outer.pivotName);
        middle.pivot = middle.pivot != null ? middle.pivot : FindDeepChild(transform, middle.pivotName);
        inner.pivot = inner.pivot != null ? inner.pivot : FindDeepChild(transform, inner.pivotName);
    }

    [ContextMenu("Rebuild All Ring Colliders")]
    public void RebuildAll()
    {
        BuildRing(outer);
        BuildRing(middle);
        BuildRing(inner);
    }

    [ContextMenu("Remove All Generated Ring Colliders")]
    public void RemoveAll()
    {
        RemoveRing(outer);
        RemoveRing(middle);
        RemoveRing(inner);
    }

    void BuildRing(Ring r)
    {
        if (r.pivot == null) return;

        r.segments = Mathf.Max(3, r.segments);

        // Build an orthonormal basis in the PIVOT'S LOCAL space:
        Vector3 n = AxisToVector(r.planeNormalAxis).normalized;   // normal
        Vector3 a = AxisToVector(r.radialStartAxis).normalized;   // in-plane start axis

        // If a is too parallel to n, pick a safe fallback
        if (Mathf.Abs(Vector3.Dot(n, a)) > 0.99f)
        {
            a = Vector3.Cross(n, Vector3.up);
            if (a.sqrMagnitude < 1e-6f) a = Vector3.Cross(n, Vector3.right);
            a.Normalize();
        }

        Vector3 b = Vector3.Cross(n, a).normalized;
        a = Vector3.Cross(b, n).normalized;

        // Create/reuse root
        Transform root = r.pivot.Find(r.generatedRootName);
        if (root == null)
        {
            var rootGo = new GameObject(r.generatedRootName);
            rootGo.transform.SetParent(r.pivot, false);
            root = rootGo.transform;
        }
        ClearChildren(root);

        float stepRad = (Mathf.PI * 2f) / r.segments;
        float arcLen = (2f * Mathf.PI * r.ringRadius) / r.segments;

        // Capsule height must be >= 2*radius
        float capsuleHeight = Mathf.Max((2f * r.tubeRadius) + 0.001f,
                                        arcLen + (2f * r.tubeRadius) + r.heightPadding);

        for (int i = 0; i < r.segments; i++)
        {
            float t = i * stepRad;

            // radial and tangent directions in pivot local space
            Vector3 radial = (Mathf.Cos(t) * a) + (Mathf.Sin(t) * b);
            Vector3 tangent = (-Mathf.Sin(t) * a) + (Mathf.Cos(t) * b);

            var segGo = new GameObject($"CapsuleSeg_{i:00}");
            segGo.transform.SetParent(root, false);
            segGo.transform.localPosition = radial * r.ringRadius;

            // Make seg local up = tangent, seg local forward = normal
            segGo.transform.localRotation = Quaternion.LookRotation(n, tangent);

            segGo.layer = r.inheritPivotLayer ? r.pivot.gameObject.layer : r.collidersLayer;

            var col = segGo.AddComponent<CapsuleCollider>();
            col.direction = 1; // Y axis (because we aligned seg's up to tangent)
            col.radius = r.tubeRadius;
            col.height = capsuleHeight;
            col.center = Vector3.zero;
            col.isTrigger = r.isTrigger;
            col.material = r.physicMaterial;
        }
    }

    void RemoveRing(Ring r)
    {
        if (r.pivot == null) return;
        var root = r.pivot.Find(r.generatedRootName);
        if (root == null) return;

        if (Application.isPlaying) Destroy(root.gameObject);
        else DestroyImmediate(root.gameObject);
    }

    static Vector3 AxisToVector(Axis axis)
    {
        switch (axis)
        {
            case Axis.X: return Vector3.right;
            case Axis.Y: return Vector3.up;
            case Axis.Z: return Vector3.forward;
            case Axis.NegX: return Vector3.left;
            case Axis.NegY: return Vector3.down;
            case Axis.NegZ: return Vector3.back;
            default: return Vector3.forward;
        }
    }

    static void ClearChildren(Transform parent)
    {
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            var child = parent.GetChild(i).gameObject;
            if (Application.isPlaying) Destroy(child);
            else DestroyImmediate(child);
        }
    }

    static Transform FindDeepChild(Transform parent, string name)
    {
        foreach (Transform child in parent)
        {
            if (child.name == name) return child;
            var result = FindDeepChild(child, name);
            if (result != null) return result;
        }
        return null;
    }
}
