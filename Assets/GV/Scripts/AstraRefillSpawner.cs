using UnityEngine;
using System.Collections.Generic;

namespace GV.Scripts
{
    /// <summary>
    /// Spawns a fixed number of objects (e.g., Astra Missile Refills) within a defined area, 
    /// similar to MassObjectSpawner but simplified for a specific count.
    /// </summary>
    public class AstraRefillSpawner : MonoBehaviour
    {
        [Tooltip("The list of prefabs to spawn. Each prefab in this list will be spawned 'spawnCount' times.")]
        public List<GameObject> prefabs = new List<GameObject>();

        [Tooltip("Number of objects to spawn FOR EACH PREFAB in the list.")]
        public int spawnCount = 100;

        [Tooltip("The area box size within which to spawn objects randomly.")]
        public Vector3 spawnAreaSize = new Vector3(1000, 1000, 1000);

        [Header("Rotation")]
        [Range(0, 1)]
        [Tooltip("Randomness of the rotation (0 = no random rotation, 1 = full random rotation).")]
        public float randomRotation = 1f;

        public enum SpawnEvent
        {
            Awake,
            Start,
            OnEnable
        }
        [Header("Timing")]
        [Tooltip("When to trigger the spawn.")]
        public SpawnEvent spawnEvent = SpawnEvent.Start;

        private List<GameObject> spawnedObjects = new List<GameObject>();

        private void Awake()
        {
            if (spawnEvent == SpawnEvent.Awake) SpawnObjects();
        }

        private void Start()
        {
            if (spawnEvent == SpawnEvent.Start) SpawnObjects();
        }

        private void OnEnable()
        {
            if (spawnEvent == SpawnEvent.OnEnable)
            {
                // Only spawn if not already spawned or if we want to reset on enable
                if (spawnedObjects.Count == 0 || spawnedObjects.Exists(x => x == null))
                {
                   SpawnObjects(); 
                }
            }
        }

        /// <summary>
        /// Clears existing spawned objects and creates new ones.
        /// </summary>
        public void SpawnObjects()
        {
            // Clear existing objects
            foreach (var obj in spawnedObjects)
            {
                if (obj != null) Destroy(obj);
            }
            spawnedObjects.Clear();

            if (prefabs == null || prefabs.Count == 0)
            {
                Debug.LogWarning("[AstraRefillSpawner] No prefabs assigned!");
                return;
            }

            foreach (var prefab in prefabs)
            {
                if (prefab == null) continue;

                for (int i = 0; i < spawnCount; i++)
                {
                    // Calculate random position within the box area relative to this transform
                    Vector3 randomOffset = new Vector3(
                        Random.Range(-spawnAreaSize.x / 2, spawnAreaSize.x / 2),
                        Random.Range(-spawnAreaSize.y / 2, spawnAreaSize.y / 2),
                        Random.Range(-spawnAreaSize.z / 2, spawnAreaSize.z / 2)
                    );
    
                    Vector3 spawnPos = transform.position + randomOffset;
    
                    // Calculate rotation
                    Quaternion spawnRot = Quaternion.identity;
                    if (randomRotation > 0)
                    {
                        spawnRot = Quaternion.Euler(
                            Random.Range(0, randomRotation * 360),
                            Random.Range(0, randomRotation * 360),
                            Random.Range(0, randomRotation * 360)
                        );
                    }
                    else
                    {
                        spawnRot = transform.rotation; // Use spawner's rotation if no random
                    }
    
                    // Instantiate
                    GameObject newObj = Instantiate(prefab, spawnPos, spawnRot, transform);
                    spawnedObjects.Add(newObj);
                }
            }
        }

        private void OnDrawGizmosSelected()
        {
            // Draw the spawn area in the editor
            Gizmos.color = new Color(0, 1, 0, 0.3f);
            Gizmos.DrawCube(transform.position, spawnAreaSize);
            Gizmos.color = Color.green;
            Gizmos.DrawWireCube(transform.position, spawnAreaSize);
        }
    }
}
