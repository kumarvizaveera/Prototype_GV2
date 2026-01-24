using System;
using System.Collections.Generic;
using UnityEngine;

namespace VSX.Engines3D
{
    // Removed RequireComponent to prevent forced addition of RigidBody/Engines
    public class AircraftArtifactManager : MonoBehaviour
    {
        [Header("Equipped Artifacts")]
        public List<ArtifactData> artifacts = new List<ArtifactData>();

        [Header("References")]
        [Tooltip("Assign the ship's Character Manager here.")]
        public AircraftCharacterManager targetManager;

        private void Awake()
        {
            // If not assigned, try to find one on this object, or globally as fallback
            if (targetManager == null) targetManager = GetComponent<AircraftCharacterManager>();
            if (targetManager == null) targetManager = FindObjectOfType<AircraftCharacterManager>();
        }

        private void OnEnable()
        {
            ApplyArtifactBonuses();
        }

        // Public method to add/remove artifacts at runtime
        public void AddArtifact(ArtifactData artifact)
        {
            if (!artifacts.Contains(artifact))
            {
                artifacts.Add(artifact);
                ApplyArtifactBonuses();
            }
        }

        public void RemoveArtifact(ArtifactData artifact)
        {
            if (artifacts.Contains(artifact))
            {
                artifacts.Remove(artifact);
                ApplyArtifactBonuses();
            }
        }

        public void ApplyArtifactBonuses()
        {
            // We delegate the actual application to the Character Manager
            // to ensure meaningful stacking (Base * Character * Artifact)
            // instead of compounding on top of current values.
            
            if (targetManager != null)
            {
                targetManager.RefreshStats();
            }
            else
            {
                // If there's no character manager, we can't safely apply bonuses 
                // without risking the infinite-stacking bug.
                // For now, we just warn. 
                Debug.LogWarning("[AircraftArtifactManager] No Target CharacterManager found. Artifacts will not apply.");
            }
        }
        
        // Helper for CharacterManager to read total multiplier
        public (float speed, float steering, float boost) GetTotalMultipliers()
        {
            float mSpeed = 1f;
            float mSteer = 1f;
            float mBoost = 1f;

            foreach (var a in artifacts)
            {
                if (a != null)
                {
                    mSpeed *= a.speedMultiplier;
                    mSteer *= a.steeringMultiplier;
                    mBoost *= a.boostMultiplier;
                }
            }
            return (mSpeed, mSteer, mBoost);
        }
    }
}
