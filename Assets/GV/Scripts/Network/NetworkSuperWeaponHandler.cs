using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Fusion;
using VSX.Engines3D;
using TMPro;

namespace GV.Network
{
    public class NetworkSuperWeaponHandler : NetworkBehaviour
    {
        [Header("Projectile/Laser Bonuses")]
        public float projectileDamageMultiplier = 2f;
        public float projectileRangeMultiplier = 2f;
        public float projectileSpeedMultiplier = 2f;
        public float projectileFireRateMultiplier = 2f;
        public float projectileReloadMultiplier = 2f;

        [Header("Missile Bonuses")]
        public float missileDamageMultiplier = 2f;
        public float missileRangeMultiplier = 2f;
        public float missileSpeedMultiplier = 2f;
        public float missileFireRateMultiplier = 2f;
        public float missileReloadMultiplier = 2f;
        
        [Header("UI")]
        public TMP_Text timerText;
        public string timerFormat = "Weapon: {0:0.0}";

        private AircraftCharacterManager characterManager;
        
        [Networked] public NetworkBool IsSuperWeaponActive { get; set; }
        [Networked] public TickTimer SuperWeaponTimer { get; set; }
        
        // We might want to sync the bonuses if they can vary. For now, assume they are set on the prefab or passed by the orb (if we sync them).
        // To be safe and allow Orb to override, we should sync them.
        [Networked] public float NetProjDmg { get; set; }
        [Networked] public float NetProjRange { get; set; }
        [Networked] public float NetProjSpeed { get; set; }
        [Networked] public float NetProjRate { get; set; }
        [Networked] public float NetProjReload { get; set; }
        
        [Networked] public float NetMissileDmg { get; set; }
        [Networked] public float NetMissileRange { get; set; }
        [Networked] public float NetMissileSpeed { get; set; }
        [Networked] public float NetMissileRate { get; set; }
        [Networked] public float NetMissileReload { get; set; }


        private ChangeDetector _changes;

        public override void Spawned()
        {
            _changes = GetChangeDetector(ChangeDetector.Source.SimulationState);
            characterManager = GetComponent<AircraftCharacterManager>();

            // Only assign UI references on clients — server has no UI.
            bool isDedicatedServer = NetworkManager.Instance != null && NetworkManager.Instance.IsDedicatedServer;
            if (!isDedicatedServer && PowerSphereMasterController.Instance != null && PowerSphereMasterController.Instance.superWeaponTimerText != null)
            {
                timerText = PowerSphereMasterController.Instance.superWeaponTimerText;
                timerFormat = PowerSphereMasterController.Instance.superWeaponTimerFormat;
                timerText.gameObject.SetActive(false);
            }

            if (IsSuperWeaponActive)
            {
                ApplyBonuses(true);
            }
        }

        public override void Render()
        {
            foreach (var change in _changes.DetectChanges(this))
            {
                if (change == nameof(IsSuperWeaponActive))
                {
                    ApplyBonuses(IsSuperWeaponActive);
                }
            }

            // UI Update — only for the local player (input authority)
            if (!Object.HasInputAuthority) return;

            // Lazy resolve timerText if it wasn't available at Spawned() time
            if (timerText == null && PowerSphereMasterController.Instance != null
                && PowerSphereMasterController.Instance.superWeaponTimerText != null)
            {
                timerText = PowerSphereMasterController.Instance.superWeaponTimerText;
                timerFormat = PowerSphereMasterController.Instance.superWeaponTimerFormat;
            }

            if (IsSuperWeaponActive && timerText != null)
            {
                 float remaining = 0f;
                 if (SuperWeaponTimer.IsRunning)
                     remaining = (float)SuperWeaponTimer.RemainingTime(Runner);

                 if (remaining > 0)
                 {
                     timerText.gameObject.SetActive(true);
                     timerText.text = string.Format(timerFormat, remaining);
                 }
                 else
                 {
                     timerText.gameObject.SetActive(false);
                 }
            }
            else if (!IsSuperWeaponActive && timerText != null)
            {
                timerText.gameObject.SetActive(false);
            }
        }

        public override void FixedUpdateNetwork()
        {
            if (Object.HasStateAuthority)
            {
                if (IsSuperWeaponActive && SuperWeaponTimer.Expired(Runner))
                {
                    IsSuperWeaponActive = false;
                }
            }
        }

        // Server Only
        public void ActivateSuperWeapon(float duration, AircraftCharacterManager.SuperWeaponBonuses overrides = null)
        {
            if (!Object.HasStateAuthority) return;

            // Apply overrides if provided, otherwise use defaults
            if (overrides != null)
            {
                NetProjDmg = overrides.projectileDamage;
                NetProjRange = overrides.projectileRange;
                NetProjSpeed = overrides.projectileSpeed;
                NetProjRate = overrides.projectileFireRate;
                NetProjReload = overrides.projectileReload;

                NetMissileDmg = overrides.missileDamage;
                NetMissileRange = overrides.missileRange;
                NetMissileSpeed = overrides.missileSpeed;
                NetMissileRate = overrides.missileFireRate;
                NetMissileReload = overrides.missileReload;
            }
            else
            {
                NetProjDmg = projectileDamageMultiplier;
                NetProjRange = projectileRangeMultiplier;
                NetProjSpeed = projectileSpeedMultiplier;
                NetProjRate = projectileFireRateMultiplier;
                NetProjReload = projectileReloadMultiplier;

                NetMissileDmg = missileDamageMultiplier;
                NetMissileRange = missileRangeMultiplier;
                NetMissileSpeed = missileSpeedMultiplier;
                NetMissileRate = missileFireRateMultiplier;
                NetMissileReload = missileReloadMultiplier;
            }

            IsSuperWeaponActive = true;
            SuperWeaponTimer = TickTimer.CreateFromSeconds(Runner, duration);
        }

        private void ApplyBonuses(bool active)
        {
            if (characterManager == null) characterManager = GetComponent<AircraftCharacterManager>();
            if (characterManager == null) return;

            if (active)
            {
                AircraftCharacterManager.SuperWeaponBonuses bonuses = new AircraftCharacterManager.SuperWeaponBonuses();
                bonuses.projectileDamage = NetProjDmg;
                bonuses.projectileRange = NetProjRange;
                bonuses.projectileSpeed = NetProjSpeed;
                bonuses.projectileFireRate = NetProjRate;
                bonuses.projectileReload = NetProjReload;

                bonuses.missileDamage = NetMissileDmg;
                bonuses.missileRange = NetMissileRange;
                bonuses.missileSpeed = NetMissileSpeed;
                bonuses.missileFireRate = NetMissileRate;
                bonuses.missileReload = NetMissileReload;

                // Apply bonuses to the character manager (timer UI is managed by Render())
                characterManager.SetSuperWeapon(bonuses);

                Debug.Log($"[NetworkSuperWeaponHandler] ApplyBonuses(true) on {gameObject.name}");
            }
            else
            {
                characterManager.SetSuperWeapon(null);
                Debug.Log($"[NetworkSuperWeaponHandler] ApplyBonuses(false) on {gameObject.name}");
            }
        }
    }
}
