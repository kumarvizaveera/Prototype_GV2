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

        [Tooltip("Seconds the player must wait between activations of this power.")]
        public float activationCooldown = 5f;

        [HideInInspector] public bool isCollected = false;

        /// <summary>Remaining time the power effect is still active. 0 = effect finished.</summary>
        [HideInInspector] public float effectRemaining = 0f;

        /// <summary>Remaining use-cooldown. Only ticks down AFTER effectRemaining reaches 0.</summary>
        [HideInInspector] public float cooldownRemaining = 0f;
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
        [Tooltip("Material applied to stats text when the power is on cooldown (collected but temporarily unavailable).")]
        public Material cooldownTextMaterial;

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
            // Tick down all cooldowns each frame
            TickCooldowns();

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

        /// <summary>
        /// Ticks down timers each frame. Effect duration counts down first;
        /// the use-cooldown only begins once the effect has fully expired.
        /// </summary>
        private void TickCooldowns()
        {
            float dt = Time.deltaTime;
            for (int i = 0; i < powerEntries.Count; i++)
            {
                var entry = powerEntries[i];

                // Phase 1: power effect is still active
                if (entry.effectRemaining > 0f)
                {
                    entry.effectRemaining -= dt;
                    if (entry.effectRemaining < 0f)
                        entry.effectRemaining = 0f;
                }
                // Phase 2: effect done → tick the use-cooldown
                else if (entry.cooldownRemaining > 0f)
                {
                    entry.cooldownRemaining -= dt;
                    if (entry.cooldownRemaining < 0f)
                        entry.cooldownRemaining = 0f;
                }
            }
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
                    bool wasAlreadyCollected = powerEntries[i].isCollected;
                    powerEntries[i].isCollected = true;

                    if (wasAlreadyCollected)
                    {
                        // Already had this power — reset both timers as a bonus for re-collecting
                        powerEntries[i].effectRemaining = 0f;
                        powerEntries[i].cooldownRemaining = 0f;
                        Debug.Log($"[PowerSphereMaster] Re-collected {type} — cooldown reset!");
                    }
                    else
                    {
                        Debug.Log($"[PowerSphereMaster] Collected {type} — now available for use.");
                    }

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
        /// Activates the currently selected power. The power is NOT consumed —
        /// it stays collected permanently but goes on cooldown before it can be used again.
        /// </summary>
        public void ActivateCurrentPower()
        {
            if (currentIndex < 0 || currentIndex >= powerEntries.Count) return;

            var entry = powerEntries[currentIndex];

            if (!entry.isCollected)
            {
                Debug.Log("[PowerSphereMaster] No collected power in this slot to activate.");
                return;
            }

            // Block if effect is still active or still on cooldown
            if (entry.effectRemaining > 0f)
            {
                Debug.Log($"[PowerSphereMaster] {entry.label} is still active ({entry.effectRemaining:F1}s remaining).");
                return;
            }
            if (entry.cooldownRemaining > 0f)
            {
                Debug.Log($"[PowerSphereMaster] {entry.label} is on cooldown ({entry.cooldownRemaining:F1}s remaining).");
                return;
            }

            if (cachedPlayerNetObj == null && cachedPlayerRoot == null)
            {
                Debug.LogWarning("[PowerSphereMaster] No cached player reference — cannot activate power.");
                return;
            }

            // Set effect duration and cooldown as SEPARATE timers.
            // TickCooldowns will count down effectRemaining first,
            // then cooldownRemaining only starts after effect expires.
            float effectDuration = GetPowerEffectDuration(entry.powerType);
            entry.effectRemaining = effectDuration;
            entry.cooldownRemaining = entry.activationCooldown;

            Debug.Log($"[PowerSphereMaster] Activating {entry.powerType} — effect {effectDuration}s, then cooldown {entry.activationCooldown}s.");

            // Apply the power effect through the appropriate network handler
            ApplyPowerToPlayer(entry.powerType);

            // Update UI immediately
            UpdatePowerUI();
        }

        private void ApplyPowerToPlayer(PowerUpType type)
        {
            // Use cachedPlayerNetObj if available, otherwise fall back to cachedPlayerRoot
            GameObject target = cachedPlayerNetObj != null ? cachedPlayerNetObj.gameObject : cachedPlayerRoot;
            if (target == null) return;

            // Route through NetworkPowerBridge which handles the Host/Client authority split.
            // On the host: executes directly (StateAuthority).
            // On a client: sends an RPC to the host which has StateAuthority on the handlers.
            var bridge = target.GetComponentInChildren<NetworkPowerBridge>();
            if (bridge != null)
            {
                bridge.RequestActivatePower(type);
            }
            else
            {
                Debug.LogError("[PowerSphereMaster] NetworkPowerBridge not found on player — add it to the player ship prefab. Power activation will not work for clients.");
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

        // =====================================================================
        // UI — mirrors MissileCycleController.UpdateStatsUI()
        // =====================================================================

        /// <summary>
        /// Updates per-slot stats text and applies the correct material based on state:
        ///   - Empty (uncollected):                      emptyTextMaterial       — "Label"
        ///   - Collected, effect active:                  activeTextMaterial      — "Label (Active 8.2s)"
        ///   - Collected, on cooldown (effect done):      cooldownTextMaterial    — "Label (CD 3.2s)"
        ///   - Collected, ready, not selected:            inactiveTextMaterial    — "Label (Ready)"
        ///   - Collected, ready, currently selected:      activeTextMaterial      — "Label (Ready)"
        /// </summary>
        private void UpdatePowerUI()
        {
            for (int i = 0; i < powerEntries.Count; i++)
            {
                var entry = powerEntries[i];
                if (entry.statsText == null) continue;

                string displayText = entry.label;
                Material targetMat = emptyTextMaterial; // default = not collected

                if (entry.isCollected)
                {
                    if (entry.effectRemaining > 0f)
                    {
                        // Power effect is still active
                        displayText += " (Active " + entry.effectRemaining.ToString("F1") + "s)";
                        targetMat = activeTextMaterial != null ? activeTextMaterial : inactiveTextMaterial;
                    }
                    else if (entry.cooldownRemaining > 0f)
                    {
                        // Effect done, on use-cooldown
                        displayText += " (CD " + entry.cooldownRemaining.ToString("F1") + "s)";
                        targetMat = cooldownTextMaterial != null ? cooldownTextMaterial : inactiveTextMaterial;
                    }
                    else
                    {
                        // Ready to use
                        displayText += " (Ready)";

                        if (i == currentIndex)
                        {
                            targetMat = activeTextMaterial != null ? activeTextMaterial : inactiveTextMaterial;
                        }
                        else
                        {
                            targetMat = inactiveTextMaterial;
                        }
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

        /// <summary>
        /// Returns the effect duration for a given power type, pulled from
        /// the settings structs. Teleport uses autoPilotSeconds as its
        /// "active" duration. This is used so the use-cooldown only starts
        /// ticking AFTER the power's effect has fully expired.
        /// </summary>
        private float GetPowerEffectDuration(PowerUpType type)
        {
            switch (type)
            {
                case PowerUpType.Shield:       return shieldSettings.duration;
                case PowerUpType.Invisibility:  return invisibilitySettings.duration;
                case PowerUpType.SuperBoost:    return superBoostSettings.boostDuration;
                case PowerUpType.SuperWeapon:   return superWeaponSettings.duration;
                case PowerUpType.Teleport:      return teleportSettings.autoPilotAfterTeleport
                                                       ? teleportSettings.autoPilotSeconds
                                                       : 0f;
                default:                        return 0f;
            }
        }

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
        /// Returns true if the currently selected power is off cooldown and ready to fire.
        /// </summary>
        public bool IsCurrentPowerReady()
        {
            if (currentIndex < 0 || currentIndex >= powerEntries.Count) return false;
            var entry = powerEntries[currentIndex];
            return entry.isCollected && entry.effectRemaining <= 0f && entry.cooldownRemaining <= 0f;
        }

        /// <summary>
        /// Resets all power inventory (e.g., on race restart).
        /// </summary>
        public void ResetAllPowers()
        {
            foreach (var entry in powerEntries)
            {
                entry.isCollected = false;
                entry.effectRemaining = 0f;
                entry.cooldownRemaining = 0f;
            }
            currentIndex = -1;
            cachedPlayerRoot = null;
            cachedPlayerNetObj = null;
            cachedTeleportPowerUp = null;
            ShowActiveNotification("None");
        }
    }
}
