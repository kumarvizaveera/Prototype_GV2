// ExistingObjectsToCheckpoints.cs
// Use this to snap your existing objects (like gyro rings) to specific checkpoint positions

using System;
using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
public class ExistingObjectsToCheckpoints : MonoBehaviour
{
    [Header("Source")]
    [Tooltip("The parent containing all checkpoints.")]
    public Transform checkpointsParent;

    [Tooltip("If true, only direct children of checkpointsParent are considered checkpoints.")]
    public bool onlyDirectChildren = true;

    [Header("Objects to Position")]
    [Tooltip("Your existing objects and which checkpoint index they should snap to.")]
    public List<ObjectCheckpointMapping> objectMappings = new List<ObjectCheckpointMapping>();

    [Header("Position & Rotation")]
    [Tooltip("Offset from checkpoint position (local space).")]
    public Vector3 localOffset = Vector3.zero;

    [Tooltip("Rotation offset applied (Euler angles).")]
    public Vector3 rotationOffsetEuler = Vector3.zero;

    [Tooltip("If true, objects inherit the checkpoint's rotation. If false, uses world rotation.")]
    public bool inheritCheckpointRotation = true;

    [Tooltip("If true, objects become children of their target checkpoints.")]
    public bool parentToCheckpoint = false;

    [Header("Scale")]
    [Tooltip("If true, applies scale compensation based on checkpoint scale.")]
    public bool compensateParentScale = false;

    [Tooltip("Scale multiplier.")]
    public float scaleMultiplier = 1f;

    // Cached checkpoint list
    private List<Transform> cachedCheckpoints = new List<Transform>();

    [Serializable]
    public class ObjectCheckpointMapping
    {
        [Tooltip("The existing object to position (e.g., your gyro ring).")]
        public GameObject targetObject;

        [Tooltip("The checkpoint index (0-based) to snap this object to.")]
        public int checkpointIndex;

        [Tooltip("Optional: Override the global offset for this specific object.")]
        public bool useCustomOffset = false;
        public Vector3 customOffset = Vector3.zero;
    }

    void OnEnable() => SnapAllToCheckpoints();
    void OnValidate() => SnapAllToCheckpoints();

    [ContextMenu("Snap All Objects to Checkpoints")]
    public void SnapAllToCheckpoints()
    {
        if (checkpointsParent == null) return;

        // Gather checkpoints
        cachedCheckpoints.Clear();
        if (onlyDirectChildren)
        {
            for (int i = 0; i < checkpointsParent.childCount; i++)
                cachedCheckpoints.Add(checkpointsParent.GetChild(i));
        }
        else
        {
            foreach (Transform t in checkpointsParent.GetComponentsInChildren<Transform>(true))
                if (t != checkpointsParent) cachedCheckpoints.Add(t);
        }

        // Position each mapped object
        foreach (var mapping in objectMappings)
        {
            if (mapping.targetObject == null) continue;
            if (mapping.checkpointIndex < 0 || mapping.checkpointIndex >= cachedCheckpoints.Count)
            {
                Debug.LogWarning($"Checkpoint index {mapping.checkpointIndex} is out of range (0-{cachedCheckpoints.Count - 1})");
                continue;
            }

            Transform checkpoint = cachedCheckpoints[mapping.checkpointIndex];
            if (checkpoint == null) continue;

            Transform objTransform = mapping.targetObject.transform;

            // Parent if needed
            if (parentToCheckpoint && objTransform.parent != checkpoint)
            {
                objTransform.SetParent(checkpoint, false);
            }

            // Calculate offset
            Vector3 offset = mapping.useCustomOffset ? mapping.customOffset : localOffset;

            // Position
            if (parentToCheckpoint)
            {
                objTransform.localPosition = offset;
            }
            else
            {
                objTransform.position = checkpoint.TransformPoint(offset);
            }

            // Rotation
            if (inheritCheckpointRotation)
            {
                objTransform.rotation = checkpoint.rotation * Quaternion.Euler(rotationOffsetEuler);
            }
            else
            {
                objTransform.rotation = Quaternion.Euler(rotationOffsetEuler);
            }

            // Scale compensation
            if (compensateParentScale && parentToCheckpoint)
            {
                Vector3 s = checkpoint.lossyScale;
                float sx = Mathf.Abs(s.x) < 1e-6f ? 1f : s.x;
                float sy = Mathf.Abs(s.y) < 1e-6f ? 1f : s.y;
                float sz = Mathf.Abs(s.z) < 1e-6f ? 1f : s.z;

                objTransform.localScale = new Vector3(1f / sx, 1f / sy, 1f / sz) * scaleMultiplier;
            }
            else if (scaleMultiplier != 1f)
            {
                objTransform.localScale = Vector3.one * scaleMultiplier;
            }
        }
    }

    [ContextMenu("Auto-Assign First N Objects")]
    public void AutoAssignSequential()
    {
        // Assigns checkpoint indices 0, 1, 2, 3... to each object in the list
        for (int i = 0; i < objectMappings.Count; i++)
        {
            objectMappings[i].checkpointIndex = i;
        }
        SnapAllToCheckpoints();
    }

    /// <summary>
    /// Returns the checkpoint Transform at the given index, or null if out of range.
    /// </summary>
    public Transform GetCheckpoint(int index)
    {
        if (cachedCheckpoints == null || index < 0 || index >= cachedCheckpoints.Count)
            return null;
        return cachedCheckpoints[index];
    }

    /// <summary>
    /// Returns total checkpoint count.
    /// </summary>
    public int CheckpointCount => cachedCheckpoints?.Count ?? 0;
}
