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
    public class MissileMountEntryDynamic : MissileMountEntry 
    {
        // No extra fields needed for now
    }

    /// <summary>
    /// Manages a collection of separate Missile Mounts (each representing a missile type),
    /// allowing the player to cycle between only the UNLOCKED ones.
    /// Specialized for Dynamic count updates (e.g. Astra).
    /// Networked version.
    /// </summary>
    public class MissileCycleControllerDynamic : NetworkBehaviour
    {
        public static MissileCycleControllerDynamic Instance; // Singleton for easy access

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


        [Networked, Capacity(8)]
        public NetworkArray<int> NetworkedAmmoCounts { get; }
        
        [Networked, OnChangedRender(nameof(OnCurrentIndexChanged))]
        public int currentIndex { get; set; } = -1;
        
        private Coroutine activeHideCoroutine;


        public override void Spawned()
        {
            // Initial sync or setup if needed
            
            // For local player control, we usually access the local instance.
            if (Object.HasInputAuthority)
            {
                Instance = this;
            }
        }
        
        public override void FixedUpdateNetwork()
        {
            if (Object.HasStateAuthority)
            {
                // Sync local ammo counts to network array
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
                // Client syncs networked ammo counts to local resource containers during the physical tick
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
            // the player presses RMB.
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

        private void Awake()
        {
            // Instance = this; // Moved to Spawned for network safety or keep if single local player
            if(Instance == null) Instance = this;
        }

        private IEnumerator Start()
        {
            yield return null; 

            // Explicitly disable all mounts first to ensure clean state after initialization
            // AND FORCE them to use Trigger Index 2 (Missile Fire group) to avoid accidental laser firing
            //
            // CRITICAL: Use transform.root.GetComponentInChildren — NOT GetComponentInChildren on this object!
            // MissileCycleControllerDynamic and TriggerablesManager may be on different branches of the
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
                // Local only setup
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
            UpdateStatsUI();
        }

        private void Update()
        {
            // Only allow input if we have InputAuthority (and are spawned)
            if (Object != null && Object.IsValid && !Object.HasInputAuthority) return;
            
            // Allow locally if offline/not networked yet
            if (Object != null && Object.IsValid == false) { /* Allow local testing */ }


            if (Input.GetKeyDown(cycleKey))
            {
                // Client must ask the Host to cycle
                if (Object.HasInputAuthority && !Object.HasStateAuthority)
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
            for(int i = 0; i < missileMounts.Count; i++)
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

        /// <summary>
        /// Helper to add ammo to a specific weapon by label.
        /// Unlocks the weapon if it was locked.
        /// </summary>
        public void AddAmmo(string weaponLabel, int amount)
        {
            // If networked and not authority, RPC
            if (Object != null && Object.IsValid && !Object.HasStateAuthority)
            {
                RPC_AddAmmo(weaponLabel, amount);
                return;
            }

            // Server side logic (or local if not networked)
            AddAmmoInternal(weaponLabel, amount);
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_AddAmmo(string weaponLabel, int amount)
        {
            AddAmmoInternal(weaponLabel, amount);
        }

        private void AddAmmoInternal(string weaponLabel, int amount)
        {
             // Find the entry with the matching label
            MissileMountEntry targetEntry = null;
            foreach(var entry in missileMounts)
            {
                if(entry.label.Equals(weaponLabel, System.StringComparison.OrdinalIgnoreCase))
                {
                    targetEntry = entry;
                    break;
                }
            }

            if(targetEntry == null)
            {
                Debug.LogWarning($"MissileCycleControllerDynamic: Could not find weapon with label '{weaponLabel}'");
                return;
            }

            // Unlock if needed
            if (!targetEntry.isUnlocked)
            {
                targetEntry.isUnlocked = true;
                // Sync unlock state if needed? For now we assume local visuals match logic
                Debug.Log($"MissileCycleControllerDynamic: Unlocked {targetEntry.label} because ammo was added.");
                
                if(currentIndex == -1 || GetUnlockedCount() == 1)
                {
                    int index = missileMounts.IndexOf(targetEntry);
                    if (Object != null && Object.HasStateAuthority)
                    {
                        currentIndex = index;
                    }
                    // If offline/not spawned
                    else if (Object == null || !Object.IsValid)
                    {
                        currentIndex = index;
                        OnCurrentIndexChanged();
                    }
                }
            }

            // Add Ammo
            if (targetEntry.mount != null && targetEntry.mount.MountedModule() != null)
            {
                Weapon weapon = targetEntry.mount.MountedModule().GetComponent<Weapon>();
                if (weapon != null)
                {
                    bool foundHandler = false;
                    foreach (var handler in weapon.ResourceHandlers)
                    {
                        if (handler.unitResourceChange < 0 && !handler.perSecond && handler.resourceContainer != null)
                        {
                            handler.resourceContainer.AddRemove((float)amount);
                            foundHandler = true;
                            // Note: ResourceContainer itself should be networked or synced separately via NetworkedResourceSync
                            break; 
                        }
                    }
                    if(!foundHandler)
                    {
                         Debug.LogWarning($"MissileCycleControllerDynamic: Found mount for {weaponLabel} but no valid ammo resource handler.");
                    }
                }
            }
        }

        [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
        public void RPC_RequestCycleNext()
        {
            CycleNext();
        }

        public void CycleNext()
        {
            if (GetUnlockedCount() <= 1) return; 

            int attempts = 0;
            int nextIndex = currentIndex;
            
            do
            {
                nextIndex = (nextIndex + 1) % missileMounts.Count;
                attempts++;
            } 
            while (!missileMounts[nextIndex].isUnlocked && attempts < missileMounts.Count);

            if (missileMounts[nextIndex].isUnlocked && nextIndex != currentIndex)
            {
                if (Object != null && Object.HasStateAuthority)
                {
                    currentIndex = nextIndex;
                }
                else if (Object == null || !Object.IsValid)
                {
                    // Offline fallback
                    currentIndex = nextIndex;
                    OnCurrentIndexChanged();
                }
            }
        }

        // Equip is now handled proactively via OnCurrentIndexChanged
        // Removed Equip() method entirely as `currentIndex` sync handles it.
        
        private int GetUnlockedCount()
        {
            int count = 0;
            foreach (var m in missileMounts) if (m.isUnlocked) count++;
            return count;
        }

        private void ShowActiveNotification(string label)
        {
            if (activeNotificationText == null) return;

            string finalName = string.IsNullOrEmpty(label) ? "Unknown" : label;
            activeNotificationText.text = activeMessagePrefix + finalName;
            activeNotificationText.gameObject.SetActive(true);

            if (activeHideCoroutine != null) StopCoroutine(activeHideCoroutine);
        }
    }
}
