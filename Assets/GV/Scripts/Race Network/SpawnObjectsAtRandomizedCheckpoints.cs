using System.Collections.Generic;
using UnityEngine;

public class SpawnObjectsAtRandomizedCheckpoints : MonoBehaviour
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
    [Tooltip("The object prefab to spawn.")]
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

    void Start()
    {
        SpawnObjects();
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

        foreach (var target in spawnTargets)
        {
            int randomOffset = Random.Range(-target.randomRange, target.randomRange + 1);
            int finalIndex = target.checkpointIndex + randomOffset;

            // Ensure index is within valid bounds if not wrapping
            if (!CheckpointNetwork.Instance.wrapAround)
            {
                finalIndex = Mathf.Clamp(finalIndex, 0, CheckpointNetwork.Instance.Count - 1);
            }
            
            Transform checkpoint = CheckpointNetwork.Instance.GetCheckpoint(finalIndex);

            if (checkpoint != null)
            {
                Vector3 spawnPos = checkpoint.position + (checkpoint.rotation * positionOffset);
                Quaternion spawnRot = checkpoint.rotation * Quaternion.Euler(rotationOffset);

                GameObject spawnedObj = Instantiate(objectToSpawn, spawnPos, spawnRot);
                
                if (parentToCheckpoint)
                {
                    spawnedObj.transform.SetParent(checkpoint);
                }
                
                Debug.Log($"[SpawnObjectsAtRandomizedCheckpoints] Spawned at Checkpoint {finalIndex} (Base: {target.checkpointIndex}, Offset: {randomOffset})");
            }
            else
            {
                Debug.LogWarning($"[SpawnObjectsAtRandomizedCheckpoints] Could not find checkpoint {finalIndex}");
            }
        }
    }
}
