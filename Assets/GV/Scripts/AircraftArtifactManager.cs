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
        
        public class ArtifactBonuses
        {
            public float speed = 1f;
            public float steering = 1f;
            public float boost = 1f;

            public float projectileDamage = 1f;
            public float projectileRange = 1f;
            public float projectileSpeed = 1f;
            public float projectileFireRate = 1f;
            public float projectileReload = 1f;

            public float missileDamage = 1f;
            public float missileRange = 1f;
            public float missileSpeed = 1f;
            public float missileFireRate = 1f;
            public float missileReload = 1f;
        }

        // Helper for CharacterManager to read total multiplier
        public ArtifactBonuses GetTotalMultipliers()
        {
            ArtifactBonuses bonuses = new ArtifactBonuses();
            
            Debug.Log($"[ArtifactManager] Calculating total multipliers. Equipped artifacts: {artifacts.Count}");

            foreach (var a in artifacts)
            {
                if (a != null)
                {
                    Debug.Log($"[ArtifactManager] Processing artifact: {a.artifactName} (Speed: {a.speedMultiplier}, Dmg: {a.projectileDamageMultiplier})");

                    bonuses.speed *= a.speedMultiplier;
                    bonuses.steering *= a.steeringMultiplier;
                    bonuses.boost *= a.boostMultiplier;

                    bonuses.projectileDamage *= a.projectileDamageMultiplier;
                    bonuses.projectileRange *= a.projectileRangeMultiplier;
                    bonuses.projectileSpeed *= a.projectileSpeedMultiplier;
                    bonuses.projectileFireRate *= a.projectileFireRateMultiplier;
                    bonuses.projectileReload *= a.projectileReloadMultiplier;

                    bonuses.missileDamage *= a.missileDamageMultiplier;
                    bonuses.missileRange *= a.missileRangeMultiplier;
                    bonuses.missileSpeed *= a.missileSpeedMultiplier;
                    bonuses.missileFireRate *= a.missileFireRateMultiplier;
                    bonuses.missileReload *= a.missileReloadMultiplier;
                }
            }

            Debug.Log($"[ArtifactManager] Total Projectile Damage Multiplier: {bonuses.projectileDamage}");
            return bonuses;
        }
    }
}
