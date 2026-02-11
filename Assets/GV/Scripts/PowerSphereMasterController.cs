using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VSX.Engines3D;
using GV.PowerUps;

namespace GV
{
    public class PowerSphereMasterController : MonoBehaviour
    {
        public static PowerSphereMasterController Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        [Header("Global Sphere Settings")]
        [Tooltip("If true, all spheres disappear after use.")]
        public bool consumeOnPickup = true;
        
        [Tooltip("If > 0, spheres reappear after this many seconds.")]
        public float respawnTime = -1f;

        [Header("Global Cycling Settings")]
        [Tooltip("If true, all spheres cycle powers.")]
        public bool cyclePowers = true;
        
        [Tooltip("Time in seconds between power switches.")]
        public float cycleInterval = 5f;

        [System.Serializable]
        public struct ShieldSettings
        {
            public float duration;
        }
        [Header("Power Up: Shield")]
        public ShieldSettings shieldSettings = new ShieldSettings { duration = 10f };

        [System.Serializable]
        public struct InvisibilitySettings
        {
            public float duration;
            public Material glassMaterial;
            public bool revertOnExit;
        }
        [Header("Power Up: Invisibility")]
        public InvisibilitySettings invisibilitySettings = new InvisibilitySettings { duration = 10f, revertOnExit = false };

        [System.Serializable]
        public struct SuperBoostSettings
        {
            public float speedMultiplier;
            public float steeringMultiplier;
            public float boostMultiplier;
            public float boostDuration;
        }
        [Header("Power Up: Super Boost")]
        public SuperBoostSettings superBoostSettings = new SuperBoostSettings 
        { 
            speedMultiplier = 1.2f, 
            steeringMultiplier = 1.0f, 
            boostMultiplier = 1.2f, 
            boostDuration = 5.0f 
        };

        [System.Serializable]
        public struct SuperWeaponSettings
        {
            public float duration;
            [Space]
            public float projectileDamageMultiplier;
            public float projectileRangeMultiplier;
            public float projectileSpeedMultiplier;
            public float projectileFireRateMultiplier;
            public float projectileReloadMultiplier;
            [Space]
            public float missileDamageMultiplier;
            public float missileRangeMultiplier;
            public float missileSpeedMultiplier;
            public float missileFireRateMultiplier;
            public float missileReloadMultiplier;
        }
        [Header("Power Up: Super Weapon")]
        public SuperWeaponSettings superWeaponSettings = new SuperWeaponSettings 
        { 
            duration = 10f,
            projectileDamageMultiplier = 2f,
            projectileRangeMultiplier = 2f,
            projectileSpeedMultiplier = 2f,
            projectileFireRateMultiplier = 2f,
            projectileReloadMultiplier = 2f,
            missileDamageMultiplier = 2f,
            missileRangeMultiplier = 2f,
            missileSpeedMultiplier = 2f,
            missileFireRateMultiplier = 2f,
            missileReloadMultiplier = 2f
        };

        [System.Serializable]
        public struct TeleportSettings
        {
            public bool allowCycling; // New option to exclude from random cycle
            public int checkpointsToJump;
            public float behindDistanceOnPath;
            public float upOffset;
            public float rightOffset;
            public bool keepVelocity;
            [Space]
            public bool autoPilotAfterTeleport;
            public float autoPilotSeconds;
            public bool autoPilotUseCurrentSpeed;
            public float autoPilotSpeed;
            public float autoPilotSpeedMultiplier;
        }
        [Header("Power Up: Teleport")]
        public TeleportSettings teleportSettings = new TeleportSettings 
        { 
            allowCycling = true,
            checkpointsToJump = 6,
            behindDistanceOnPath = 4.0f,
            upOffset = 0f,
            rightOffset = 0f,
            keepVelocity = true,
            autoPilotAfterTeleport = true,
            autoPilotSeconds = 3.0f,
            autoPilotUseCurrentSpeed = true,
            autoPilotSpeed = 50f,
            autoPilotSpeedMultiplier = 1f
        };

        [Header("UI References")]
        public TMPro.TMP_Text shieldTimerText;
        public string shieldTimerFormat = "Shield: {0:0.0}";

        public TMPro.TMP_Text invisibilityTimerText;
        public string invisibilityTimerFormat = "Invisibility: {0:0.0}";

        public TMPro.TMP_Text superBoostTimerText;
        public string superBoostTimerFormat = "Boost: {0:0.0}";

        public TMPro.TMP_Text superWeaponTimerText;
        public string superWeaponTimerFormat = "Weapon: {0:0.0}";

        public TMPro.TMP_Text teleportTimerText;
        public string teleportTimerFormat = "Auto Pilot: {0:0.0}";

    }
}
