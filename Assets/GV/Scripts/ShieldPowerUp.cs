using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VSX.Health;
using GV;
using GV.Network;
using TMPro;

namespace GV.PowerUps
{
    public class ShieldPowerUp : MonoBehaviour
    {
        [Header("Power Up Settings")]
        public bool manualTriggerOnly = false;
        public PowerUpType powerUpType = PowerUpType.Shield;

        [Header("Settings")]
        [Tooltip("Play this audio clip when collected.")]
        public AudioClip collectSound;

        [Tooltip("Optional: Instantiate this GameObject when collected (e.g. for audio prefab).")]
        public GameObject collectEffectObject;

        [Tooltip("Duration of the shield effect in seconds.")]
        public float duration = 10f;
        
        [Tooltip("If true, destroy this object after collection. If false, just disable it.")]
        public bool destroyOnCollect = true;

        [Header("UI")]
        public TMP_Text timerText;
        public string timerFormat = "Shield: {0:0.0}";

        [Header("Debug")]
        public bool debugLogs = true;

        private void Start()
        {
            if (debugLogs) Debug.Log($"[ShieldPowerUp] Script started on {gameObject.name}. Manual: {manualTriggerOnly}");
            if (timerText != null) timerText.gameObject.SetActive(false);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (debugLogs) Debug.Log($"[ShieldPowerUp] OnTriggerEnter. Manual: {manualTriggerOnly}. Other: {other.name}");
            if (manualTriggerOnly) return;

            // Find the root object (likely the ship controller)
            Rigidbody rb = other.attachedRigidbody;
            GameObject target = rb ? rb.gameObject : other.gameObject;

            Apply(target);
        }

        public void Apply(GameObject target)
        {
            // Register Collection
            if (PowerUpManager.Instance != null)
            {
                PowerUpManager.Instance.RegisterCollection(powerUpType);
            }

            // Look for EnergyShieldController in children
            EnergyShieldController shieldController = target.GetComponentInChildren<EnergyShieldController>(true);

            if (shieldController != null)
            {
                if (debugLogs) Debug.Log($"[ShieldPowerUp] Found shield controller on {target.name}");

                shieldController.SetUI(timerText, timerFormat);

                // Enable the game object containing the shield controller

                // Enable the game object containing the shield controller
                if (PowerSphereMasterController.Instance != null)
                {
                    duration = PowerSphereMasterController.Instance.shieldSettings.duration;
                }

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
                
                // Play sound/VFX only for the local player to prevent double sounds in multiplayer
                if (NetworkAudioHelper.IsLocalPlayer(target))
                {
                    if (collectSound != null)
                    {
                        AudioSource.PlayClipAtPoint(collectSound, transform.position);
                    }

                    if (collectEffectObject != null)
                    {
                        Instantiate(collectEffectObject, transform.position, Quaternion.identity);
                    }
                }

                // Remove the powerup
                if (!manualTriggerOnly)
                {
                    if (destroyOnCollect)
                    {
                        Destroy(gameObject);
                    }
                    else
                    {
                        gameObject.SetActive(false);
                    }
                }
            }
            else
            {
                 if (debugLogs) Debug.LogWarning($"[ShieldPowerUp] No EnergyShieldController found on {target.name} or its children.");
            }
        }
    }
}
