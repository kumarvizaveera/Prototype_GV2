using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VSX.Objectives;
using VSX.VehicleCombatKits;
using VSX.Vehicles;
using VSX.Loadouts;
using VSX.UI;

[System.Serializable]
public class MountUnlockEvent
{
    [Tooltip("The objective that must be completed to unlock these mounts.")]
    public ObjectiveController triggerObjective;

    [Tooltip("Drag specific ModuleMount objects here to lock/unlock them.")]
    public List<ModuleMount> targetMounts = new List<ModuleMount>();
    
    [Tooltip("The index of the module mount to lock/unlock (Fallback if Target Mounts is empty).")]
    public int mountIndex = 1;

    [Tooltip("The message to display when this specific unlock happens.")]
    public string unlockMessage = "WEAPON UNLOCKED";
}

public class UnlockMountOnObjective : MonoBehaviour
{
    [Header("Settings")]
    [Tooltip("List of unlock events to handle.")]
    [SerializeField]
    protected List<MountUnlockEvent> unlockEvents = new List<MountUnlockEvent>();

    [Tooltip("The spawner that creates the player vehicle (Standard).")]
    [SerializeField]
    protected PilotedVehicleSpawn playerSpawner;

    [Tooltip("The spawner that creates the player vehicle (Loadout System).")]
    [SerializeField]
    protected LoadoutVehicleSpawner loadoutSpawner;

    [Tooltip("Direct reference to the vehicle (if pre-placed in the scene).")]
    [SerializeField]
    protected Vehicle directVehicle;

    [Header("Global Settings")]
    [Tooltip("Whether to lock the mounts immediately when the scene starts.")]
    [SerializeField]
    protected bool lockOnStart = true;

    [Header("Notification")]
    [Tooltip("Text controller to display the unlock messages.")]
    [SerializeField]
    protected TextController notificationText;

    [Tooltip("How long to show the message (seconds).")]
    [SerializeField]
    protected float notificationDuration = 3f;

    protected Vehicle currentVehicle;

    protected virtual void Awake()
    {
        // 1. Hide text immediately in Awake to prevent flash
        if (notificationText != null) 
        {
            notificationText.gameObject.SetActive(false);
        }
    }

    protected virtual void Start()
    {

        // Subscribe to all objectives
        foreach(var evt in unlockEvents)
        {
            if (evt.triggerObjective != null)
            {
                evt.triggerObjective.onCompleted.AddListener(() => OnObjectiveCompleted(evt));
                Debug.Log($"UnlockMountOnObjective: Listening for completion of {evt.triggerObjective.name}");
            }
        }

        if (playerSpawner != null)
        {
            playerSpawner.onSpawned.AddListener(OnVehicleSpawned);
            if (playerSpawner.Vehicle != null) OnVehicleSpawned();
        }

        if (loadoutSpawner != null)
        {
            loadoutSpawner.onVehicleSpawned.AddListener(OnLoadoutVehicleSpawned);
        }

        if (directVehicle != null)
        {
            currentVehicle = directVehicle;
            CheckAndLockAll();
        }
    }

    protected void OnLoadoutVehicleSpawned(Vehicle vehicle)
    {
        currentVehicle = vehicle;
        CheckAndLockAll();
    }

    protected void OnVehicleSpawned()
    {
        if (playerSpawner != null) currentVehicle = playerSpawner.Vehicle;
        CheckAndLockAll();
    }

    protected void CheckAndLockAll()
    {
        if (!lockOnStart) return;

        foreach(var evt in unlockEvents)
        {
            if (evt.triggerObjective != null && !evt.triggerObjective.Completed)
            {
                SetMountsActive(evt, false);
            }
        }
    }
    
    protected virtual void Update()
    {
        if (!lockOnStart) return;

        // Persistently lock mounts for incomplete objectives
        foreach(var evt in unlockEvents)
        {
            if (evt.triggerObjective != null && !evt.triggerObjective.Completed)
            {
                foreach(var mount in evt.targetMounts)
                {
                    if (mount != null && mount.gameObject.activeSelf)
                    {
                        // Debug.Log($"UnlockMountOnObjective: Locking {mount.name} because {evt.triggerObjective.name} is incomplete.");
                        mount.gameObject.SetActive(false);
                        mount.UnmountActiveModule();
                    }
                }
            }
        }
    }

    protected void OnObjectiveCompleted(MountUnlockEvent evt)
    {
        Debug.Log($"UnlockMountOnObjective: Event triggered for {evt.triggerObjective.name}. Unlocking mounts.");

        // Unlock this specific event's mounts
        SetMountsActive(evt, true);
        
        // Show notification
        if (notificationText != null)
        {
            notificationText.text = evt.unlockMessage;
            notificationText.gameObject.SetActive(true);
            
            // Restart coroutine to handle overlapping messages reasonably well (last one wins)
            StopAllCoroutines();
            if (notificationDuration > 0)
            {
                StartCoroutine(HideNotificationAfterTime(notificationDuration));
            }
        }
    }

    protected IEnumerator HideNotificationAfterTime(float time)
    {
        yield return new WaitForSeconds(time);
        if (notificationText != null)
        {
            notificationText.gameObject.SetActive(false);
        }
    }

    protected void SetMountsActive(MountUnlockEvent evt, bool isActive)
    {
        // Priority 1: Direct Target Mounts
        if (evt.targetMounts.Count > 0)
        {
            foreach (ModuleMount mount in evt.targetMounts)
            {
                if (mount != null)
                {
                    mount.gameObject.SetActive(isActive);
                    if (!isActive) 
                    {
                        mount.UnmountActiveModule();
                    }
                    else
                    {
                        // IMPORTANT: When enabling, we MUST ensure a module is mounted!
                        // If we previously unmounted it, it's sitting empty.
                        // Mount the default available module (index 0) if nothing is mounted.
                        if (mount.MountedModule() == null && mount.Modules.Count > 0)
                        {
                            mount.MountModule(0);
                        }
                    }
                }
            }
            // If direct mounts are used, we typically don't fail over to index unless specifically needed.
            // But let's check index logic just in case user mixed them or used index for Vehicle.
        }

        // Priority 2: Find via Vehicle (Index-based)
        // Only if we have a current vehicle reference
        if (currentVehicle != null)
        {
            if (evt.mountIndex >= 0 && evt.mountIndex < currentVehicle.ModuleMounts.Count)
            {
                // Only touch the index-based mount if we didn't already handle it via direct reference? 
                // Or maybe the user WANTS to use index on the current vehicle. 
                // Let's assume if TargetMounts is EMPTY, we use Index.
                if (evt.targetMounts.Count == 0)
                {
                    ModuleMount mount = currentVehicle.ModuleMounts[evt.mountIndex];
                    if (mount != null)
                    {
                        mount.gameObject.SetActive(isActive);
                        if (!isActive) 
                        {
                            mount.UnmountActiveModule();
                        }
                        else
                        {
                            if (mount.MountedModule() == null && mount.Modules.Count > 0)
                            {
                                mount.MountModule(0);
                            }
                        }
                        
                        Debug.Log($"UnlockMountOnObjective: Set mount index {evt.mountIndex} on {currentVehicle.name} to Active: {isActive}");
                    }
                }
            }
        }
    }
}
