using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VSX.RadarSystem;
using GV;
using TMPro;

namespace GV.PowerUps
{
    public class InvisibilityPowerUp : MonoBehaviour
    {
        [Header("Power Up Settings")]
        public bool manualTriggerOnly = false;
        public PowerUpType powerUpType = PowerUpType.Invisibility;

        [Header("Settings")]
        [Tooltip("The glass material to apply to the aircraft.")]
        public Material glassMaterial;

        [Tooltip("Duration of the invisibility effect in seconds. Set to 0 for infinite.")]
        public float duration = 10f;

        [Tooltip("If true, the effect reverts when the aircraft exits the trigger volume (instead of time-based).")]
        public bool revertOnExit = false;

        [Header("Feedback")]
        public AudioClip collectSound;
        [Tooltip("Optional: Instantiate this GameObject when collected (e.g. for audio prefab).")]
        public GameObject collectEffectObject;

        [Header("UI")]
        public TMP_Text timerText;
        public string timerFormat = "Invisibility: {0:0.0}";

        [Header("Debug")]
        public bool debugLogs = true;



        private void Start()
        {
            if (debugLogs) Debug.Log($"[InvisibilityPowerUp] Script started on {gameObject.name}. Manual: {manualTriggerOnly}");
            if (timerText != null) timerText.gameObject.SetActive(false);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (debugLogs) Debug.Log($"[InvisibilityPowerUp] OnTriggerEnter. Manual: {manualTriggerOnly}. Other: {other.name}");
            if (manualTriggerOnly) return;

            // Try to find the root aircraft object (assuming RigidBody is on the root)
            Rigidbody rb = other.attachedRigidbody;
            GameObject target = rb ? rb.gameObject : other.gameObject;

            if (debugLogs) Debug.Log($"[InvisibilityPowerUp] Trigger entered by: {target.name}");

            // Apply the effect
            Apply(target);
        }

        public void Apply(GameObject target)
        {
             // Register Collection
            if (PowerUpManager.Instance != null)
            {
                PowerUpManager.Instance.RegisterCollection(powerUpType);
            }

            // Feedback
            if (collectSound != null) AudioSource.PlayClipAtPoint(collectSound, transform.position);
            if (collectEffectObject != null) Instantiate(collectEffectObject, transform.position, Quaternion.identity);

            ApplyInvisibility(target);
        }

        private void OnTriggerExit(Collider other)
        {
            if (manualTriggerOnly) return;
            if (!revertOnExit) return;

            Rigidbody rb = other.attachedRigidbody;
            GameObject target = rb ? rb.gameObject : other.gameObject;
            
            var handler = target.GetComponent<InvisibilityHandler>();
            if (!handler) handler = target.GetComponentInChildren<InvisibilityHandler>();
            
            if (handler != null)
            {
                handler.RevertInvisibility();
            }
        }

        public void ApplyInvisibility(GameObject target)
        {
            var handler = target.GetComponent<InvisibilityHandler>();
            if (handler == null) handler = target.AddComponent<InvisibilityHandler>();
            
            // Pass UI settings
            handler.SetUI(timerText, timerFormat);

            // Activate Invisibility
            handler.ActivateInvisibility(glassMaterial, duration, revertOnExit);

            if (debugLogs) Debug.Log($"[InvisibilityPowerUp] Delegated invisibility to {target.name}");
        }
    }
}
