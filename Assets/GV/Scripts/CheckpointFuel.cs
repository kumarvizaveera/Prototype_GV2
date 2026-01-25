using UnityEngine;
using VSX.Engines3D;
using VSX.ResourceSystem;

namespace GV.Scripts
{
    public class CheckpointFuel : MonoBehaviour
    {
        [Header("Fuel Settings")]
        [Tooltip("Standard amount of fuel given.")]
        public float baseFuel = 10f;

        [Tooltip("Random variance added as a percentage of Base Fuel (e.g. 0.1 = +0-10%).")]
        [Range(0f, 1f)]
        public float fluxPercentage = 0.05f;

        [Header("Jackpot")]
        [Tooltip("Is this a rare lucky checkpoint?")]
        public bool isJackpot = false;

        [Tooltip("Amount of fuel given if this is a Jackpot.")]
        public float jackpotFuel = 100f;

        [Tooltip("Destroy/Disable object after collection?")]
        public bool oneTimeUse = true;

        [Header("Feedback")]
        public GameObject collectionEffect;
        public AudioSource collectionAudio;

        private bool collected = false;
        private Collider m_Collider;

        private void Start()
        {
            m_Collider = GetComponent<Collider>();
            if (m_Collider != null)
            {
                // Ensure it's a trigger so we don't crash into it
                m_Collider.isTrigger = true;
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (collected) return;

            // Look for vehicle engines on the collider's object or its parents
            VehicleEngines3D engines = other.GetComponent<VehicleEngines3D>();
            if (engines == null)
            {
                engines = other.GetComponentInParent<VehicleEngines3D>();
            }

            // If we found engines, try to give fuel
            if (engines != null)
            {
                GiveFuel(engines);
            }
        }

        private void GiveFuel(VehicleEngines3D engines)
        {
            bool fuelGiven = false;

            float amountToAdd = baseFuel;

            if (isJackpot)
            {
                amountToAdd = jackpotFuel;
            }
            else
            {
                // Add a small random flux (always positive or zero based on user request "Bonus fuel")
                // flux is % of base. e.g. 10 * 0.05 = 0.5. Result 10 to 10.5
                float flux = baseFuel * UnityEngine.Random.Range(0f, fluxPercentage);
                amountToAdd += flux;
            }

            if (engines.BoostResourceHandlers != null)
            {
                foreach (var handler in engines.BoostResourceHandlers)
                {
                    if (handler != null && handler.resourceContainer != null)
                    {
                        handler.resourceContainer.AddRemove(amountToAdd);
                        fuelGiven = true;
                    }
                }
            }

            if (fuelGiven)
            {
                Collect();
            }
        }

        private void Collect()
        {
            collected = true;

            // Visual Feedback
            if (collectionEffect != null)
            {
                Instantiate(collectionEffect, transform.position, transform.rotation);
            }

            float destroyDelay = 0f;

            // Audio Feedback
            if (collectionAudio != null && collectionAudio.clip != null)
            {
                collectionAudio.Play();
                // If the audio source is on this object or a child, wait for it to finish before destroying
                if (collectionAudio.gameObject == gameObject || collectionAudio.transform.IsChildOf(transform))
                {
                    destroyDelay = collectionAudio.clip.length;
                }
            }

            // Disable logic
            if (oneTimeUse)
            {
                if (m_Collider != null) m_Collider.enabled = false;
                
                // Hide ALL visuals recursively (Mesh, UI, TextMeshPro, etc.)
                foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = false;
                foreach (var c in GetComponentsInChildren<Canvas>()) c.enabled = false;
                // Use reflection or non-generic GetComponent to safely handle TMP if present/not-present without hard dependency issues, 
                // but since we know TMP is in project, we can try generic or just finding by type name if avoiding using TMPro namespace.
                // Assuming TMPro usage is fine as per project context.
                foreach (var t in GetComponentsInChildren<TMPro.TMP_Text>()) t.enabled = false;

                // Cleanup
                Destroy(gameObject, destroyDelay);
            }
        }
        
        public void ResetCheckpoint()
        {
            collected = false;
            if (m_Collider != null) m_Collider.enabled = true;
            var renderer = GetComponent<Renderer>();
            if (renderer != null) renderer.enabled = true;
        }
    }
}
