using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace GV
{
    public class PowerUpManager : MonoBehaviour
    {
        public static PowerUpManager Instance { get; private set; }

        private HashSet<PowerUpType> collectedPowerUps = new HashSet<PowerUpType>();

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                // Optional: Don't destroy on load if you want it to persist across scene changes, 
                // but for a race, usually it's per-scene. We'll leave it as per-scene for now.
            }
            else
            {
                Destroy(gameObject);
            }
        }

        public void RegisterCollection(PowerUpType type)
        {
            if (type == PowerUpType.None) return;

            if (!collectedPowerUps.Contains(type))
            {
                collectedPowerUps.Add(type);
                Debug.Log($"[PowerUpManager] Collected: {type}");
            }
        }

        public bool HasCollected(PowerUpType type)
        {
            return collectedPowerUps.Contains(type);
        }

        public void ResetCollection()
        {
            collectedPowerUps.Clear();
            Debug.Log("[PowerUpManager] Collection reset.");
        }

        // Returns a random power-up that hasn't been collected yet.
        // Returns PowerUpType.None if all have been collected.
        public PowerUpType GetRandomUncollectedPower(List<PowerUpType> availableTypes)
        {
            List<PowerUpType> candidates = new List<PowerUpType>();

            foreach (var type in availableTypes)
            {
                if (!HasCollected(type) && type != PowerUpType.None)
                {
                    candidates.Add(type);
                }
            }

            if (candidates.Count == 0)
            {
                return PowerUpType.None;
            }

            int index = Random.Range(0, candidates.Count);
            return candidates[index];
        }
    }
}
