using UnityEngine;
using System.Collections.Generic;

namespace GV.Scripts
{
    public enum AstraSpawnShape
    {
        Default,
        Box
    }

    /// <summary>
    /// Spawns a fixed number of objects (e.g., Astra Missile Refills) within a defined area.
    /// Supports Default (spawner-centered box using spawnAreaSize) or Box (external Transform volume).
    /// </summary>
    public class AstraRefillSpawner : MonoBehaviour
    {
        [Tooltip("The list of prefabs to spawn. Each prefab in this list will be spawned 'spawnCount' times.")]
        public List<GameObject> prefabs = new List<GameObject>();

        [Tooltip("Number of objects to spawn FOR EACH PREFAB in the list.")]
        public int spawnCount = 100;

        [Header("Spawn Shape")]
        [Tooltip("Default = original area box centered on this transform. Box = use an external box volume Transform.")]
        public AstraSpawnShape spawnShape = AstraSpawnShape.Default;

        [Tooltip("The area box size within which to spawn objects randomly (Default mode only).")]
        public Vector3 spawnAreaSize = new Vector3(1000, 1000, 1000);

        [Tooltip("External box volume Transform. Uses its position and lossyScale as bounding box (Box mode only).")]
        public Transform spawnBoxVolume;

        [Tooltip("Minimum spacing between spawned objects (Box mode only).")]
        public float minSpacing = 0f;

        [Tooltip("Deterministic seed for multiplayer. Same seed = identical results on all clients. 0 = random each time (Box mode only).")]
        public int seed = 0;

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

        [Header("Network")]
        [Tooltip("When true, skip auto-spawning and wait for NetworkedAstraRefillInitializer to call SpawnObjects() with the synced seed.")]
        public bool waitForNetworkSeed = false;

        private List<GameObject> spawnedObjects = new List<GameObject>();

        private void Awake()
        {
            if (!waitForNetworkSeed && spawnEvent == SpawnEvent.Awake) SpawnObjects();
        }

        private void Start()
        {
            if (!waitForNetworkSeed && spawnEvent == SpawnEvent.Start) SpawnObjects();
        }

