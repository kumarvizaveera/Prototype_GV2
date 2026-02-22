using UnityEngine;
using TMPro;
using VSX.Weapons;
using Fusion;
using GV.Scripts;

namespace GV.Scripts
{
    public class AstraMissileRefill : MonoBehaviour
    {
        [Header("Settings")]
        [Tooltip("The label of the missile to refill (must match the label in MissileCycleControllerDynamic).")]
        public string missileLabel = "Astra";

        [Tooltip("The amount of ammo to add per interval.")]
        public int amountPerInterval = 1;

        [Tooltip("Time in seconds between refills while inside the zone.")]
        public float refillInterval = 2.0f;

        [Tooltip("Sound to play on each refill (optional).")]
        public AudioClip refillSound;

        [Header("UI")]
        [Tooltip("Text component to display recharging status and countdown.")]
        public TMP_Text feedbackText;

        [Tooltip("Message to display before the countdown.")]
        public string refillMessage = "Astra Energy is recharging...";

        [Tooltip("Text component to display when ammo capacity is full.")]
        public TMP_Text capacityFullText;

        [Tooltip("Message to display when ammo is at full capacity.")]
        public string capacityFullMessage = "Character Astra energy is full";

        private MissileCycleControllerDynamic activeController;
        private float timer;
        private Collider triggerCollider;

        private void OnValidate()
        {
            // Try to find an existing trigger
            if (triggerCollider == null)
            {
                Collider[] colliders = GetComponents<Collider>();
                foreach (var col in colliders)
                {
                    if (col.isTrigger)
                    {
                        triggerCollider = col;
                        break;
                    }
                }
            }
        }

        private void Awake()
        {
             // Find any existing trigger collider
            Collider[] colliders = GetComponents<Collider>();
            foreach (var col in colliders)
            {
                if (col.isTrigger)
                {
                    triggerCollider = col;
                    break;
                }
            }

            // If no trigger exists, add a default SphereCollider
            if (triggerCollider == null)
            {
                triggerCollider = gameObject.AddComponent<SphereCollider>();
                triggerCollider.isTrigger = true;
            }
            
            if (feedbackText != null) feedbackText.gameObject.SetActive(false);
            if (capacityFullText != null) capacityFullText.gameObject.SetActive(false);
        }

        /// <summary>
        /// Checks if the colliding object belongs to the local player's ship.
        /// Only the local player's ship has InputAuthority on this machine.
        /// </summary>
        private bool IsLocalPlayer(Collider other)
        {
            // Search from root to find the NetworkObject (ships are complex hierarchies)
            Transform root = other.transform.root;
            NetworkObject netObj = root.GetComponent<NetworkObject>();
            if (netObj == null && other.attachedRigidbody != null)
            {
                netObj = other.attachedRigidbody.GetComponent<NetworkObject>();
            }
            if (netObj == null)
            {
                netObj = other.GetComponentInParent<NetworkObject>();
            }

            // If no NetworkObject found, allow it (offline/local testing)
            if (netObj == null) return true;

            return netObj.HasInputAuthority;
        }

        private void OnTriggerEnter(Collider other)
        {
            Debug.Log($"[AstraRefill] OnTriggerEnter with {other.name}");

            // Only react to the local player's ship — ignore remote ships
            if (!IsLocalPlayer(other))
            {
                Debug.Log($"[AstraRefill] Ignoring non-local player {other.name}");
                return;
            }

            MissileCycleControllerDynamic controller = null;

            // Method 1: Check Attached Rigidbody (Most reliable for vehicles)
            if (other.attachedRigidbody != null)
            {
                controller = other.attachedRigidbody.GetComponent<MissileCycleControllerDynamic>();
                if (controller == null) controller = other.attachedRigidbody.GetComponentInChildren<MissileCycleControllerDynamic>();
            }

            // Method 2: Check Component directly
            if (controller == null)
            {
                controller = other.GetComponent<MissileCycleControllerDynamic>();
            }

            // Method 3: Check Parent
            if (controller == null)
            {
                controller = other.GetComponentInParent<MissileCycleControllerDynamic>();
            }

             // Method 4: Check Root (Last resort)
            if (controller == null && other.transform.root != other.transform)
            {
                controller = other.transform.root.GetComponentInChildren<MissileCycleControllerDynamic>();
            }

            if (controller != null)
            {
                Debug.Log($"[AstraRefill] Found Controller on {controller.gameObject.name}");
                activeController = controller;
                timer = refillInterval;
                UpdateFeedbackUI();
            }
            else
            {
                 Debug.Log($"[AstraRefill] No MissileCycleControllerDynamic found on {other.name}, its Rigidbody, or parents.");
            }
        }

