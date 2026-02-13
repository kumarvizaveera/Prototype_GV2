using UnityEngine;
using TMPro;
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
        }

        private void OnTriggerEnter(Collider other)
        {
            Debug.Log($"[AstraRefill] OnTriggerEnter with {other.name}");

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
            }
        }

        private void Update()
        {
            if (activeController != null)
            {
                timer -= Time.deltaTime;
                UpdateFeedbackUI();

                if (timer <= 0f)
                {
                    PerformRefill();
                    timer = refillInterval;
                }
            }
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