        private void OnEnable()
        {
            if (!waitForNetworkSeed && spawnEvent == SpawnEvent.OnEnable)
            {
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

            switch (spawnShape)
            {
                case AstraSpawnShape.Box:
                    SpawnInBox();
                    break;
                default:
                    SpawnDefault();
                    break;
            }
        }


        // ═══════════════════════════════════════════════════════════════
        //  DEFAULT (original – spawner-centered area box)
        // ═══════════════════════════════════════════════════════════════

        private void SpawnDefault()
        {
            foreach (var prefab in prefabs)
            {
                if (prefab == null) continue;

                for (int i = 0; i < spawnCount; i++)
                {
                    Vector3 randomOffset = new Vector3(
                        Random.Range(-spawnAreaSize.x / 2, spawnAreaSize.x / 2),
                        Random.Range(-spawnAreaSize.y / 2, spawnAreaSize.y / 2),
                        Random.Range(-spawnAreaSize.z / 2, spawnAreaSize.z / 2)
                    );

                    Vector3 spawnPos = transform.position + randomOffset;
                    Quaternion spawnRot = GetRotation();

                    GameObject newObj = Instantiate(prefab, spawnPos, spawnRot, transform);
                    spawnedObjects.Add(newObj);
                }
            }
        }


        // ═══════════════════════════════════════════════════════════════
        //  BOX (external Transform volume)
        // ═══════════════════════════════════════════════════════════════

        private void SpawnInBox()
        {
            if (spawnBoxVolume == null)
            {
                Debug.LogWarning("[AstraRefillSpawner] spawnShape is Box but no spawnBoxVolume assigned! Falling back to Default.");
                SpawnDefault();
                return;
            }

            Vector3 boxCenter = spawnBoxVolume.position;
            Vector3 boxHalfExtents = spawnBoxVolume.lossyScale * 0.5f;

            // Deterministic RNG – seed 0 means random each game
            int s = seed != 0 ? seed : System.Environment.TickCount;
            System.Random rng = new System.Random(s);

            float minSqr = minSpacing * minSpacing;
            List<Vector3> positions = new List<Vector3>();

            int totalCount = spawnCount * prefabs.Count;
            int maxAttempts = totalCount * 200;
            int attempts = 0;

            // Generate all positions first (respecting minSpacing)
            while (positions.Count < totalCount && attempts < maxAttempts)
            {
                attempts++;

                Vector3 candidate = boxCenter + new Vector3(
                    RngRange(rng, -boxHalfExtents.x, boxHalfExtents.x),
                    RngRange(rng, -boxHalfExtents.y, boxHalfExtents.y),
                    RngRange(rng, -boxHalfExtents.z, boxHalfExtents.z)
                );

                // Min-spacing check
                if (minSpacing > 0)
                {
                    bool tooClose = false;
                    for (int i = 0; i < positions.Count; i++)
                    {
                        if ((candidate - positions[i]).sqrMagnitude < minSqr)
                        {
                            tooClose = true;
                            break;
                        }
                    }
                    if (tooClose) continue;
                }

                positions.Add(candidate);
            }

            // Instantiate – cycle through prefabs
            int prefabIndex = 0;
            int countPerPrefab = 0;
            for (int i = 0; i < positions.Count; i++)
            {
                // Skip null prefabs
                while (prefabIndex < prefabs.Count && prefabs[prefabIndex] == null)
                    prefabIndex++;
                if (prefabIndex >= prefabs.Count) break;

                Quaternion spawnRot = GetRotationRng(rng);
                GameObject newObj = Instantiate(prefabs[prefabIndex], positions[i], spawnRot, transform);
                spawnedObjects.Add(newObj);

                countPerPrefab++;
                if (countPerPrefab >= spawnCount)
                {
                    countPerPrefab = 0;
                    prefabIndex++;
                }
            }

            Debug.Log($"[AstraRefillSpawner] Box: spawned {spawnedObjects.Count}/{totalCount} objects (seed={s})");
        }


        // ─── Rotation helpers ────────────────────────────────────────

        private Quaternion GetRotation()
        {
            if (randomRotation > 0)
            {
                return Quaternion.Euler(
                    Random.Range(0, randomRotation * 360),
                    Random.Range(0, randomRotation * 360),
                    Random.Range(0, randomRotation * 360)
                );
            }
            return transform.rotation;
        }

        private Quaternion GetRotationRng(System.Random rng)
        {
            if (randomRotation > 0)
            {
                return Quaternion.Euler(
                    RngRange(rng, 0, randomRotation * 360),
                    RngRange(rng, 0, randomRotation * 360),
                    RngRange(rng, 0, randomRotation * 360)
                );
            }
            return transform.rotation;
        }

        // ─── Network-safe RNG helper ─────────────────────────────────

        private static float RngRange(System.Random rng, float min, float max)
        {
            return min + (float)(rng.NextDouble() * (max - min));
        }


        // ═══════════════════════════════════════════════════════════════
        //  GIZMOS
        // ═══════════════════════════════════════════════════════════════

        private void OnDrawGizmosSelected()
        {
            if (spawnShape == AstraSpawnShape.Box && spawnBoxVolume != null)
            {
                Gizmos.color = new Color(1f, 1f, 0f, 0.3f);
                Gizmos.DrawWireCube(spawnBoxVolume.position, spawnBoxVolume.lossyScale);
            }
            else
            {
                Gizmos.color = new Color(0, 1, 0, 0.3f);
                Gizmos.DrawCube(transform.position, spawnAreaSize);
                Gizmos.color = Color.green;
                Gizmos.DrawWireCube(transform.position, spawnAreaSize);
            }
        }
    }
}
