using UnityEngine;
using System.Collections;
using VSX.UniversalVehicleCombat;

namespace GV.Network
{
    /// <summary>
    /// Waits for the LevelSynchronizer seed and then initializes the MassObjectSpawner.
    /// This script must be attached to the same GameObject as MassObjectSpawner.
    /// </summary>
    [RequireComponent(typeof(MassObjectSpawner))]
    public class NetworkedSpawnerInitializer : MonoBehaviour
    {
        private MassObjectSpawner spawner;

        private void Awake()
        {
            spawner = GetComponent<MassObjectSpawner>();
        }

        private IEnumerator Start()
        {
            // Wait until the LevelSynchronizer is ready and has a valid seed
            while (LevelSynchronizer.Instance == null)
            {
                yield return null;
            }

            // Combine the global level seed with a unique offset for this spawner
            // specific_offset can be based on position to ensure different spawners have different seeds
            int seed = LevelSynchronizer.Instance.LevelSeed + transform.position.GetHashCode();
            Random.InitState(seed);

            Debug.Log($"[NetworkedSpawnerInitializer] Initializing spawner with seed: {seed}");
            spawner.CreateObjects();
        }
    }
}
