using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using VSX.Engines3D;
using GV.PowerUps;
using GV.Network;
using Fusion;

namespace GV
{
    /// <summary>
    /// Serializable entry for each power-up slot in the UI.
    /// Mirrors MissileMountEntry from MissileCycleController.
    /// </summary>
    [System.Serializable]
    public class PowerEntry
    {
        public PowerUpType powerType;
        public TMP_Text statsText;
        public string label;
        [HideInInspector] public bool isCollected = false;
        [HideInInspector] public int count = 0;
    }

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

        // =====================================================================
        // GLOBAL SPHERE SETTINGS (unchanged)
        // =====================================================================

        [Header("Global Sphere Settings")]
        [Tooltip("Cooldown in seconds before spheres can be collected again after pickup.")]
        public float cooldownAfterPickup = 5f;

        [Tooltip("Per-player cooldown to prevent the same player from collecting again too quickly.")]
        public float perPlayerCooldown = 2f;

        [Header("Global Cycling Settings")]
        [Tooltip("If true, all spheres cycle powers.")]
        public bool cyclePowers = true;

        [Tooltip("Time in seconds between power switches.")]
        public float cycleInterval = 5f;

        // =====================================================================
        // POWER-UP SETTINGS STRUCTS (unchanged)
        // =====================================================================

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
            public bool allowCycling;
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

        // =====================================================================
        // LEGACY TIMER UI (kept for backward compatibility / active power timers)
        // =====================================================================

        [Header("UI - Active Power Timers (Legacy)")]
        public TMP_Text shieldTimerText;
        public string shieldTimerFormat = "Shield: {0:0.0}";

        public TMP_Text invisibilityTimerText;
        public string invisibilityTimerFormat = "Invisibility: {0:0.0}";

        public TMP_Text superBoostTimerText;
        public string superBoostTimerFormat = "Boost: {0:0.0}";

        public TMP_Text superWeaponTimerText;
        public string superWeaponTimerFormat = "Weapon: {0:0.0}";

        public TMP_Text teleportTimerText;
        public string teleportTimerFormat = "Auto Pilot: {0:0.0}";

        // =====================================================================
        // POWER INVENTORY & CYCLING UI (new — mirrors MissileCycleController)
        // =====================================================================

        [Header("Power Inventory")]
        [Tooltip("The list of all power-up slots. Configure one entry per power type.")]
        public List<PowerEntry> powerEntries = new List<PowerEntry>();

        [Header("Input")]
        [Tooltip("Key to cycle between collected powers.")]
        public KeyCode cycleKey = KeyCode.N;

        [Tooltip("Key to activate the currently selected power.")]
        public KeyCode activateKey = KeyCode.B;

        [Header("UI - Active Notification")]
        [Tooltip("TextMeshPro component showing the currently selected power name.")]
        public TMP_Text activeNotificationText;
        public string activeMessagePrefix = "Power: ";

        [Header("UI - Visuals")]
        [Tooltip("Material applied to the stats text of the currently selected power.")]
        public Material activeTextMaterial;
        [Tooltip("Material applied to stats text of collected but not-selected powers.")]
        public Material inactiveTextMaterial;
        [Tooltip("Material applied to stats text of uncollected (empty) power slots.")]
        public Material emptyTextMaterial;

        // =====================================================================
        // RUNTIME STATE
        // =====================================================================

        /// <summary>Index into powerEntries for the currently selected power. -1 = none selected.</summary>
        private int currentIndex = -1;

        /// <summary>Cached reference to the local player's root NetworkObject (set on first collection).</summary>
        private NetworkObject cachedPlayerNetObj;

        /// <summary>Cached reference to the local player's root GameObject.</summary>
        private GameObject cachedPlayerRoot;

        /// <summary>Reference to the RandomPowerSphere used for Teleport (needs its TeleportPowerUp component).</summary>
        private TeleportPowerUp cachedTeleportPowerUp;

        private Coroutine activeHideCoroutine;

        // =====================================================================
        // UNITY LIFECYCLE
        // =====================================================================

