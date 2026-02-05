using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VSX.RadarSystem;
using GV;

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

        [Header("Debug")]
        public bool debugLogs = true;

        // Cache to store original materials for reversion
        // Key: Renderer, Value: Array of original materials
        private Dictionary<Renderer, Material[]> originalMaterialsCache = new Dictionary<Renderer, Material[]>();
        
        // Cache for Trackable component to restore state
        // Key: Trackable, Value: Original Activated State
        private Dictionary<Trackable, bool> originalTrackableStates = new Dictionary<Trackable, bool>();

        private Coroutine revertCoroutine;

        private void Start()
        {
            if (debugLogs) Debug.Log($"[InvisibilityPowerUp] Script started on {gameObject.name}. Manual: {manualTriggerOnly}");
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

            // Handle duration logic if not revertOnExit
            if (!revertOnExit && duration > 0)
            {
                if (revertCoroutine != null) StopCoroutine(revertCoroutine);
                revertCoroutine = StartCoroutine(RevertAfterDelay(target, duration));
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (manualTriggerOnly) return;
            if (!revertOnExit) return;

            Rigidbody rb = other.attachedRigidbody;
            GameObject target = rb ? rb.gameObject : other.gameObject;

            if (IsTargetInvisible(target))
            {
                RevertInvisibility(target);
            }
        }

        public void ApplyInvisibility(GameObject target)
        {
            if (glassMaterial == null)
            {
                Debug.LogError("[InvisibilityPowerUp] No Glass Material assigned!");
                return;
            }

            // --- Visual Invisibility ---
            Renderer[] renderers = target.GetComponentsInChildren<Renderer>();
            
            foreach (Renderer r in renderers)
            {
                if (r == null) continue;
                
                // Skip particles or trails if desired, but for now we do everything that is a MeshRenderer or SkinnedMeshRenderer
                if (!(r is MeshRenderer || r is SkinnedMeshRenderer)) continue;

                // Cache original materials if not already cached
                if (!originalMaterialsCache.ContainsKey(r))
                {
                    originalMaterialsCache[r] = r.sharedMaterials;
                    
                    // Only apply if we just cached (first time hit)
                    // Create an array of the glass material matching the length of the original materials
                    Material[] newMats = new Material[r.sharedMaterials.Length];
                    for (int i = 0; i < newMats.Length; i++)
                    {
                        newMats[i] = glassMaterial;
                    }

                    r.sharedMaterials = newMats;
                }
            }

            // --- Gameplay Invisibility (AI/Radar) ---
            var trackable = target.GetComponent<Trackable>();
            // If not on root, try children or parent
            if (trackable == null) trackable = target.GetComponentInChildren<Trackable>();
            if (trackable == null) trackable = target.GetComponentInParent<Trackable>();

            if (trackable != null)
            {
                // Always use the root trackable to ensure we affect the whole ship
                trackable = trackable.RootTrackable; 
                
                // Only cache and modify if we haven't already processed this trackable
                if (!originalTrackableStates.ContainsKey(trackable))
                {
                    originalTrackableStates[trackable] = trackable.Activated;
                    trackable.SetActivation(false); // Make invisible to Radar/AI
                    if (debugLogs) Debug.Log($"[InvisibilityPowerUp] Disabled tracking for {trackable.name}");
                }
            }

            if (debugLogs) Debug.Log($"[InvisibilityPowerUp] Applied glass material to {renderers.Length} renderers on {target.name}");
        }

        public void RevertInvisibility(GameObject target)
        {
            // --- Revert Visuals ---
            Renderer[] renderers = target.GetComponentsInChildren<Renderer>();
            int revertCount = 0;

            foreach (Renderer r in renderers)
            {
                if (originalMaterialsCache.TryGetValue(r, out Material[] originalMats))
                {
                    r.sharedMaterials = originalMats;
                    revertCount++;
                }
            }
            
            // Clear cache 
             foreach (Renderer r in renderers)
            {
                originalMaterialsCache.Remove(r);
            }

            // --- Revert Gameplay Invisibility ---
            var trackable = target.GetComponent<Trackable>();
            if (trackable == null) trackable = target.GetComponentInChildren<Trackable>();
            if (trackable == null) trackable = target.GetComponentInParent<Trackable>();
            
            if (trackable != null)
            {
                trackable = trackable.RootTrackable;

                if (originalTrackableStates.TryGetValue(trackable, out bool originalState))
                {
                    // Restore original state
                    trackable.SetActivation(originalState);

                    // Force re-registration
                    if (trackable.enabled && trackable.gameObject.activeInHierarchy)
                    {
                        StartCoroutine(ForceReRegistration(trackable));
                    }
                    
                    originalTrackableStates.Remove(trackable);
                    if (debugLogs) Debug.Log($"[InvisibilityPowerUp] Restored tracking for {trackable.name}");
                }
            }

            if (debugLogs) Debug.Log($"[InvisibilityPowerUp] Reverted materials on {revertCount} renderers.");
        }

        private IEnumerator ForceReRegistration(Trackable trackable)
        {
            trackable.enabled = false;
            yield return null; // Wait one frame
            trackable.enabled = true;
        }

        private IEnumerator RevertAfterDelay(GameObject target, float delay)
        {
            yield return new WaitForSeconds(delay);
            RevertInvisibility(target);
            revertCoroutine = null;
        }

        // Helper to check if we are currently tracking this target
        private bool IsTargetInvisible(GameObject target)
        {
            // Simply check if any of its renderers are in our cache
            Renderer[] renderers = target.GetComponentsInChildren<Renderer>();
            foreach (var r in renderers)
            {
                if (originalMaterialsCache.ContainsKey(r)) return true;
            }
            return false;
        }
    }
}
