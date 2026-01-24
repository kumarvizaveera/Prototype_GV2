using UnityEngine;
using VSX.Engines3D;
using VSX.ResourceSystem;

namespace GV.Scripts
{
    public class CheckpointFuel : MonoBehaviour
    {
        [Header("Settings")]
        [Tooltip("Amount of boost fuel to add when collected.")]
        public float fuelAmount = 10f;

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

            if (engines.BoostResourceHandlers != null)
            {
                foreach (var handler in engines.BoostResourceHandlers)
                {
                    if (handler != null && handler.resourceContainer != null)
                    {
                        handler.resourceContainer.AddRemove(fuelAmount);
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

            // Visual/Audio Feedback
            if (collectionEffect != null)
            {
                Instantiate(collectionEffect, transform.position, transform.rotation);
            }

            if (collectionAudio != null && collectionAudio.clip != null)
            {
                collectionAudio.Play();
            }

            // Disable logic
            if (oneTimeUse)
            {
                if (m_Collider != null) m_Collider.enabled = false;
                
                // Hide visuals if necessary, but keep object alive if audio is playing on it
                // For now, assuming audio might be 2D or on a child. 
                // If we destroy, we lose audio.
                
                // If we have a mesh renderer, disable it
                var renderer = GetComponent<Renderer>();
                if (renderer != null) renderer.enabled = false;
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
