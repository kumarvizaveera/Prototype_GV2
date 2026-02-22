using UnityEngine;
using System.Collections;
using GV.Scripts;

namespace GV.Network
{
    /// <summary>
    /// Waits for the LevelSynchronizer seed and then initializes the AstraRefillSpawner.
    /// Ensures all clients spawn Astra Refills at the same positions using a shared seed.
    ///
    /// Usage:
    ///   1. Attach this script to the same GameObject as AstraRefillSpawner.
    ///   2. Enable "Wait For Network Seed" on the AstraRefillSpawner component.
    ///   3. Ensure LevelSynchronizer is present in the scene (spawned via Fusion).
    /// </summary>
    [RequireComponent(typeof(AstraRefillSpawner))]
    public class NetworkedAstraRefillInitializer : MonoBehaviour
    {
        private AstraRefillSpawner spawner;

        private void Awake()
        {
            spawner = GetComponent<AstraRefillSpawner>();

            // Safety: force the flag on so the spawner doesn't auto-spawn before seed is ready
            spawner.waitForNetworkSeed = true;
        }

        private IEnumerator Start()
        {
            // Wait until the LevelSynchronizer is ready and has a valid seed
            while (LevelSynchronizer.Instance == null)
            {
                yield return null;
            }

            // Combine the global level seed with a unique offset for this spawner
            // Position hash ensures different spawners get different (but deterministic) seeds
            int seed = LevelSynchronizer.Instance.LevelSeed + transform.position.GetHashCode();
            Random.InitState(seed);

            Debug.Log($"[NetworkedAstraRefillInitializer] Initializing spawner with seed: {seed} on {gameObject.name}");
            spawner.SpawnObjects();
        }
    }
}
