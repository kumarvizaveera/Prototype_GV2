using System.Collections.Generic;
using UnityEngine;
using Fusion;

[DefaultExecutionOrder(200)]
public class SpawnObjectsAtRandomizedCheckpoints : NetworkBehaviour
{
    [System.Serializable]
    public struct SpawnTarget
    {
        [Tooltip("The base checkpoint index to spawn at.")]
        public int checkpointIndex;
        
        [Tooltip("Random range added/subtracted from the base index. E.g., 6 means +/- 6.")]
        public int randomRange;
    }

    [Header("Configuration")]
    [Tooltip("The object prefab to spawn. MUST have a NetworkObject component!")]
    public GameObject objectToSpawn;

    [Tooltip("List of spawn locations with their random ranges.")]
    public List<SpawnTarget> spawnTargets = new List<SpawnTarget>
    {
        new SpawnTarget { checkpointIndex = 30, randomRange = 6 },
        new SpawnTarget { checkpointIndex = 66, randomRange = 6 },
        new SpawnTarget { checkpointIndex = 84, randomRange = 6 }
    };

    [Header("Placement Settings")]
    [Tooltip("Offset from the checkpoint position.")]
    public Vector3 positionOffset = Vector3.zero;

    [Tooltip("Rotation offset relative to the checkpoint's rotation.")]
    public Vector3 rotationOffset = Vector3.zero;

    [Tooltip("Whether the spawned object should be parented to the checkpoint.")]
    public bool parentToCheckpoint = false;

    [Header("Overlap Avoidance")]
    [Tooltip("Reference to the script managing existing objects (e.g., Gyro Rings).")]
    public ExistingObjectsToCheckpoints existingObjectsRef;

    [Tooltip("Minimum distance (in checkpoint indices) to keep from existing objects.")]
    public int minSafeDistance = 6;

    // Use Spawned() to run logic when this object is part of the network simulation
    public override void Spawned()
    {
        // Only the server spawns objects
        if (Object.HasStateAuthority)
        {
            SpawnObjects();
        }
    }

    [ContextMenu("Spawn Objects")]
    public void SpawnObjects()
    {
        if (objectToSpawn == null)
        {
            Debug.LogError("[SpawnObjectsAtRandomizedCheckpoints] No object to spawn assigned!");
            return;
        }

        if (CheckpointNetwork.Instance == null)
        {
            Debug.LogError("[SpawnObjectsAtRandomizedCheckpoints] CheckpointNetwork instance not found!");
            return;
        }

        if (existingObjectsRef == null)
        {
             Debug.LogWarning("[SpawnObjectsAtRandomizedCheckpoints] 'Existing Objects Ref' is not assigned! Overlap avoidance will NOT work.");
        }

        // Gather occupied indices from the other script
        HashSet<int> occupiedIndices = new HashSet<int>();
        if (existingObjectsRef != null)
        {
            // Re-calculate where the existing objects are based on its public properties
            // We assume ExistingObjectsToCheckpoints has already run its Start/SnapAllToCheckpoints
            int start = existingObjectsRef.ActualStartIndex;
            int interval = existingObjectsRef.checkpointInterval;
            int count = existingObjectsRef.objectsToPlace.Count;

            for (int i = 0; i < count; i++)
            {
                int index = start + (i * interval);
                occupiedIndices.Add(index);
            }
        }
        
        // Track spawns from THIS script to avoid self-overlap too (optional, but good practice)
        // For now, we only care about avoiding the *existing* objects as requested.
        // But let's add our own spawns to the set so subsequent spawns in this loop don't overlap previous ones.
        
        foreach (var target in spawnTargets)
        {
            // 1. Collect all valid candidate indices in the random range
            List<int> validCandidates = new List<int>();
            List<int> allCandidates = new List<int>(); // Fallback

            for (int r = -target.randomRange; r <= target.randomRange; r++)
            {
                int candidateIndex = target.checkpointIndex + r;
                
                // Validate/Wrap bounds
                if (!CheckpointNetwork.Instance.wrapAround)
                {
                    if (candidateIndex < 0 || candidateIndex >= CheckpointNetwork.Instance.Count) 
                        continue;
                }
                
                candidateIndex = Mathf.Clamp(candidateIndex, 0, CheckpointNetwork.Instance.Count - 1);

                allCandidates.Add(candidateIndex);

                // Check distance against ALL occupied indices
                if (IsPositionValid(candidateIndex, occupiedIndices))
                {
                    validCandidates.Add(candidateIndex);
                }
            }

            int finalIndex;
            if (validCandidates.Count > 0)
            {
                // Pick random valid
                finalIndex = validCandidates[Random.Range(0, validCandidates.Count)];
            }
            else
            {
                // No valid slot found! Overlap is inevitable.
                // Fallback: Pick the candidate that has the MAX minimum distance to any occupied slot.
                Debug.LogWarning($"[SpawnObjectsAtRandomizedCheckpoints] No safe index found for base {target.checkpointIndex} within range {target.randomRange}! overlaps are inevitable. Finding best fit.");
                finalIndex = GetBestFallbackIndex(allCandidates, occupiedIndices);
            }

            // Spawn
            Transform checkpoint = CheckpointNetwork.Instance.GetCheckpoint(finalIndex);
            if (checkpoint != null)
            {
                Vector3 spawnPos = checkpoint.position + (checkpoint.rotation * positionOffset);
                Quaternion spawnRot = checkpoint.rotation * Quaternion.Euler(rotationOffset);

                // Fusion Spawn
                NetworkObject prefabNO = objectToSpawn.GetComponent<NetworkObject>();
                if (prefabNO == null)
                {
                    Debug.LogError($"[SpawnObjectsAtRandomizedCheckpoints] Assigned prefab '{objectToSpawn.name}' is missing a NetworkObject component!");
                    return;
                }

                NetworkObject spawnedNO = Runner.Spawn(prefabNO, spawnPos, spawnRot, Object.InputAuthority);
                
                if (parentToCheckpoint && spawnedNO)
                {
                    spawnedNO.transform.SetParent(checkpoint);
                }
                
                // Mark this new index as occupied for subsequent iterations
                occupiedIndices.Add(finalIndex);

                Debug.Log($"[SpawnObjectsAtRandomizedCheckpoints] Spawned at Checkpoint {finalIndex} (Base: {target.checkpointIndex}). Safe from existing objects.");
            }
            else
            {
                Debug.LogWarning($"[SpawnObjectsAtRandomizedCheckpoints] Could not find checkpoint {finalIndex}");
            }
        }
    }

    private bool IsPositionValid(int candidate, HashSet<int> occupied)
    {
        foreach (int occ in occupied)
        {
            if (Mathf.Abs(candidate - occ) < minSafeDistance)
            {
                return false;
            }
        }
        return true;
    }

    private int GetBestFallbackIndex(List<int> candidates, HashSet<int> occupied)
    {
        if (candidates.Count == 0) return 0; // Should not happen if range is valid

        int bestIndex = candidates[0];
        int maxMinDist = -1;

        foreach (int c in candidates)
        {
            int minDist = int.MaxValue;
            foreach (int occ in occupied)
            {
                int dist = Mathf.Abs(c - occ);
                if (dist < minDist) minDist = dist;
            }

            if (minDist > maxMinDist)
            {
                maxMinDist = minDist;
                bestIndex = c;
            }
        }
        return bestIndex;
    }
}
