using UnityEngine;
using UnityEngine.Splines;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class RingSpawnerOnSpline : MonoBehaviour
{
    [Header("References")]
    public SplineContainer splineContainer;
    public GameObject ringPrefab;
    public Transform ringsParent;

    [Header("Spawn Settings")]
    [Min(2)] public int ringCount = 100;
    public bool alignToPathTangent = true;

    [Tooltip("Rotate ring so aircraft flies through it. Adjust depending on your ring model orientation.")]
    public Vector3 ringRotationOffsetEuler = new Vector3(0f, 0f, 0f);

    [Header("Controls")]
    public bool regenerateInEditor = false;
    public bool clearChildrenFirst = true;

    void OnValidate()
    {
        if (!regenerateInEditor) return;
        regenerateInEditor = false;

        if (splineContainer == null || ringPrefab == null) return;
        if (ringsParent == null) ringsParent = transform;

#if UNITY_EDITOR
        if (!Application.isPlaying)
            Generate();
#endif
    }

#if UNITY_EDITOR
    void Generate()
    {
        if (clearChildrenFirst)
        {
            for (int i = ringsParent.childCount - 1; i >= 0; i--)
                DestroyImmediate(ringsParent.GetChild(i).gameObject);
        }

        var spline = splineContainer.Spline;
        if (spline == null || spline.Count < 2) return;

        for (int i = 0; i < ringCount; i++)
        {
            float t = (ringCount == 1) ? 0f : (float)i / (ringCount - 1);

            Vector3 pos = splineContainer.EvaluatePosition(t);

            Quaternion rot = Quaternion.identity;
            if (alignToPathTangent)
            {
                Vector3 tangent = splineContainer.EvaluateTangent(t);
                if (tangent.sqrMagnitude > 0.0001f)
                {
                    tangent.Normalize();
                    // Ring "forward" points along path
                    rot = Quaternion.LookRotation(tangent, Vector3.up);
                }
            }

            rot *= Quaternion.Euler(ringRotationOffsetEuler);

            GameObject ring = (GameObject)PrefabUtility.InstantiatePrefab(ringPrefab, ringsParent);
            ring.transform.SetPositionAndRotation(pos, rot);
            ring.name = $"Ring_{i:000}";
        }
    }
#endif
}
