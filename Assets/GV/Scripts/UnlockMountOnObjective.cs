using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VSX.Objectives;
using VSX.VehicleCombatKits;
using VSX.Vehicles;
using VSX.Loadouts;
using VSX.UI;

public class UnlockMountOnObjective : MonoBehaviour
{
    [Header("Settings")]
    [Tooltip("The objective that must be completed to unlock the mount.")]
    [SerializeField]
    protected ObjectiveController triggerObjective;

    [Tooltip("The spawner that creates the player vehicle (Standard).")]
    [SerializeField]
    protected PilotedVehicleSpawn playerSpawner;

    [Tooltip("The spawner that creates the player vehicle (Loadout System).")]
    [SerializeField]
    protected LoadoutVehicleSpawner loadoutSpawner;

    [Tooltip("Direct reference to the vehicle (if pre-placed in the scene).")]
    [SerializeField]
    protected Vehicle directVehicle;

    [Header("Mount Settings")]
    [Tooltip("Drag specific ModuleMount objects here to lock/unlock them directly.")]
    [SerializeField]
    protected List<ModuleMount> targetMounts = new List<ModuleMount>();

    [Tooltip("The index of the module mount to lock/unlock (0 = Primary, 1 = Secondary). Used if Target Mounts list is empty.")]
    [SerializeField]
    protected int mountIndex = 1;

    [Tooltip("Whether to lock the mount immediately when the scene starts.")]
    [SerializeField]
    protected bool lockOnStart = true;

    [Header("Notification")]
    [Tooltip("Text controller to display the unlock message (optional).")]
    [SerializeField]
    protected TextController notificationText;

    [Tooltip("The message to display when unlocked.")]
    [SerializeField]
    protected string unlockMessage = "SECONDARY UNLOCKED";

    [Tooltip("How long to show the message (seconds). Set to 0 to keep it on.")]
    [SerializeField]
    protected float notificationDuration = 3f;

    protected Vehicle currentVehicle;

    protected virtual void Start()
    {
        if (triggerObjective != null)
        {
            triggerObjective.onCompleted.AddListener(OnObjectiveCompleted);
        }

        if (playerSpawner != null)
        {
            playerSpawner.onSpawned.AddListener(OnVehicleSpawned);
            
            // Check if already spawned
            if (playerSpawner.Vehicle != null)
            {
                OnVehicleSpawned();
            }
        }

        if (loadoutSpawner != null)
        {
            loadoutSpawner.onVehicleSpawned.AddListener(OnLoadoutVehicleSpawned);
        }

        if (directVehicle != null)
        {
            currentVehicle = directVehicle;
            CheckAndLock();
        }
    }

    protected void OnLoadoutVehicleSpawned(Vehicle vehicle)
    {
        currentVehicle = vehicle;
        CheckAndLock();
    }

    protected void OnVehicleSpawned()
    {
        if (playerSpawner != null) currentVehicle = playerSpawner.Vehicle;
        CheckAndLock();
    }

    protected void CheckAndLock()
    {
        // If the objective is NOT complete, and we want to lock it
        if (triggerObjective != null && !triggerObjective.Completed && lockOnStart)
        {
            SetMountActive(false);
        }
    }
    
    protected virtual void Update()
    {
        // Fallback: If we have direct mounts but they aren't disabled yet (e.g. they were enabled by a swap or other script), 
        // ensure they stay disabled until unlocked. 
        // NOTE: This might be aggressive, but ensures user request "disable secondary" is respected.
        // Optimized: Only check if we are supposed to be locked.
        if (triggerObjective != null && !triggerObjective.Completed && lockOnStart)
        {
             foreach(var mount in targetMounts)
             {
                 if (mount != null && mount.gameObject.activeSelf)
                 {
                     mount.gameObject.SetActive(false);
                     mount.UnmountActiveModule();
                 }
             }
        }
    }

    protected void OnObjectiveCompleted()
    {
        // Objective done! Unlock the mount.
        SetMountActive(true);
        
        if (notificationText != null)
        {
            notificationText.text = unlockMessage;
            notificationText.gameObject.SetActive(true);
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

    protected void SetMountActive(bool isActive)
    {
        // Priority 1: Direct Target Mounts
        if (targetMounts.Count > 0)
        {
            foreach (ModuleMount mount in targetMounts)
            {
                if (mount != null)
                {
                    mount.gameObject.SetActive(isActive);
                    if (!isActive) mount.UnmountActiveModule();
                    Debug.Log($"UnlockMountOnObjective: Set direct mount {mount.name} to Active: {isActive}");
                }
            }
            return; // Exit if we used direct mounts
        }

        // Priority 2: Find via Vehicle (Legacy/Dynamic)
        if (currentVehicle == null)
        {
            // Try to find it if we missed the event
            if (playerSpawner != null) currentVehicle = playerSpawner.Vehicle;
        }

        if (currentVehicle != null)
        {
            if (mountIndex >= 0 && mountIndex < currentVehicle.ModuleMounts.Count)
            {
                ModuleMount mount = currentVehicle.ModuleMounts[mountIndex];
                if (mount != null)
                {
                    mount.gameObject.SetActive(isActive);
                    // Also unmount if disabling to be safe
                    if (!isActive) mount.UnmountActiveModule();
                    
                    Debug.Log($"UnlockMountOnObjective: Set mount {mountIndex} on {currentVehicle.name} to Active: {isActive}");
                }
            }
            else
            {
                Debug.LogWarning($"UnlockMountOnObjective: Mount index {mountIndex} is out of range for vehicle {currentVehicle.name}");
            }
        }
        else
        {
            Debug.LogWarning("UnlockMountOnObjective: No player vehicle found to update mounts.");
        }
    }
}
