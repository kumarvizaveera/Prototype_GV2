using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VSX.Health;

namespace GV.PowerUps
{
    public class ShieldPowerUp : MonoBehaviour
    {
        [Header("Settings")]
        [Tooltip("Play this audio clip when collected.")]
        public AudioClip collectSound;

        [Tooltip("Duration of the shield effect in seconds.")]
        public float duration = 10f;
        
        [Tooltip("If true, destroy this object after collection. If false, just disable it.")]
        public bool destroyOnCollect = true;

        [Header("Debug")]
        public bool debugLogs = true;

        private void OnTriggerEnter(Collider other)
        {
            // Find the root object (likely the ship controller)
            Rigidbody rb = other.attachedRigidbody;
            GameObject target = rb ? rb.gameObject : other.gameObject;

            // Look for EnergyShieldController in children
            EnergyShieldController shieldController = target.GetComponentInChildren<EnergyShieldController>(true);

            if (shieldController != null)
            {
                if (debugLogs) Debug.Log($"[ShieldPowerUp] Found shield controller on {target.name}");

                // Enable the game object containing the shield controller
                if (!shieldController.IsShieldActive)
                {
                    shieldController.ActivateShield(duration);
                    if (debugLogs) Debug.Log($"[ShieldPowerUp] Activated shield on {target.name} for {duration} seconds");
                }
                else
                {
                    // If already active, replenish duration
                    shieldController.ActivateShield(duration);
                     if (debugLogs) Debug.Log($"[ShieldPowerUp] Refreshed shield on {target.name} for {duration} seconds");
                }
                
                // Play sound if assigned
                if (collectSound != null)
                {
                    AudioSource.PlayClipAtPoint(collectSound, transform.position);
                }

                // Remove the powerup
                if (destroyOnCollect)
                {
                    Destroy(gameObject);
                }
                else
                {
                    gameObject.SetActive(false);
                }
            }
            else
            {
                 if (debugLogs) Debug.LogWarning($"[ShieldPowerUp] No EnergyShieldController found on {target.name} or its children.");
            }
        }
    }
}