        private void Update()
        {
            // Cycle input
            if (Input.GetKeyDown(cycleKey))
            {
                CycleNext();
            }

            // Activate input
            if (Input.GetKeyDown(activateKey))
            {
                ActivateCurrentPower();
            }

            // Update UI every frame (matches MissileCycleController pattern)
            UpdatePowerUI();
        }

        // =====================================================================
        // PUBLIC API — called by RandomPowerSphere on pickup
        // =====================================================================

        /// <summary>
        /// Stores a collected power in the inventory instead of immediately activating it.
        /// Called by RandomPowerSphere.ApplyPower() on the server, then the sphere deactivates as usual.
        /// </summary>
        /// <param name="type">The power type collected.</param>
        /// <param name="playerRoot">The root GameObject of the player who collected it.</param>
        /// <param name="teleportRef">Optional: reference to the TeleportPowerUp on the sphere (needed for teleport activation).</param>
        public void CollectPower(PowerUpType type, GameObject playerRoot, TeleportPowerUp teleportRef = null)
        {
            if (type == PowerUpType.None) return;

            // Cache the player reference for later activation
            if (cachedPlayerRoot == null || cachedPlayerRoot != playerRoot)
            {
                cachedPlayerRoot = playerRoot;
                cachedPlayerNetObj = playerRoot.GetComponent<NetworkObject>();
                if (cachedPlayerNetObj == null)
                    cachedPlayerNetObj = playerRoot.GetComponentInParent<NetworkObject>();
            }

            // Cache teleport reference if provided
            if (teleportRef != null)
                cachedTeleportPowerUp = teleportRef;

            // Find the matching power entry and mark as collected
            for (int i = 0; i < powerEntries.Count; i++)
            {
                if (powerEntries[i].powerType == type)
                {
                    powerEntries[i].isCollected = true;
                    powerEntries[i].count++;
                    Debug.Log($"[PowerSphereMaster] Collected {type} (count: {powerEntries[i].count})");

                    // Auto-select the newly collected power (immediate feedback, like MissileCycleController unlocking)
                    currentIndex = i;
                    ShowActiveNotification(powerEntries[i].label);
                    break;
                }
            }
        }

        // =====================================================================
        // ACTIVATION — applies the selected power to the cached player
        // =====================================================================

        /// <summary>
        /// Activates the currently selected power from the inventory.
        /// Mirrors MissileCycleController's equip logic but triggers the actual power effect.
        /// </summary>
        public void ActivateCurrentPower()
        {
            if (currentIndex < 0 || currentIndex >= powerEntries.Count) return;

            var entry = powerEntries[currentIndex];

            if (!entry.isCollected || entry.count <= 0)
            {
                Debug.Log("[PowerSphereMaster] No collected power in this slot to activate.");
                return;
            }

            if (cachedPlayerNetObj == null && cachedPlayerRoot == null)
            {
                Debug.LogWarning("[PowerSphereMaster] No cached player reference — cannot activate power.");
                return;
            }

            // Consume one charge
            entry.count--;
            if (entry.count <= 0)
            {
                entry.isCollected = false;
                entry.count = 0;
            }

            Debug.Log($"[PowerSphereMaster] Activating {entry.powerType} (remaining: {entry.count})");

            // Apply the power effect through the appropriate network handler
            ApplyPowerToPlayer(entry.powerType);

            // Update UI immediately
            UpdatePowerUI();

            // If the slot is now empty, try to auto-select the next collected power
            if (!entry.isCollected)
            {
                AutoSelectNextCollected();
            }
        }

