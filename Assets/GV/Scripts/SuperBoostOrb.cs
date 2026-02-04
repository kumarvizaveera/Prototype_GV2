using UnityEngine;
using System.Collections;
using VSX.Engines3D;

namespace GV
{
    public class SuperBoostOrb : MonoBehaviour
    {
        [Header("Boost Settings")]
        [Tooltip("The speed multiplier to apply (e.g. 2.0 = 2x speed).")]
        public float boostMultiplier = 2.0f;
        
        [Tooltip("How long the boost lasts in seconds.")]
        public float boostDuration = 5.0f;

        [Header("Pickup Settings")]
        [Tooltip("If true, the orb disappears when touched.")]
        public bool consumeOnPickup = true;
        
        [Tooltip("If > 0, the orb reappears after this many seconds.")]
        public float respawnTime = -1f;

        [Header("Feedback")]
        [Tooltip("Optional sound to play on pickup (using AudioSource at position).")]
        public AudioClip pickupSound;
        [Tooltip("Optional particle effect to spawn on pickup.")]
        public GameObject pickupEffect;

        private Collider m_Collider;
        private Renderer[] m_Renderers;

        private void Awake()
        {
            m_Collider = GetComponent<Collider>();
            m_Renderers = GetComponentsInChildren<Renderer>();
        }

        private void OnTriggerEnter(Collider other)
        {
            // Find the vehicle root
            Rigidbody rb = other.attachedRigidbody;
            GameObject target = rb ? rb.gameObject : other.gameObject;

            // Look for the handler
            var handler = target.GetComponent<AircraftSuperBoostHandler>();
            
            // If not found on root, try children (common in some setups)
            if (handler == null) handler = target.GetComponentInChildren<AircraftSuperBoostHandler>();

            if (handler != null)
            {
                ApplyPickup(handler);
            }
        }

        private void ApplyPickup(AircraftSuperBoostHandler handler)
        {
            // Activate Boost
            handler.ActivateSuperBoost(boostMultiplier, boostDuration);

            // FX
            if (pickupSound != null) AudioSource.PlayClipAtPoint(pickupSound, transform.position);
            if (pickupEffect != null) Instantiate(pickupEffect, transform.position, Quaternion.identity);

            // Consume
            if (consumeOnPickup)
            {
                if (respawnTime > 0)
                {
                    StartCoroutine(RespawnRoutine());
                }
                else
                {
                    gameObject.SetActive(false);
                }
            }
        }

        private IEnumerator RespawnRoutine()
        {
            // Hide visuals and disable collider
            SetVisuals(false);

            yield return new WaitForSeconds(respawnTime);

            // Show visuals and enable collider
            SetVisuals(true);
        }

        private void SetVisuals(bool active)
        {
            if (m_Collider) m_Collider.enabled = active;
            foreach (var r in m_Renderers) r.enabled = active;
        }
    }
}
