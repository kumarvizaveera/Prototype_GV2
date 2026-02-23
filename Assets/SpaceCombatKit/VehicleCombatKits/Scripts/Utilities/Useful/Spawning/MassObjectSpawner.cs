using UnityEngine;
using System.Collections;
using System.Collections.Generic;

namespace VSX.UniversalVehicleCombat
{
	public enum MassSpawnShape
	{
		Grid,
		Box
	}

	/// <summary>
	/// This class spawns a group of objects in the scene (e.g. asteroids).
	/// Supports Grid (original) or Box volume spawn shapes.
	/// </summary>
	public class MassObjectSpawner : MonoBehaviour
	{

		[SerializeField]
		protected GameObject prefab;

		[Tooltip("Additional prefabs to randomly pick from. If populated, the spawner randomly selects from this list (plus the single prefab above if set). Leave empty to use only the single prefab.")]
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

		[Header("Spawn Shape")]
		[Tooltip("Grid = original grid layout. Box = random inside a box volume.")]
		[SerializeField]
		protected MassSpawnShape spawnShape = MassSpawnShape.Grid;

		[Tooltip("Box volume GameObject. Uses position and lossyScale as bounding box. Only used when spawnShape = Box.")]
		[SerializeField]
		protected Transform spawnBoxVolume;

		[Tooltip("Number of objects to spawn (Box mode only).")]
		[SerializeField]
		protected int spawnCount = 100;

		[Tooltip("Minimum spacing between spawned objects (Box mode only).")]
		[SerializeField]
		protected float minSpacing = 20f;

		[Tooltip("Deterministic seed for multiplayer. Same seed = identical results on all clients. 0 = random each time.")]
		[SerializeField]
		protected int seed = 0;

		[Header("Rotation")]

		[Range(0, 1)]
		[SerializeField]
		protected float randomRotation;

		[Header("Density (Grid mode only)")]

		[SerializeField]
		protected int numX = 20;

		[SerializeField]
		protected int numY = 2;

		[SerializeField]
		protected int numZ = 20;

		[SerializeField]
		protected float spacingX = 300;

		[SerializeField]
		protected float spacingY = 300;

		[SerializeField]
		protected float spacingZ = 300;

		[SerializeField]
		protected float minRandomOffset = 20;

		[SerializeField]
		protected float maxRandomOffset = 20;

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
		/// Create the objects in the scene
		/// </summary>
		public virtual void CreateObjects()
		{
			_cachedPrefabPool = null; // rebuild pool each spawn call
			switch (spawnShape)
			{
				case MassSpawnShape.Box:
					CreateObjectsInBox();
					break;
				default:
					CreateObjectsGrid();
					break;
			}
		}


		// ═══════════════════════════════════════════════════════════════
		//  GRID (original)
		// ═══════════════════════════════════════════════════════════════

		protected virtual void CreateObjectsGrid()
		{
			for (int i = 0; i < numX; ++i)
			{
				for (int j = 0; j < numY; ++j)
				{
					for (int k = 0; k < numZ; ++k)
					{

						Vector3 spawnPos = Vector3.zero;

						// Get a random offset for the position
						Vector3 offsetVector = Random.Range(minRandomOffset, maxRandomOffset) * Random.insideUnitSphere;

						// Calculate the spawn position
						spawnPos.x = transform.position.x - ((numX - 1) * spacingX) / 2 + (i * spacingX);
						spawnPos.y = transform.position.y - ((numY - 1) * spacingY) / 2 + (j * spacingY);
						spawnPos.z = transform.position.z - ((numZ - 1) * spacingZ) / 2 + (k * spacingZ);

						spawnPos += offsetVector;

						// Spawn objects within a radius from the center, pulling in those objects that are close to the boundary
						float distFromCenter = Vector3.Distance(spawnPos, transform.position);
						if (distFromCenter > distanceCutoff)
						{
							if (distFromCenter - distanceCutoff < maxDistMargin)
							{
								spawnPos = transform.position + (spawnPos - transform.position).normalized * distanceCutoff;
							}
							else
							{
								continue;
							}
						}

						bool isBlocked = false;
						for (int l = 0; l < spawnBlockers.Count; ++l)
						{
							if (spawnBlockers[l] != null && spawnBlockers[l].IsBlocked(spawnPos))
							{
								isBlocked = true;
								break;
							}
						}
						if (isBlocked) continue;

						// Calculate a random rotation
						Quaternion spawnRot = Quaternion.Euler(Random.Range(0, randomRotation * 360), Random.Range(0, randomRotation * 360),
																Random.Range(0, randomRotation * 360));

						// Pick a random prefab and create the object
						GameObject chosenPrefab = PickPrefabUnity();
						if (chosenPrefab == null) continue;
						GameObject temp = (GameObject)Instantiate(chosenPrefab, spawnPos, spawnRot, transform);

						// Random scale
						float scale = Random.Range(minRandomScale, maxRandomScale);
						float scaleAmount = distanceScaleCurve.Evaluate(distFromCenter / distanceCutoff);
						scale *= scaleAmount * maxScaleAmount + (1 - scaleAmount) * scaleAmount;

						temp.transform.localScale = new Vector3(scale, scale, scale);

					}
				}
			}
		}


