using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Fusion;
using VSX.RadarSystem;
using TMPro;

namespace GV
{
    public class InvisibilityHandler : NetworkBehaviour
    {
        [Header("Settings")]
        [Tooltip("The material to apply when invisible. Assign this on the player prefab.")]
        public Material defaultGlassMaterial;

        // Cache to store original materials for reversion
        private Dictionary<Renderer, Material[]> originalMaterialsCache = new Dictionary<Renderer, Material[]>();
        
        // Cache for Trackable component to restore state
        private Dictionary<Trackable, bool> originalTrackableStates = new Dictionary<Trackable, bool>();
        
        [Networked] public NetworkBool IsInvisible { get; set; }
        [Networked] public TickTimer InvisibilityTimer { get; set; }

        private ChangeDetector _changes;

        private TMP_Text timerText;
        private string timerFormat = "Invisibility: {0:0.0}";

        public void SetUI(TMP_Text timerText, string timerFormat)
        {
            this.timerText = timerText;
            this.timerFormat = timerFormat;
            if (this.timerText != null) this.timerText.gameObject.SetActive(false);
        }

        public override void Spawned()
        {
            _changes = GetChangeDetector(ChangeDetector.Source.SimulationState);
            
            if (IsInvisible)
            {
                ApplyInvisibility(defaultGlassMaterial);
            }
        }

        public override void Render()
        {
            foreach (var change in _changes.DetectChanges(this))
            {
                if (change == nameof(IsInvisible))
                {
                    if (IsInvisible)
                    {
                        ApplyInvisibility(defaultGlassMaterial); // Use default material
                    }
                    else
                    {
                        RevertInvisibility();
                    }
                }
            }

            // UI Update (local only)
            if (IsInvisible && timerText != null)
            {
                 float remaining = 0f;
                 if (InvisibilityTimer.IsRunning)
                     remaining = (float)InvisibilityTimer.RemainingTime(Runner);

                 if (remaining > 0)
                 {
                     timerText.text = string.Format(timerFormat, remaining);
                 }
                 else
                 {
                     timerText.gameObject.SetActive(false);
                 }
            }
        }

        public override void FixedUpdateNetwork()
        {
            if (Object.HasStateAuthority)
            {
                if (IsInvisible && InvisibilityTimer.Expired(Runner))
                {
                    IsInvisible = false;
                }
            }
        }

        // Server Only
        public void ActivateInvisibility(Material glassMaterial, float duration, bool revertOnExitIgnoredForNetwork = true)
        {
            if (!Object.HasStateAuthority) return;

            // Note: We ignore the passed material for network sync simplicity, 
            // unless we want to resource load it. Using defaultGlassMaterial on the prefab is safer.
            // If the passed material is crucial, we'd need a way to sync it (e.g. ID).
            
            IsInvisible = true;
            InvisibilityTimer = TickTimer.CreateFromSeconds(Runner, duration);
        }

        public void ApplyInvisibility(Material glassMaterial)
        {
            if (glassMaterial == null) glassMaterial = defaultGlassMaterial;
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
                
                // Skip particles or trails if possible?
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
            // Only do this on Server/StateAuthority? 
            // Actually, RadarSystem might be local for UI. 
            // If Trackable.Activated is false, local radars won't see it.
            // So we should apply this on all clients so local interactions update.
            
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
            
            if (timerText != null) timerText.gameObject.SetActive(true);
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
            
            if (timerText != null) timerText.gameObject.SetActive(false);
        }

        private IEnumerator ForceReRegistration(Trackable trackable)
        {
            trackable.enabled = false;
            yield return null;
            trackable.enabled = true;
        }

        private void OnDisable()
        {
             // Safety reset
             // RevertInvisibility(); // Might cause issues if done during destroy
        }
    }
}