        private void OnTriggerExit(Collider other)
        {
             Debug.Log($"[AstraRefill] OnTriggerExit with {other.name}");

            // Only react to the local player's ship
            if (!IsLocalPlayer(other)) return;

            MissileCycleControllerDynamic controller = null;

            if (other.attachedRigidbody != null)
            {
                controller = other.attachedRigidbody.GetComponent<MissileCycleControllerDynamic>();
                if (controller == null) controller = other.attachedRigidbody.GetComponentInChildren<MissileCycleControllerDynamic>();
            }

            if (controller == null) controller = other.GetComponent<MissileCycleControllerDynamic>();
            if (controller == null) controller = other.GetComponentInParent<MissileCycleControllerDynamic>();
             if (controller == null && other.transform.root != other.transform) controller = other.transform.root.GetComponentInChildren<MissileCycleControllerDynamic>();

            if (controller != null && controller == activeController)
            {
                Debug.Log($"[AstraRefill] Exited active controller zone.");
                activeController = null;
                if (feedbackText != null) feedbackText.gameObject.SetActive(false);
                if (capacityFullText != null) capacityFullText.gameObject.SetActive(false);
            }
        }

        private void Update()
        {
            if (activeController != null)
            {
                if (IsAmmoFull())
                {
                    // Capacity is full — hide refill text, show full text
                    if (feedbackText != null) feedbackText.gameObject.SetActive(false);
                    if (capacityFullText != null)
                    {
                        capacityFullText.gameObject.SetActive(true);
                        capacityFullText.text = capacityFullMessage;
                    }
                }
                else
                {
                    // Still refilling — show refill text, hide full text
                    if (capacityFullText != null) capacityFullText.gameObject.SetActive(false);

                    timer -= Time.deltaTime;
                    UpdateFeedbackUI();

                    if (timer <= 0f)
                    {
                        PerformRefill();
                        timer = refillInterval;
                    }
                }
            }
        }

        /// <summary>
        /// Checks if the ammo resource container for the target missile is at full capacity.
        /// </summary>
        private bool IsAmmoFull()
        {
            if (activeController == null) return false;

            foreach (var entry in activeController.missileMounts)
            {
                if (entry.label.Equals(missileLabel, System.StringComparison.OrdinalIgnoreCase))
                {
                    if (entry.mount != null && entry.mount.MountedModule() != null)
                    {
                        Weapon weapon = entry.mount.MountedModule().GetComponent<Weapon>();
                        if (weapon != null)
                        {
                            foreach (var handler in weapon.ResourceHandlers)
                            {
                                if (handler.unitResourceChange < 0 && !handler.perSecond && handler.resourceContainer != null)
                                {
                                    return handler.resourceContainer.IsFull;
                                }
                            }
                        }
                    }
                    break;
                }
            }
            return false;
        }

        private void UpdateFeedbackUI()
        {
            if (feedbackText != null)
            {
                feedbackText.gameObject.SetActive(true);
                // "Astra Energy is recharging... 2"
                feedbackText.text = $"{refillMessage} {Mathf.Ceil(timer)}";
            }
        }

        private void PerformRefill()
        {
            if (activeController != null)
            {
                Debug.Log($"[AstraRefill] Performing Refill for {activeController.gameObject.name}");
                activeController.AddAmmo(missileLabel, amountPerInterval);

                if (refillSound != null)
                {
                    AudioSource.PlayClipAtPoint(refillSound, transform.position);
                }
            }
        }

    }
}