        private void ApplyPowerToPlayer(PowerUpType type)
        {
            // Use cachedPlayerNetObj if available, otherwise fall back to cachedPlayerRoot
            GameObject target = cachedPlayerNetObj != null ? cachedPlayerNetObj.gameObject : cachedPlayerRoot;
            if (target == null) return;

            switch (type)
            {
                case PowerUpType.Teleport:
                    if (cachedTeleportPowerUp != null)
                    {
                        cachedTeleportPowerUp.Apply(target);
                        Debug.Log("[PowerSphereMaster] Activated Teleport");
                    }
                    else
                    {
                        Debug.LogWarning("[PowerSphereMaster] No TeleportPowerUp reference cached — cannot teleport.");
                    }
                    break;

                case PowerUpType.Invisibility:
                    var invHandler = target.GetComponentInChildren<InvisibilityHandler>();
                    if (invHandler != null)
                    {
                        float dur = invisibilitySettings.duration;
                        Material mat = invisibilitySettings.glassMaterial;
                        bool revert = invisibilitySettings.revertOnExit;
                        invHandler.ActivateInvisibility(mat, dur, revert);
                        Debug.Log("[PowerSphereMaster] Activated Invisibility");
                    }
                    else Debug.LogError("[PowerSphereMaster] InvisibilityHandler missing on player");
                    break;

                case PowerUpType.Shield:
                    var shieldHandler = target.GetComponentInChildren<NetworkShieldHandler>();
                    if (shieldHandler != null)
                    {
                        shieldHandler.ActivateShield(shieldSettings.duration);
                        Debug.Log("[PowerSphereMaster] Activated Shield");
                    }
                    else Debug.LogError("[PowerSphereMaster] NetworkShieldHandler missing on player");
                    break;

                case PowerUpType.SuperBoost:
                    var boostHandler = target.GetComponentInChildren<AircraftSuperBoostHandler>();
                    if (boostHandler != null)
                    {
                        boostHandler.ActivateSuperBoost(
                            superBoostSettings.speedMultiplier,
                            superBoostSettings.steeringMultiplier,
                            superBoostSettings.boostMultiplier,
                            superBoostSettings.boostDuration
                        );
                        Debug.Log("[PowerSphereMaster] Activated SuperBoost");
                    }
                    else Debug.LogError("[PowerSphereMaster] AircraftSuperBoostHandler missing on player");
                    break;

                case PowerUpType.SuperWeapon:
                    var swHandler = target.GetComponentInChildren<NetworkSuperWeaponHandler>();
                    if (swHandler != null)
                    {
                        AircraftCharacterManager.SuperWeaponBonuses bonuses = new AircraftCharacterManager.SuperWeaponBonuses
                        {
                            projectileDamage = superWeaponSettings.projectileDamageMultiplier,
                            projectileRange = superWeaponSettings.projectileRangeMultiplier,
                            projectileSpeed = superWeaponSettings.projectileSpeedMultiplier,
                            projectileFireRate = superWeaponSettings.projectileFireRateMultiplier,
                            projectileReload = superWeaponSettings.projectileReloadMultiplier,
                            missileDamage = superWeaponSettings.missileDamageMultiplier,
                            missileRange = superWeaponSettings.missileRangeMultiplier,
                            missileSpeed = superWeaponSettings.missileSpeedMultiplier,
                            missileFireRate = superWeaponSettings.missileFireRateMultiplier,
                            missileReload = superWeaponSettings.missileReloadMultiplier
                        };
                        swHandler.ActivateSuperWeapon(superWeaponSettings.duration, bonuses);
                        Debug.Log("[PowerSphereMaster] Activated SuperWeapon");
                    }
                    else Debug.LogError("[PowerSphereMaster] NetworkSuperWeaponHandler missing on player");
                    break;
            }
        }

        // =====================================================================
        // CYCLING — mirrors MissileCycleController.CycleNext()
        // =====================================================================

        /// <summary>
        /// Cycles to the next collected power slot, skipping empty (uncollected) slots.
        /// </summary>
        public void CycleNext()
        {
            if (GetCollectedCount() <= 1) return; // No need to cycle if 0 or 1 collected

            int attempts = 0;
            int nextIndex = currentIndex;

            do
            {
                nextIndex = (nextIndex + 1) % powerEntries.Count;
                attempts++;
            }
            while (!powerEntries[nextIndex].isCollected && attempts < powerEntries.Count);

            if (powerEntries[nextIndex].isCollected && nextIndex != currentIndex)
            {
                currentIndex = nextIndex;
                ShowActiveNotification(powerEntries[currentIndex].label);
            }
        }