		// ═══════════════════════════════════════════════════════════════
		//  BOX (random inside a box volume)
		// ═══════════════════════════════════════════════════════════════

		protected virtual void CreateObjectsInBox()
		{
			if (spawnBoxVolume == null)
			{
				Debug.LogWarning($"[MassObjectSpawner] spawnShape is Box but no spawnBoxVolume assigned! Falling back to Grid.");
				CreateObjectsGrid();
				return;
			}

			Vector3 boxCenter = spawnBoxVolume.position;
			Vector3 boxHalfExtents = spawnBoxVolume.lossyScale * 0.5f;

			// Deterministic RNG – seed 0 means random each game
			int s = seed != 0 ? seed : System.Environment.TickCount;
			System.Random rng = new System.Random(s);

			float minSqr = minSpacing * minSpacing;
			List<Vector3> positions = new List<Vector3>(spawnCount);
			int maxAttempts = spawnCount * 200;
			int attempts = 0;

			while (positions.Count < spawnCount && attempts < maxAttempts)
			{
				attempts++;

				Vector3 candidate = boxCenter + new Vector3(
					RngRange(rng, -boxHalfExtents.x, boxHalfExtents.x),
					RngRange(rng, -boxHalfExtents.y, boxHalfExtents.y),
					RngRange(rng, -boxHalfExtents.z, boxHalfExtents.z)
				);

				// Min-spacing check
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

				// Spawn blockers check
				bool isBlocked = false;
				for (int l = 0; l < spawnBlockers.Count; ++l)
				{
					if (spawnBlockers[l] != null && spawnBlockers[l].IsBlocked(candidate))
					{
						isBlocked = true;
						break;
					}
				}
				if (isBlocked) continue;

				positions.Add(candidate);
			}

			// Instantiate objects
			for (int i = 0; i < positions.Count; i++)
			{
				Quaternion spawnRot = Quaternion.Euler(
					RngRange(rng, 0, randomRotation * 360),
					RngRange(rng, 0, randomRotation * 360),
					RngRange(rng, 0, randomRotation * 360)
				);

				GameObject chosenPrefab = PickPrefab(rng);
				if (chosenPrefab == null) continue;
				GameObject temp = Instantiate(chosenPrefab, positions[i], spawnRot, transform);

				float distFromCenter = Vector3.Distance(positions[i], transform.position);
				float scale = RngRange(rng, minRandomScale, maxRandomScale);
				if (distanceCutoff > 0)
				{
					float scaleAmount = distanceScaleCurve.Evaluate(distFromCenter / distanceCutoff);
					scale *= scaleAmount * maxScaleAmount + (1 - scaleAmount) * scaleAmount;
				}
				temp.transform.localScale = new Vector3(scale, scale, scale);
			}

			Debug.Log($"[MassObjectSpawner] Box: spawned {positions.Count}/{spawnCount} objects (seed={s})");
		}


		// ─── Network-safe RNG helpers ────────────────────────────────
		static float RngRange(System.Random rng, float min, float max)
		{
			return min + (float)(rng.NextDouble() * (max - min));
		}

		/// <summary>
		/// Builds a merged list of all valid prefabs (single + list), cached per spawn call.
		/// </summary>
		List<GameObject> _cachedPrefabPool;
		protected List<GameObject> GetPrefabPool()
		{
			if (_cachedPrefabPool != null) return _cachedPrefabPool;
			_cachedPrefabPool = new List<GameObject>();
			if (prefab != null) _cachedPrefabPool.Add(prefab);
			if (prefabs != null)
			{
				for (int i = 0; i < prefabs.Count; i++)
				{
					if (prefabs[i] != null && !_cachedPrefabPool.Contains(prefabs[i]))
						_cachedPrefabPool.Add(prefabs[i]);
				}
			}
			return _cachedPrefabPool;
		}

		/// <summary>
		/// Pick a random prefab from the pool using deterministic RNG.
		/// </summary>
		protected GameObject PickPrefab(System.Random rng)
		{
			var pool = GetPrefabPool();
			if (pool.Count == 0) return null;
			if (pool.Count == 1) return pool[0];
			return pool[rng.Next(pool.Count)];
		}

		/// <summary>
		/// Pick a random prefab using Unity's Random (for Grid mode).
		/// </summary>
		protected GameObject PickPrefabUnity()
		{
			var pool = GetPrefabPool();
			if (pool.Count == 0) return null;
			if (pool.Count == 1) return pool[0];
			return pool[Random.Range(0, pool.Count)];
		}


		// ═══════════════════════════════════════════════════════════════
		//  GIZMOS
		// ═══════════════════════════════════════════════════════════════

		protected virtual void OnDrawGizmosSelected()
		{
			if (spawnShape == MassSpawnShape.Box && spawnBoxVolume != null)
			{
				Gizmos.color = new Color(1f, 1f, 0f, 0.3f);
				Gizmos.DrawWireCube(spawnBoxVolume.position, spawnBoxVolume.lossyScale);
			}
		}
	}
}
