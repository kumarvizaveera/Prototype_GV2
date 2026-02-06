using UnityEngine;
using System.Collections;
using VSX.Engines3D;


using TMPro;

namespace GV
{
    public class SuperBoostOrb : MonoBehaviour
    {
        [Header("Power Up Settings")]
        public bool manualTriggerOnly = false;
        public PowerUpType powerUpType = PowerUpType.SuperBoost;

        [Header("Boost Settings")]
        [Tooltip("Multiplier for normal movement speed.")]
        public float speedMultiplier = 1.2f;

        [Tooltip("Multiplier for steering/turning speed.")]
        public float steeringMultiplier = 1.0f;

        [Tooltip("Multiplier for boost speed.")]
        public float boostMultiplier = 1.2f;
        
        [Tooltip("How long the boost lasts in seconds.")]
        public float boostDuration = 5.0f;

        [Header("Pickup Settings")]
        [Tooltip("If true, the orb disappears when touched.")]
        public bool consumeOnPickup = true;

        [Header("UI")]
        public TMP_Text timerText;
        public string timerFormat = "Boost: {0:0.0}";
        
        [Tooltip("If > 0, the orb reappears after this many seconds.")]
        public float respawnTime = -1f;

        [Header("Feedback")]
        [Tooltip("Optional sound to play on pickup (using AudioSource at position).")]
        public AudioClip pickupSound;
        [Tooltip("Optional: Instantiate this GameObject when collected (e.g. for audio prefab).")]
        public GameObject pickupSoundObject;
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
            if (manualTriggerOnly) return; 

            // Find the vehicle root
            Rigidbody rb = other.attachedRigidbody;
            GameObject target = rb ? rb.gameObject : other.gameObject;

            Apply(target);
        }

        public void Apply(GameObject target)
        {
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
            // Register Collection
            if (PowerUpManager.Instance != null)
            {
                PowerUpManager.Instance.RegisterCollection(powerUpType);
            }


            
            // Pass UI settings
            handler.SetUI(timerText, timerFormat);

            // Activate Boost
            handler.ActivateSuperBoost(speedMultiplier, steeringMultiplier, boostMultiplier, boostDuration);

            // FX
            if (pickupSound != null) AudioSource.PlayClipAtPoint(pickupSound, transform.position);
            if (pickupSoundObject != null) Instantiate(pickupSoundObject, transform.position, Quaternion.identity);
            if (pickupEffect != null) Instantiate(pickupEffect, transform.position, Quaternion.identity);

            // Consume (only if not manual, as manual means we are a shared component on a mystery sphere)
            if (!manualTriggerOnly && consumeOnPickup)
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
