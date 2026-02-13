using UnityEngine;
using Fusion;
using VSX.VehicleCombatKits; // Namespace where VehicleHealth likely resides
using VSX.Health;            // Namespace for Damageable/HealthType
using System.Collections.Generic;
using System.Linq;

namespace GV.Network
{
    public class BattleZoneController : NetworkBehaviour
    {
        [Header("Settings")]
        [SerializeField] private float initialRadius = 500f;
        [SerializeField] private float minRadius = 0f;
        [SerializeField] private float shrinkDuration = 60f;
        [SerializeField] private bool autoStart = true;
        
        [Header("Damage Settings")]
        [SerializeField] private float damagePerSecond = 10f;
        [SerializeField] private float damageInterval = 1f; // Apply damage every X seconds

        [Header("Visuals")]
        [SerializeField] private Transform sphereVisual;

        [Networked] public float CurrentRadius { get; set; }
        [Networked] public NetworkBool IsShrinking { get; set; }
        [Networked] private TickTimer ShrinkTimer { get; set; }
        
        private TickTimer _damageTimer;

        public override void Spawned()
        {
            if (Object.HasStateAuthority)
            {
                CurrentRadius = initialRadius;
                IsShrinking = false;

                if (autoStart)
                {
                    StartShrinking();
                }
            }
        }

        public override void FixedUpdateNetwork()
        {
            if (Object.HasStateAuthority)
            {
                // Ensure Radius is valid (if for some reason it's 0 but initial is not, force set it if not started)
                if (CurrentRadius == 0 && initialRadius > 0 && !IsShrinking && !autoStart)
                {
                     CurrentRadius = initialRadius;
                }

                if (IsShrinking)
                {
                    if (ShrinkTimer.IsRunning)
                    {
                        float progress = 1f - (float)(ShrinkTimer.RemainingTime(Runner) / shrinkDuration);
                        CurrentRadius = Mathf.Lerp(initialRadius, minRadius, progress);
                    }
                    else if (ShrinkTimer.Expired(Runner))
                    {
                         CurrentRadius = minRadius;
                         IsShrinking = false; // Stop shrinking when done
                    }
                }
                
                // Logic for damage
                if (_damageTimer.ExpiredOrNotRunning(Runner))
                {
                    _damageTimer = TickTimer.CreateFromSeconds(Runner, damageInterval);
                    CheckPlayersAndApplyDamage();
                }
            }
        }

        public override void Render()
        {
            // Update visual size on all clients
            if (sphereVisual != null)
            {
                // Diameter = Radius * 2
                float diameter = CurrentRadius * 2f;
                sphereVisual.localScale = new Vector3(diameter, diameter, diameter);
            }
        }

        private void CheckPlayersAndApplyDamage()
        {
            // Find all active players
            // We can iterate through NetworkRunner.ActivePlayers if we have a way to get their NetworkObject/GameObject
            // Or we can find objects of type VehicleHealth
            
            // Optimization: Maintain a list of registered players if finding every frame is too slow.
            // For now, FindObjects works for a prototype but is heavy.
            // Better: use NetworkManager's player list or similar if available. 
            // Assuming we don't have a reliable central list, we can search for VehicleHealth components.
            
            // Getting all VehicleHealth components in the scene
            VehicleHealth[] allVehicles = FindObjectsOfType<VehicleHealth>();
            
            foreach (var vehicle in allVehicles)
            {
                if (vehicle == null) continue;
                
                // Skip if not a player (optional check, depends on if AI should take damage too)
                // For now, we damage everything with VehicleHealth that is outside.
                
                float distance = Vector3.Distance(transform.position, vehicle.transform.position);
                if (distance > CurrentRadius)
                {
                     // Get active damageable(s) or apply to general health
                     // VehicleHealth usually has a list of Damageables.
                     
                     // We will iterate through damageables and apply damage.
                     foreach(var damageable in vehicle.Damageables)
                     {
                         if (damageable.IsDamageable && !damageable.Destroyed)
                         {
                             // Create damage info
                             HealthEffectInfo info = new HealthEffectInfo();
                             info.amount = damagePerSecond * damageInterval;
                             info.worldPosition = vehicle.transform.position;
                             info.sourceRootTransform = transform;
                             
                             damageable.Damage(info);
                             
                             // If we only want to damage the "hull" or main health, we'd need to filter by HealthType.
                             // But generic "Zone Damage" usually affects everything.
                         }
                     }
                }
            }
        }

        // Call this to start the battle zone shrinking
        public void StartShrinking()
        {
            if (Object.HasStateAuthority)
            {
                IsShrinking = true;
                ShrinkTimer = TickTimer.CreateFromSeconds(Runner, shrinkDuration);
            }
        }
    }
}
