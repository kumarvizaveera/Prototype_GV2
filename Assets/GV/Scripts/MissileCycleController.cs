using UnityEngine;
using TMPro;
using VSX.Vehicles;
using System.Collections;
using System.Collections.Generic;
using VSX.Weapons;
using VSX.ResourceSystem;
using Fusion;

namespace GV.Scripts
{
    [System.Serializable]
    public class MissileMountEntry
    {
        public ModuleMount mount;
        public TMP_Text statsText;
        public string label; // Custom label for UI
        public bool startUnlocked = false;
        [HideInInspector] public bool isUnlocked = false;
    }

    /// <summary>
    /// Manages a collection of separate Missile Mounts (each representing a missile type),
    /// allowing the player to cycle between only the UNLOCKED ones.
    /// Networked version: syncs currentIndex and ammo counts across host/client.
    /// </summary>
    public class MissileCycleController : NetworkBehaviour
    {
        public static MissileCycleController Instance; // Singleton for easy access

        [Header("Configuration")]
        [Tooltip("The list of all missile mounts available in the game.")]
        public List<MissileMountEntry> missileMounts = new List<MissileMountEntry>();

        [Tooltip("Key to cycle the missiles.")]
        public KeyCode cycleKey = KeyCode.M;

        [Header("UI - Active Status")]
        [Tooltip("The TextMeshPro UGUI component to display the active missile name when cycling.")]
        public TMP_Text activeNotificationText;
        public string activeMessagePrefix = "Active Missile: ";

        [Header("UI - Visuals")]
        public Material activeTextMaterial;
        public Material inactiveTextMaterial;
        public Material emptyTextMaterial;

        // --- Networked State ---
        [Networked, Capacity(8)]
        public NetworkArray<int> NetworkedAmmoCounts { get; }

        [Networked, OnChangedRender(nameof(OnCurrentIndexChanged))]
        public int currentIndex { get; set; } = -1;

        private Coroutine activeHideCoroutine;

        // Swap awareness — this controller is for Spaceship (A), only show UI when A is active
        private AircraftMeshSwapWithFX swapController;
        private bool wasHiddenBySwap = false;

        private void Awake()
        {
            // Fallback singleton for offline/local testing; Spawned() overrides for networked play
            if (Instance == null) Instance = this;

            swapController = GetComponentInParent<AircraftMeshSwapWithFX>();
            if (swapController == null)
                swapController = transform.root.GetComponentInChildren<AircraftMeshSwapWithFX>();
        }

        public override void Spawned()
        {
            // For local player control, we usually access the local instance.
            if (Object.HasInputAuthority)
            {
                Instance = this;
            }
            else
            {
                // NON-LOCAL ship: Hide all missile UI elements immediately.
                // Each ship prefab has its own screen-space notification and stats texts.
                // Without hiding them, every ship instance renders its UI on top of each other.
                HideAllUI();
            }
        }

        /// <summary>
        /// Hides all missile-related UI elements (notification text + per-mount stats texts).
        /// Called on non-local ships so only the local player's missile UI is visible.
        /// </summary>
        private void HideAllUI()
        {
            if (activeNotificationText != null)
            {
                activeNotificationText.gameObject.SetActive(false);
            }
            foreach (var entry in missileMounts)
            {
                if (entry.statsText != null)
                {
                    entry.statsText.gameObject.SetActive(false);
                }
            }
        }

