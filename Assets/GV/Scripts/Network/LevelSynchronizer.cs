using Fusion;
using UnityEngine;

namespace GV.Network
{
    public class LevelSynchronizer : NetworkBehaviour
    {
        public static LevelSynchronizer Instance { get; private set; }

        [Networked] public int LevelSeed { get; set; }

        public override void Spawned()
        {
            if (Instance != null && Instance != this)
            {
                Runner.Despawn(Object);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            if (Object.HasStateAuthority)
            {
                LevelSeed = System.Environment.TickCount;
                Debug.Log($"[LevelSynchronizer] Generated Seed: {LevelSeed}");
            }
            else
            {
                Debug.Log($"[LevelSynchronizer] Received Seed: {LevelSeed}");
            }
        }
    }
}
