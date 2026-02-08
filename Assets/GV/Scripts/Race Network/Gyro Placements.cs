// ExistingObjectsToCheckpoints.cs
// Use this to snap your existing objects (like gyro rings) to checkpoint positions
// Supports randomized placement at runtime within a min/max range with fixed interval

using System.Collections.Generic;
using UnityEngine;

public class ExistingObjectsToCheckpoints : MonoBehaviour
{
    [Header("Source")]
    [Tooltip("The parent containing all checkpoints.")]
    public Transform checkpointsParent;

    [Tooltip("If true, only direct children of checkpointsParent are considered checkpoints.")]
    public bool onlyDirectChildren = true;

    [Header("Objects to Position")]
    [Tooltip("Your existing objects to position (e.g., your 6 gyro rings). Order matters!")]
    public List<GameObject> objectsToPlace = new List<GameObject>();

    [Header("Randomized Placement")]
    [Tooltip("If true, randomizes the starting checkpoint within the min/max range at runtime.")]
    public bool randomizeStart = true;

    [Tooltip("Minimum checkpoint index for the FIRST object (inclusive).")]
    public int minCheckpoint = 3;

    [Tooltip("Maximum checkpoint index for the LAST object (inclusive).")]
    public int maxCheckpoint = 105;

    [Tooltip("Fixed interval/spacing between each object. E.g., 18 means 3, 21, 39, 57...")]
    public int checkpointInterval = 18;

    [Header("Fixed Placement (if randomizeStart is false)")]
    [Tooltip("If randomizeStart is false, use this as the fixed starting checkpoint.")]
    public int fixedStartCheckpoint = 9;

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
    
    // The actual start index used this session (for debugging)
    private int actualStartIndex;

    void Start()
    {
        // At runtime, snap all objects to their calculated positions
        SnapAllToCheckpoints();
    }

    [ContextMenu("Snap All Objects to Checkpoints")]
    public void SnapAllToCheckpoints()
    {
        if (checkpointsParent == null) return;
        if (objectsToPlace.Count == 0) return;

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

        // Calculate the starting checkpoint index
        if (randomizeStart)
        {
            // Calculate how much space we need for all objects
            // With N objects and interval I, last object is at: start + (N-1) * I
            // So: start + (N-1) * I <= maxCheckpoint
            // Therefore: start <= maxCheckpoint - (N-1) * I
            int objectCount = objectsToPlace.Count;
            int totalSpaceNeeded = (objectCount - 1) * checkpointInterval;
            int maxStartIndex = maxCheckpoint - totalSpaceNeeded;

            // Clamp to ensure valid range
            int minStart = Mathf.Max(0, minCheckpoint);
            int maxStart = Mathf.Min(cachedCheckpoints.Count - 1 - totalSpaceNeeded, maxStartIndex);

            if (maxStart < minStart)
            {
                Debug.LogWarning($"Cannot fit {objectCount} objects with interval {checkpointInterval} between checkpoints {minCheckpoint} and {maxCheckpoint}. Using minimum.");
                actualStartIndex = minStart;
            }
            else
            {
                // Pick a random start within the valid range
                actualStartIndex = Random.Range(minStart, maxStart + 1);
            }

            Debug.Log($"[GyroRings] Randomized start: Checkpoint {actualStartIndex} (range was {minStart}-{maxStart})");
        }
        else
        {
            actualStartIndex = fixedStartCheckpoint;
        }

        // Position each object
        for (int i = 0; i < objectsToPlace.Count; i++)
        {
            GameObject obj = objectsToPlace[i];
            if (obj == null) continue;

            // Calculate checkpoint index: start + (i * interval)
            int checkpointIndex = actualStartIndex + (i * checkpointInterval);

            // Validate index
            if (checkpointIndex < 0 || checkpointIndex >= cachedCheckpoints.Count)
            {
                Debug.LogWarning($"Checkpoint index {checkpointIndex} for {obj.name} is out of range (0-{cachedCheckpoints.Count - 1}). Skipping.");
                continue;
            }

            Transform checkpoint = cachedCheckpoints[checkpointIndex];
            if (checkpoint == null) continue;

            Transform objTransform = obj.transform;

            // Parent if needed
            if (parentToCheckpoint && objTransform.parent != checkpoint)
            {
                objTransform.SetParent(checkpoint, false);
            }

            // Position
            if (parentToCheckpoint)
            {
                objTransform.localPosition = localOffset;
            }
            else
            {
                objTransform.position = checkpoint.TransformPoint(localOffset);
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

            Debug.Log($"Placed {obj.name} at checkpoint {checkpointIndex} ({checkpoint.name})");
        }
    }

    [ContextMenu("Preview Possible Range")]
    public void PreviewPossibleRange()
    {
        int objectCount = objectsToPlace.Count;
        int totalSpaceNeeded = (objectCount - 1) * checkpointInterval;
        int maxStartIndex = maxCheckpoint - totalSpaceNeeded;

        Debug.Log($"[GyroRings Preview]\n" +
                  $"  Objects: {objectCount}\n" +
                  $"  Interval: {checkpointInterval}\n" +
                  $"  Total space needed: {totalSpaceNeeded}\n" +
                  $"  Start can range from: {minCheckpoint} to {maxStartIndex}\n" +
                  $"  Example if start={minCheckpoint}: {GetIndicesString(minCheckpoint)}\n" +
                  $"  Example if start={maxStartIndex}: {GetIndicesString(maxStartIndex)}");
    }

    private string GetIndicesString(int start)
    {
        string result = "";
        for (int i = 0; i < objectsToPlace.Count; i++)
        {
            if (i > 0) result += ", ";
            result += (start + i * checkpointInterval).ToString();
        }
        return result;
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

    /// <summary>
    /// Returns the actual start index used this session (useful for debugging/UI).
    /// </summary>
    public int ActualStartIndex => actualStartIndex;
}