        public override void FixedUpdateNetwork()
        {
            if (Object.HasStateAuthority)
            {
                // Host: Sync local ammo counts to network array
                for (int i = 0; i < missileMounts.Count; i++)
                {
                    if (i < NetworkedAmmoCounts.Length)
                    {
                        var entry = missileMounts[i];
                        if (entry.mount != null && entry.mount.MountedModule() != null)
                        {
                            Weapon weapon = entry.mount.MountedModule().GetComponent<Weapon>();
                            if (weapon != null)
                            {
                                foreach (var handler in weapon.ResourceHandlers)
                                {
                                    if (handler.unitResourceChange < 0 && !handler.perSecond && handler.resourceContainer != null)
                                    {
                                        NetworkedAmmoCounts.Set(i, handler.resourceContainer.CurrentAmountInteger);
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                // Client: Sync networked ammo counts to local resource containers during the physical tick.
                // This ensures that when the Client predicts weapon firing (ApplyWeaponInput),
                // the local CanTriggerWeapon() check sees the correct synchronized ammo count!
                for (int i = 0; i < missileMounts.Count; i++)
                {
                    if (i < NetworkedAmmoCounts.Length)
                    {
                        var entry = missileMounts[i];
                        if (entry.mount != null && entry.mount.MountedModule() != null)
                        {
                            Weapon weapon = entry.mount.MountedModule().GetComponent<Weapon>();
                            if (weapon != null)
                            {
                                foreach (var handler in weapon.ResourceHandlers)
                                {
                                    if (handler.unitResourceChange < 0 && !handler.perSecond && handler.resourceContainer != null)
                                    {
                                        int netAmmo = NetworkedAmmoCounts.Get(i);
                                        if (handler.resourceContainer.CurrentAmountInteger != netAmmo)
                                        {
                                            handler.resourceContainer.SetAmount((float)netAmmo);
                                        }
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        public override void Render()
        {
            // CLIENT-SIDE AMMO SYNC:
            // FixedUpdateNetwork() does NOT run on the client in Fusion 2 Host Mode (client only has
            // InputAuthority, not StateAuthority). Without this sync, the client's ResourceContainer
            // stays at its initial value (often 0), causing CanTriggerWeapon() to fail every time
            // the player presses the missile fire button.
            //
            // Render() runs AFTER Update() in the same frame, so the ammo value is technically one
            // frame stale when Update()'s ApplyWeaponInput checks CanTriggerWeapon(). This is fine:
            // - The client's local firing is only for audio/VFX feedback (no actual projectile spawn)
            // - The REAL firing happens on the Host (which syncs ammo in FixedUpdateNetwork before input)
            // - One frame of staleness is imperceptible
            if (Object != null && Object.IsValid && !Object.HasStateAuthority)
            {
                for (int i = 0; i < missileMounts.Count; i++)
                {
                    if (i < NetworkedAmmoCounts.Length)
                    {
                        var entry = missileMounts[i];
                        if (entry.mount != null && entry.mount.MountedModule() != null)
                        {
                            Weapon weapon = entry.mount.MountedModule().GetComponent<Weapon>();
                            if (weapon != null)
                            {
                                foreach (var handler in weapon.ResourceHandlers)
                                {
                                    if (handler.unitResourceChange < 0 && !handler.perSecond && handler.resourceContainer != null)
                                    {
                                        int netAmmo = NetworkedAmmoCounts.Get(i);
                                        if (handler.resourceContainer.CurrentAmountInteger != netAmmo)
                                        {
                                            handler.resourceContainer.SetAmount((float)netAmmo);
                                        }
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        private IEnumerator Start()
        {
            yield return null;

            // Explicitly disable all mounts first to ensure clean state after initialization
            // AND FORCE them to use Trigger Index 2 (Missile Fire group) to avoid accidental laser firing
            //
            // CRITICAL: Use transform.root.GetComponentInChildren — NOT GetComponentInChildren on this object!
            // MissileCycleController and TriggerablesManager may be on different branches of the
            // ship hierarchy (siblings, not parent-child). GetComponentInChildren only searches descendants
            // of THIS gameObject, so it would return null if TriggerablesManager is on a sibling branch.
            var triggerManager = transform.root.GetComponentInChildren<VSX.Weapons.TriggerablesManager>(true);
            Debug.Log($"[MissileCycleController] Start() — triggerManager={(triggerManager != null ? triggerManager.gameObject.name : "NULL")}, " +
                      $"mountedTriggerables={(triggerManager != null ? triggerManager.MountedTriggerables.Count.ToString() : "N/A")}, " +
                      $"searching from root={transform.root.name}");
            foreach (var entry in missileMounts)
            {
                if (entry.mount != null)
                {
                    entry.mount.gameObject.SetActive(false);

                    if (entry.mount.MountedModule() != null)
                    {
                        var trig = entry.mount.MountedModule().GetComponent<VSX.Weapons.Triggerable>();
                        if (trig != null)
                        {
                            trig.DefaultTriggerIndex = 2; // Force to BUTTON_FIRE_MISSILE

                            // Also update TriggerablesManager if it ALREADY mounted it
                            if (triggerManager != null)
                            {
                                bool foundInManager = false;
                                foreach (var mt in triggerManager.MountedTriggerables)
                                {
                                    if (mt.triggerable == trig)
                                    {
                                        foundInManager = true;
                                        for (int i = 0; i < mt.triggerValuesByGroup.Count; i++)
                                        {
                                            mt.triggerValuesByGroup[i] = 2;
                                        }
                                        Debug.Log($"[MissileCycleController] Updated triggerValuesByGroup to 2 for '{entry.label}'");
                                    }
                                }
                                if (!foundInManager)
                                {
                                    Debug.LogWarning($"[MissileCycleController] Triggerable for '{entry.label}' NOT found in TriggerablesManager.MountedTriggerables! " +
                                                     $"Total mounted: {triggerManager.MountedTriggerables.Count}");
                                }
                            }
                            else
                            {
                                Debug.LogWarning($"[MissileCycleController] TriggerablesManager is NULL! Cannot update triggerValuesByGroup for '{entry.label}'");
                            }
                        }
                    }
                }
            }

            // Initialize unlocks based on startUnlocked setting
            bool anyUnlocked = false;
            foreach (var entry in missileMounts)
            {
                if (entry.startUnlocked)
                {
                    entry.isUnlocked = true;
                    anyUnlocked = true;
                }
            }

            if (Object != null && Object.IsValid && Object.HasStateAuthority)
            {
                if (anyUnlocked)
                {
                    for (int i = 0; i < missileMounts.Count; i++)
                    {
                        if (missileMounts[i].isUnlocked)
                        {
                            currentIndex = i;
                            break;
                        }
                    }
                }
            }
            else if (Object == null || !Object.IsValid)
            {
                // Local only setup (offline/not spawned yet)
                if (anyUnlocked)
                {
                    for (int i = 0; i < missileMounts.Count; i++)
                    {
                        if (missileMounts[i].isUnlocked)
                        {
                            // Avoid setting networked property before spawned
                            UpdateEquippedVisuals(i);
                            ShowActiveNotification(missileMounts[i].label);
                            break;
                        }
                    }
                }
                else
                {
                    ShowActiveNotification("None");
                }
                yield break; // Don't run the below logic locally yet
            }

            // Local initial visuals
            UpdateEquippedVisuals(currentIndex);
            if (anyUnlocked) ShowActiveNotification(missileMounts[Mathf.Max(0, currentIndex)].label);
            else ShowActiveNotification("None");
        }

        private void OnCurrentIndexChanged()
        {
            UpdateEquippedVisuals(currentIndex);

            // Only show notification on the client who owns the ship, unless handling local offline
            if (Object == null || !Object.IsValid || Object.HasInputAuthority)
            {
                if (currentIndex >= 0 && currentIndex < missileMounts.Count)
                {
                    ShowActiveNotification(missileMounts[currentIndex].label);
                }
            }
        }

        private void UpdateEquippedVisuals(int index)
        {
            for (int i = 0; i < missileMounts.Count; i++)
            {
                if (missileMounts[i].mount != null)
                {
                    bool isActive = (i == index);
                    missileMounts[i].mount.gameObject.SetActive(isActive);

                    // CRITICAL: We must also update the Module.isActivated state.
                    // Otherwise, TriggerablesManager will still fire hidden weapons
                    // because it does not check gameObject.activeInHierarchy!
                    if (missileMounts[i].mount.MountedModule() != null)
                    {
                        missileMounts[i].mount.MountedModule().SetActivated(isActive);
                    }
                }
            }
            // Only update UI on the local player's ship
            if (Object == null || !Object.IsValid || Object.HasInputAuthority)
            {
                UpdateStatsUI();
            }
        }

        private void Update()
        {
            // Guard: skip if the NetworkObject has been despawned (e.g. player left).
            // Accessing [Networked] properties like currentIndex throws InvalidOperationException
            // if Spawned() hasn't been called or the object was despawned.
            if (Object == null || !Object.IsValid) return;

            // Only allow input if we have InputAuthority (and are spawned)
            if (!Object.HasInputAuthority) return;

            // Swap awareness: this controller is for Spaceship (A).
            // When Vimana (B) is active, hide UI and skip input.
            if (swapController != null && !swapController.IsAActive)
            {
                if (!wasHiddenBySwap)
                {
                    HideAllUI();
                    wasHiddenBySwap = true;
                }
                return;
            }

            // Restore UI when swapping back to Spaceship (A)
            if (wasHiddenBySwap)
            {
                wasHiddenBySwap = false;
                UpdateEquippedVisuals(currentIndex);
                if (currentIndex >= 0 && currentIndex < missileMounts.Count)
                    ShowActiveNotification(missileMounts[currentIndex].label);
            }

            if (Input.GetKeyDown(cycleKey))
            {
                // Client must ask the Host to cycle
                if (Object != null && Object.IsValid && Object.HasInputAuthority && !Object.HasStateAuthority)
                {
                    RPC_RequestCycleNext();
                }
                else
                {
                    CycleNext();
                }
            }

            // Only update stats UI periodically or when needed instead of every frame?
            // For now, keep it simple but recognize it runs every frame.
            UpdateStatsUI();
        }

        private void UpdateStatsUI()
        {
            for (int i = 0; i < missileMounts.Count; i++)
            {
                var entry = missileMounts[i];
                if (entry.statsText != null)
                {
                    string statsString = entry.label;
                    int currentAmmo = -1;

                    if (entry.mount != null && entry.mount.MountedModule() != null)
                    {
                        Weapon weapon = entry.mount.MountedModule().GetComponent<Weapon>();
                        if (weapon != null)
                        {
                            foreach (var handler in weapon.ResourceHandlers)
                            {
                                if (handler.unitResourceChange < 0 && !handler.perSecond && handler.resourceContainer != null)
                                {
                                    currentAmmo = handler.resourceContainer.CurrentAmountInteger;
                                    statsString += " (" + currentAmmo + ")";
                                    break;
                                }
                            }
                        }
                    }

                    // Apply material based on state (Empty > Active > Inactive)
                    Material targetMat = inactiveTextMaterial;

                    if (i == currentIndex)
                    {
                        if (activeTextMaterial != null) targetMat = activeTextMaterial;
                    }

                    if (currentAmmo == 0)
                    {
                        if (emptyTextMaterial != null) targetMat = emptyTextMaterial;
                    }

                    if (targetMat != null) entry.statsText.fontSharedMaterial = targetMat;

                    entry.statsText.text = statsString;
                }
            }
        }

        // ===================== RPCs =====================

        [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
        public void RPC_RequestCycleNext()
        {
            CycleNext();
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_UnlockMount(int mountIndex)
        {
            UnlockMountInternal(mountIndex);
        }

        // ===================== Public API =====================

        /// <summary>
        /// Called by UnlockMountOnObjective when a mount should be made available.
        /// Network-aware: if called on a client, sends RPC to host.
        /// </summary>
        public void UnlockMount(ModuleMount mountToUnlock)
        {
            int mountIndex = -1;
            for (int i = 0; i < missileMounts.Count; i++)
            {
                if (missileMounts[i].mount == mountToUnlock)
                {
                    mountIndex = i;
                    break;
                }
            }

            if (mountIndex < 0)
            {
                Debug.LogWarning($"MissileCycleController: Mount not found in missileMounts list.");
                return;
            }

            // If networked and not authority, RPC to host
            if (Object != null && Object.IsValid && !Object.HasStateAuthority)
            {
                RPC_UnlockMount(mountIndex);
                return;
            }

            // Server-side / local logic
            UnlockMountInternal(mountIndex);
        }

        private void UnlockMountInternal(int mountIndex)
        {
            if (mountIndex < 0 || mountIndex >= missileMounts.Count) return;

            var entry = missileMounts[mountIndex];
            entry.isUnlocked = true;
            Debug.Log($"MissileCycleController: Unlocked {entry.label}");

            // Always equip the newly unlocked missile to provide immediate feedback
            if (Object != null && Object.IsValid && Object.HasStateAuthority)
            {
                currentIndex = mountIndex; // Networked property — triggers OnCurrentIndexChanged on all
            }
            else if (Object == null || !Object.IsValid)
            {
                // Offline fallback
                currentIndex = mountIndex;
                OnCurrentIndexChanged();
            }
        }

        public void CycleNext()
        {
            if (GetUnlockedCount() <= 1) return; // No need to cycle if 0 or 1 active

            int attempts = 0;
            int nextIndex = currentIndex;

            // Find next unlocked index
            do
            {
                nextIndex = (nextIndex + 1) % missileMounts.Count;
                attempts++;
            }
            while (!missileMounts[nextIndex].isUnlocked && attempts < missileMounts.Count);

            if (missileMounts[nextIndex].isUnlocked && nextIndex != currentIndex)
            {
                if (Object != null && Object.IsValid && Object.HasStateAuthority)
                {
                    currentIndex = nextIndex; // Networked — triggers OnCurrentIndexChanged on all
                }
                else if (Object == null || !Object.IsValid)
                {
                    // Offline fallback
                    currentIndex = nextIndex;
                    OnCurrentIndexChanged();
                }
            }
        }

        // ===================== Helpers =====================

        private int GetUnlockedCount()
        {
            int count = 0;
            foreach (var m in missileMounts) if (m.isUnlocked) count++;
            return count;
        }

        private void ShowActiveNotification(string label)
        {
            if (activeNotificationText == null) return;

            // Only show notification on the LOCAL player's ship.
            // Without this check, all ship instances on the same machine (host's ship +
            // client's ship on host) each write to their own notification text in screen-space,
            // causing overlapping UI.
            if (Object != null && Object.IsValid && !Object.HasInputAuthority) return;

            string finalName = string.IsNullOrEmpty(label) ? "Unknown" : label;
            activeNotificationText.text = activeMessagePrefix + finalName;
            activeNotificationText.gameObject.SetActive(true);

            // Keep notification visible permanently (no auto-hide)
            if (activeHideCoroutine != null) StopCoroutine(activeHideCoroutine);
        }

        private IEnumerator HideActiveNotificationAfterDelay(float delay)
        {
            yield return new WaitForSeconds(delay);
            if (activeNotificationText != null)
            {
                activeNotificationText.gameObject.SetActive(false);
            }
            activeHideCoroutine = null;
        }
    }
}
