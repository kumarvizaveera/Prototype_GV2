using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VSX.RadarSystem;
using TMPro;

namespace GV
{
    public class InvisibilityHandler : MonoBehaviour
    {
        // Cache to store original materials for reversion
        private Dictionary<Renderer, Material[]> originalMaterialsCache = new Dictionary<Renderer, Material[]>();
        
        // Cache for Trackable component to restore state
        private Dictionary<Trackable, bool> originalTrackableStates = new Dictionary<Trackable, bool>();

        private Coroutine revertCoroutine;
        
        private TMP_Text timerText;
        private string timerFormat = "Invisibility: {0:0.0}";

        public void SetUI(TMP_Text timerText, string timerFormat)
        {
            this.timerText = timerText;
            this.timerFormat = timerFormat;
            if (this.timerText != null) this.timerText.gameObject.SetActive(false);
        }

        public void ActivateInvisibility(Material glassMaterial, float duration, bool revertOnExit)
        {
            ApplyInvisibility(glassMaterial);

            if (!revertOnExit && duration > 0)
            {
                if (revertCoroutine != null) StopCoroutine(revertCoroutine);
                revertCoroutine = StartCoroutine(RevertAfterDelay(duration));
            }
        }

        public void ApplyInvisibility(Material glassMaterial)
        {
            if (glassMaterial == null)
            {
                Debug.LogError("[InvisibilityHandler] No Glass Material assigned!");
                return;
            }

            // --- Visual Invisibility ---
            Renderer[] renderers = GetComponentsInChildren<Renderer>();
            
            foreach (Renderer r in renderers)
            {
                if (r == null) continue;
                
                // Skip particles or trails if desired, but for now we do everything that is a MeshRenderer or SkinnedMeshRenderer
                if (!(r is MeshRenderer || r is SkinnedMeshRenderer)) continue;

                // Cache original materials if not already cached
                if (!originalMaterialsCache.ContainsKey(r))
                {
                    originalMaterialsCache[r] = r.sharedMaterials;
                    
                    Material[] newMats = new Material[r.sharedMaterials.Length];
                    for (int i = 0; i < newMats.Length; i++)
                    {
                        newMats[i] = glassMaterial;
                    }

                    r.sharedMaterials = newMats;
                }
            }

            // --- Gameplay Invisibility (AI/Radar) ---
            var trackable = GetComponent<Trackable>();
            if (trackable == null) trackable = GetComponentInChildren<Trackable>();
            if (trackable == null) trackable = GetComponentInParent<Trackable>();

            if (trackable != null)
            {
                trackable = trackable.RootTrackable; 
                
                if (!originalTrackableStates.ContainsKey(trackable))
                {
                    originalTrackableStates[trackable] = trackable.Activated;
                    trackable.SetActivation(false);
                    Debug.Log($"[InvisibilityHandler] Disabled tracking for {trackable.name}");
                }
            }
        }

        public void RevertInvisibility()
        {
            // --- Revert Visuals ---
            Renderer[] renderers = GetComponentsInChildren<Renderer>();
            
            foreach (Renderer r in renderers)
            {
                if (originalMaterialsCache.TryGetValue(r, out Material[] originalMats))
                {
                    r.sharedMaterials = originalMats;
                }
            }
            originalMaterialsCache.Clear();

            // --- Revert Gameplay Invisibility ---
            var trackable = GetComponent<Trackable>();
            if (trackable == null) trackable = GetComponentInChildren<Trackable>();
            if (trackable == null) trackable = GetComponentInParent<Trackable>();
            
            if (trackable != null)
            {
                trackable = trackable.RootTrackable;

                if (originalTrackableStates.TryGetValue(trackable, out bool originalState))
                {
                    trackable.SetActivation(originalState);

                    // Force re-registration
                    if (trackable.enabled && trackable.gameObject.activeInHierarchy)
                    {
                        StartCoroutine(ForceReRegistration(trackable));
                    }
                    
                    originalTrackableStates.Remove(trackable);
                    Debug.Log($"[InvisibilityHandler] Restored tracking for {trackable.name}");
                }
            }
        }

        private IEnumerator ForceReRegistration(Trackable trackable)
        {
            trackable.enabled = false;
            yield return null;
            trackable.enabled = true;
        }

        private IEnumerator RevertAfterDelay(float delay)
        {
            float remaining = delay;
            if (timerText != null) timerText.gameObject.SetActive(true);

            while (remaining > 0)
            {
                if (timerText != null) 
                    timerText.text = string.Format(timerFormat, remaining);
                
                remaining -= Time.deltaTime;
                yield return null;
            }

            if (timerText != null) timerText.gameObject.SetActive(false);

            RevertInvisibility();
            revertCoroutine = null;
        }

        private void OnDisable()
        {
             // Ensure we revert if the handler (ship) itself is disabled/destroyed, though less critical than powerup being destroyed
             // But if specific to switching ships, might be relevant.
             // For now, let's keep it simple.
        }
    }
}
