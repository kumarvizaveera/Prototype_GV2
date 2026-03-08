using UnityEngine;
using System.Collections.Generic;

namespace VSX.UniversalVehicleCombat
{
    /// <summary>
    /// Spawns a group of objects randomly inside a box volume.
    /// </summary>
    public class MassObjectSpawner : MonoBehaviour
    {
        [SerializeField]
        protected GameObject prefab;

        [Tooltip("Additional prefabs to randomly pick from. If populated, the spawner randomly selects from this list plus the single prefab above if set.")]
        [SerializeField]
        protected List<GameObject> prefabs = new List<GameObject>();

        protected enum SpawnEvent
        {
            Awake,
            Start,
            OnEnable,
            Scripted
        }

        [SerializeField]
        protected SpawnEvent spawnEvent = SpawnEvent.Awake;

        [Header("Spawn Volume")]
        [Tooltip("Box volume transform. Uses position and lossyScale as the bounds. If empty, this GameObject transform is used.")]
        [SerializeField]
        protected Transform spawnBoxVolume;

        [Tooltip("Number of objects to spawn.")]
        [SerializeField]
        protected int spawnCount = 100;

        [Tooltip("Minimum spacing between spawned objects.")]
        [SerializeField]
        protected float minSpacing = 20f;

        [Tooltip("Deterministic seed for multiplayer. Same seed means identical results on all clients. 0 means random each time.")]
        [SerializeField]
        protected int seed = 0;

        [Header("Scaling")]

        [SerializeField]
        protected float minRandomScale = 1;

        [SerializeField]
        protected float maxRandomScale = 3;

        [SerializeField]
        protected float minScaleAmount = 1;

        [SerializeField]
        protected float maxScaleAmount = 2;

        [SerializeField]
        protected AnimationCurve distanceScaleCurve = AnimationCurve.Linear(0, 1, 1, 0);

        [SerializeField]
        protected float distanceCutoff = 1000;

        [SerializeField]
        protected float maxDistMargin = 0;

        [SerializeField]
        protected List<MassObjectSpawnBlocker> spawnBlockers = new List<MassObjectSpawnBlocker>();

        protected List<GameObject> cachedPrefabPool;

        protected virtual void Awake()
        {
            if (spawnEvent == SpawnEvent.Awake) CreateObjects();
        }

        protected virtual void Start()
        {
            if (spawnEvent == SpawnEvent.Start) CreateObjects();
        }

        protected virtual void OnEnable()
        {
            if (spawnEvent == SpawnEvent.OnEnable) CreateObjects();
        }

        /// <summary>
        /// Create the objects in the scene.
        /// </summary>
        public virtual void CreateObjects()
        {
            cachedPrefabPool = null;
            CreateObjectsInBox();
        }

        protected virtual void CreateObjectsInBox()
        {
            Transform volume = spawnBoxVolume != null ? spawnBoxVolume : transform;
            Vector3 boxCenter = volume.position;
            Vector3 boxHalfExtents = volume.lossyScale * 0.5f;

            int resolvedSeed = seed != 0 ? seed : System.Environment.TickCount;
            System.Random rng = new System.Random(resolvedSeed);

            float minSpacingSqr = minSpacing * minSpacing;
            List<Vector3> positions = new List<Vector3>(spawnCount);
            int maxAttempts = spawnCount * 200;
            int attempts = 0;

            while (positions.Count < spawnCount && attempts < maxAttempts)
            {
                attempts++;

                Vector3 candidate = boxCenter + new Vector3(
                    RngRange(rng, -boxHalfExtents.x, boxHalfExtents.x),
                    RngRange(rng, -boxHalfExtents.y, boxHalfExtents.y),
                    RngRange(rng, -boxHalfExtents.z, boxHalfExtents.z));

                float distFromCenter = Vector3.Distance(candidate, boxCenter);
                if (distanceCutoff > 0f && distFromCenter > distanceCutoff)
                {
                    if (distFromCenter - distanceCutoff < maxDistMargin)
                    {
                        candidate = boxCenter + (candidate - boxCenter).normalized * distanceCutoff;
                    }
                    else
                    {
                        continue;
                    }
                }

                bool tooClose = false;
                for (int i = 0; i < positions.Count; i++)
                {
                    if ((candidate - positions[i]).sqrMagnitude < minSpacingSqr)
                    {
                        tooClose = true;
                        break;
                    }
                }

                if (tooClose) continue;

                bool isBlocked = false;
                for (int i = 0; i < spawnBlockers.Count; ++i)
                {
                    if (spawnBlockers[i] != null && spawnBlockers[i].IsBlocked(candidate))
                    {
                        isBlocked = true;
                        break;
                    }
                }

                if (isBlocked) continue;

                positions.Add(candidate);
            }

            for (int i = 0; i < positions.Count; i++)
            {
                GameObject chosenPrefab = PickPrefab(rng);
                if (chosenPrefab == null) continue;

                GameObject temp = Instantiate(chosenPrefab, positions[i], Quaternion.identity);
                temp.transform.SetParent(null, true);

                float distFromCenter = Vector3.Distance(positions[i], boxCenter);
                float scale = RngRange(rng, minRandomScale, maxRandomScale);
                if (distanceCutoff > 0f)
                {
                    float scaleAmount = distanceScaleCurve.Evaluate(distFromCenter / distanceCutoff);
                    scale *= scaleAmount * maxScaleAmount + (1 - scaleAmount) * scaleAmount;
                }

                temp.transform.localScale = new Vector3(scale, scale, scale);
            }

            Debug.Log($"[MassObjectSpawner] Spawned {positions.Count}/{spawnCount} objects (seed={resolvedSeed})");
        }

        protected static float RngRange(System.Random rng, float min, float max)
        {
            return min + (float)(rng.NextDouble() * (max - min));
        }

        /// <summary>
        /// Builds a merged list of all valid prefabs, cached per spawn call.
        /// </summary>
        protected List<GameObject> GetPrefabPool()
        {
            if (cachedPrefabPool != null) return cachedPrefabPool;

            cachedPrefabPool = new List<GameObject>();
            if (prefab != null) cachedPrefabPool.Add(prefab);

            if (prefabs != null)
            {
                for (int i = 0; i < prefabs.Count; i++)
                {
                    if (prefabs[i] != null && !cachedPrefabPool.Contains(prefabs[i]))
                    {
                        cachedPrefabPool.Add(prefabs[i]);
                    }
                }
            }

            return cachedPrefabPool;
        }

        /// <summary>
        /// Pick a random prefab from the pool using deterministic RNG.
        /// </summary>
        protected GameObject PickPrefab(System.Random rng)
        {
            List<GameObject> pool = GetPrefabPool();
            if (pool.Count == 0) return null;
            if (pool.Count == 1) return pool[0];
            return pool[rng.Next(pool.Count)];
        }

        protected virtual void OnDrawGizmosSelected()
        {
            Transform volume = spawnBoxVolume != null ? spawnBoxVolume : transform;
            Gizmos.color = new Color(1f, 1f, 0f, 0.3f);
            Gizmos.DrawWireCube(volume.position, volume.lossyScale);
        }
    }
}
