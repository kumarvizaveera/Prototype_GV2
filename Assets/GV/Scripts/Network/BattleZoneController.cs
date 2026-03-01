using UnityEngine;
using Fusion;
using VSX.VehicleCombatKits; // Namespace where VehicleHealth likely resides
using VSX.Health;            // Namespace for Damageable/HealthType
using System.Collections.Generic;
using System.Linq;
using TMPro;

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

        [Header("UI")]
        [SerializeField] private TextMeshProUGUI timerText;
        [Tooltip("Text shown before the timer. e.g. \"Sphere zone is shrinking in \"")]
        [SerializeField] private string timerPrefix = "";
        [Tooltip("Text shown after the timer. e.g. \" remaining\"")]
        [SerializeField] private string timerSuffix = "";
        [Tooltip("Text shown when the shrink timer has finished.")]
        [SerializeField] private string timerFinishedText = "00:00";
        [Tooltip("Text shown before shrinking starts. Leave empty to show full duration.")]
        [SerializeField] private string timerIdleText = "";

        /// <summary>
        /// The initial radius set in the Inspector. Safe to read before Fusion Spawned().
        /// Use this instead of CurrentRadius when Fusion is not yet running.
        /// </summary>
        public float InitialRadius => initialRadius;

        [Networked] public float CurrentRadius { get; set; }
        [Networked] public NetworkBool IsShrinking { get; set; }
        [Networked] private TickTimer ShrinkTimer { get; set; }
        [Networked] public float NetworkedShrinkDuration { get; set; }

        private TickTimer _damageTimer;

        public override void Spawned()
        {
            if (Object.HasStateAuthority)
            {
                CurrentRadius = initialRadius;
                NetworkedShrinkDuration = shrinkDuration;
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

                // Sync duration if changed at runtime in Inspector (host only)
                if (!Mathf.Approximately(NetworkedShrinkDuration, shrinkDuration))
                {
                    float oldDuration = NetworkedShrinkDuration;
                    NetworkedShrinkDuration = shrinkDuration;

                    // If currently shrinking, recreate timer with adjusted remaining time
                    if (IsShrinking && ShrinkTimer.IsRunning && oldDuration > 0f)
                    {
                        float? remainingOld = ShrinkTimer.RemainingTime(Runner);
                        if (remainingOld.HasValue)
                        {
                            float elapsed = oldDuration - remainingOld.Value;
                            float elapsedRatio = Mathf.Clamp01(elapsed / oldDuration);
                            float newRemaining = Mathf.Max(0f, shrinkDuration * (1f - elapsedRatio));
                            ShrinkTimer = TickTimer.CreateFromSeconds(Runner, newRemaining);
                        }
                    }
                }

                if (IsShrinking)
                {
                    if (ShrinkTimer.IsRunning)
                    {
                        float duration = NetworkedShrinkDuration > 0f ? NetworkedShrinkDuration : 1f;
                        float progress = 1f - (float)(ShrinkTimer.RemainingTime(Runner) / duration);
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
            // Dedicated server has no visuals or UI — skip rendering.
            if (NetworkManager.Instance != null && NetworkManager.Instance.IsDedicatedServer) return;

            // Update visual size on all clients
            if (sphereVisual != null)
            {
                // Diameter = Radius * 2
                float diameter = CurrentRadius * 2f;
                sphereVisual.localScale = new Vector3(diameter, diameter, diameter);
            }

            // Update timer UI on all clients
            if (timerText != null)
            {
                if (IsShrinking && ShrinkTimer.IsRunning)
                {
                    float? remaining = ShrinkTimer.RemainingTime(Runner);
                    if (remaining.HasValue && remaining.Value > 0f)
                    {
                        int totalSeconds = Mathf.CeilToInt(remaining.Value);
                        int minutes = totalSeconds / 60;
                        int seconds = totalSeconds % 60;
                        timerText.text = $"{timerPrefix}{minutes:00}:{seconds:00}{timerSuffix}";
                    }
                    else
                    {
                        timerText.text = timerFinishedText;
                    }
                }
                else if (!IsShrinking && CurrentRadius <= minRadius)
                {
                    timerText.text = timerFinishedText;
                }
                else
                {
                    // Not yet started
                    if (!string.IsNullOrEmpty(timerIdleText))
                    {
                        timerText.text = timerIdleText;
                    }
                    else
                    {
                        float displayDuration = NetworkedShrinkDuration > 0f ? NetworkedShrinkDuration : shrinkDuration;
                        int totalSeconds = Mathf.CeilToInt(displayDuration);
                        int minutes = totalSeconds / 60;
                        int seconds = totalSeconds % 60;
                        timerText.text = $"{timerPrefix}{minutes:00}:{seconds:00}{timerSuffix}";
                    }
                }
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
                NetworkedShrinkDuration = shrinkDuration;
                IsShrinking = true;
                ShrinkTimer = TickTimer.CreateFromSeconds(Runner, NetworkedShrinkDuration);
            }
        }
    }
}