        /// <summary>
        /// After using a power and the slot becomes empty, auto-select the next collected power.
        /// </summary>
        private void AutoSelectNextCollected()
        {
            // If there are still collected powers, find the next one
            if (GetCollectedCount() > 0)
            {
                int attempts = 0;
                int nextIndex = currentIndex;

                do
                {
                    nextIndex = (nextIndex + 1) % powerEntries.Count;
                    attempts++;
                }
                while (!powerEntries[nextIndex].isCollected && attempts < powerEntries.Count);

                if (powerEntries[nextIndex].isCollected)
                {
                    currentIndex = nextIndex;
                    ShowActiveNotification(powerEntries[currentIndex].label);
                    return;
                }
            }

            // Nothing collected — reset
            currentIndex = -1;
            ShowActiveNotification("None");
        }

        // =====================================================================
        // UI — mirrors MissileCycleController.UpdateStatsUI()
        // =====================================================================

        /// <summary>
        /// Updates per-slot stats text and applies the correct material based on state:
        ///   - Empty (uncollected): emptyTextMaterial
        ///   - Collected but not selected: inactiveTextMaterial
        ///   - Currently selected: activeTextMaterial
        /// </summary>
        private void UpdatePowerUI()
        {
            for (int i = 0; i < powerEntries.Count; i++)
            {
                var entry = powerEntries[i];
                if (entry.statsText == null) continue;

                // Build display string: label + count if collected
                string displayText = entry.label;
                if (entry.isCollected && entry.count > 0)
                {
                    displayText += " (" + entry.count + ")";
                }

                // Determine material based on state (same priority as MissileCycleController: empty > active > inactive)
                Material targetMat = emptyTextMaterial; // default = not collected

                if (entry.isCollected && entry.count > 0)
                {
                    // Collected — inactive by default
                    targetMat = inactiveTextMaterial;

                    // If this is the currently selected slot, use active material
                    if (i == currentIndex)
                    {
                        if (activeTextMaterial != null) targetMat = activeTextMaterial;
                    }
                }
                else
                {
                    // Not collected — empty
                    if (emptyTextMaterial != null) targetMat = emptyTextMaterial;
                }

                if (targetMat != null) entry.statsText.fontSharedMaterial = targetMat;
                entry.statsText.text = displayText;
            }
        }

        private void ShowActiveNotification(string label)
        {
            if (activeNotificationText == null) return;

            string finalName = string.IsNullOrEmpty(label) ? "None" : label;
            activeNotificationText.text = activeMessagePrefix + finalName;
            activeNotificationText.gameObject.SetActive(true);

            // Keep notification visible permanently (matches MissileCycleController behavior)
            if (activeHideCoroutine != null) StopCoroutine(activeHideCoroutine);
        }

        // =====================================================================
        // HELPERS
        // =====================================================================

        private int GetCollectedCount()
        {
            int count = 0;
            foreach (var e in powerEntries)
                if (e.isCollected) count++;
            return count;
        }

        /// <summary>
        /// Returns the currently selected power type, or None if nothing is selected.
        /// </summary>
        public PowerUpType GetSelectedPowerType()
        {
            if (currentIndex >= 0 && currentIndex < powerEntries.Count && powerEntries[currentIndex].isCollected)
                return powerEntries[currentIndex].powerType;
            return PowerUpType.None;
        }

        /// <summary>
        /// Returns whether the player has any collected (stored) powers available.
        /// </summary>
        public bool HasAnyCollectedPower()
        {
            return GetCollectedCount() > 0;
        }

        /// <summary>
        /// Resets all power inventory (e.g., on race restart).
        /// </summary>
        public void ResetAllPowers()
        {
            foreach (var entry in powerEntries)
            {
                entry.isCollected = false;
                entry.count = 0;
            }
            currentIndex = -1;
            cachedPlayerRoot = null;
            cachedPlayerNetObj = null;
            cachedTeleportPowerUp = null;
            ShowActiveNotification("None");
        }
    }
}
