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

    [Tooltip("Drag specific Module objects here to lock/unlock them (e.g. missiles on a shared mount).")]
    public List<Module> targetModules = new List<Module>();
    
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

                foreach(var module in evt.targetModules)
                {
                    if (module != null && module.gameObject.activeSelf)
                    {
                        LockModule(module);
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
                    // GV Integration: Check if this is managed by the MissileCycleController
                    if (GV.Scripts.MissileCycleController.Instance != null && isActive)
                    {
                        // Check if the controller knows about this mount
                        bool isManaged = false;
                        foreach(var entry in GV.Scripts.MissileCycleController.Instance.missileMounts)
                        {
                            if (entry.mount == mount)
                            {
                                isManaged = true;
                                break;
                            }
                        }

                        if (isManaged)
                        {
                            GV.Scripts.MissileCycleController.Instance.UnlockMount(mount);
                            // Do NOT SetActive(true) here, the controller decides when to equip it.
                            continue;
                        }
                    }

                    // Fallback / Standard behavior
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
                }
            }
        }

        // Priority 2: Find via Vehicle (Index-based)
        if (currentVehicle != null && evt.targetMounts.Count == 0)
        {
            if (evt.mountIndex >= 0 && evt.mountIndex < currentVehicle.ModuleMounts.Count)
            {
                ModuleMount mount = currentVehicle.ModuleMounts[evt.mountIndex];
                if (mount != null)
                {
                    // GV Integration for Indexed mounts
                     if (GV.Scripts.MissileCycleController.Instance != null && isActive)
                    {
                        // Check management
                         bool isManaged = false;
                        foreach(var entry in GV.Scripts.MissileCycleController.Instance.missileMounts)
                        {
                            if (entry.mount == mount)
                            {
                                isManaged = true;
                                break;
                            }
                        }
                         if (isManaged)
                        {
                            GV.Scripts.MissileCycleController.Instance.UnlockMount(mount);
                            Debug.Log($"UnlockMountOnObjective: Delegated unlock of index {evt.mountIndex} to CycleController.");
                            return; 
                        }
                    }

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
                }
            }
        }

        // Priority 3: Target Modules (New)
        if (evt.targetModules.Count > 0)
        {
            foreach (Module module in evt.targetModules)
            {
                if (module != null)
                {
                    if (isActive)
                    {
                        UnlockModule(module);
                    }
                    else
                    {
                        LockModule(module);
                    }
                }
            }
        }
    }

    protected void UnlockModule(Module module)
    {
        module.gameObject.SetActive(true);
        
        // Find parent mount
        ModuleMount mount = module.GetComponentInParent<ModuleMount>();
        if (mount != null)
        {
            // Add to mount if not already there
            if (!mount.Modules.Contains(module))
            {
                mount.AddModule(module, false); 
            }
        }
        else
        {
             // Fallback: maybe it was disabled physically, try finding in parent transform even if inactive?
             // If gameObject was inactive, GetComponentInParent works if we are child.
             // If we were unparented, it's harder. Assuming hierarchy structure is maintained.
        }
    }

    protected void LockModule(Module module)
    {
        // Find parent mount
        ModuleMount mount = module.GetComponentInParent<ModuleMount>();
        if (mount != null)
        {
            mount.RemoveModule(module);
        }

        module.gameObject.SetActive(false);
    }
}
