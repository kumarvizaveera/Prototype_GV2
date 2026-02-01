using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VSX.Objectives;
using VSX.VehicleCombatKits;
using VSX.Loadouts;
using VSX.Vehicles;

public class FighterObjectiveController : ObjectiveController
{
    [Tooltip("The parent object containing the PilotedVehicleSpawn components.")]
    [SerializeField]
    protected Transform spawnerParent;

    [Tooltip("List of specific PilotedVehicleSpawn components to track.")]
    [SerializeField]
    protected List<PilotedVehicleSpawn> spawners = new List<PilotedVehicleSpawn>();

    [Tooltip("List of specific LoadoutVehicleSpawner components to track.")]
    [SerializeField]
    protected List<LoadoutVehicleSpawner> loadoutSpawners = new List<LoadoutVehicleSpawner>();

    [Tooltip("Number of fighter kills required to complete the objective.")]
    [SerializeField]
    protected int requiredKills = 1;

    protected int currentKills = 0;

    protected virtual void Awake()
    {
        // Auto-find spawners if parent is assigned
        if (spawnerParent != null)
        {
            spawners.AddRange(spawnerParent.GetComponentsInChildren<PilotedVehicleSpawn>());
            loadoutSpawners.AddRange(spawnerParent.GetComponentsInChildren<LoadoutVehicleSpawner>());
        }

        // Subscribe to events for PilotedVehicleSpawn
        foreach (PilotedVehicleSpawn spawner in spawners)
        {
            if (spawner != null)
            {
                spawner.onDestroyed.AddListener(OnFighterDestroyed);
            }
        }

        // Subscribe to events for LoadoutVehicleSpawner
        foreach (LoadoutVehicleSpawner spawner in loadoutSpawners)
        {
            if (spawner != null)
            {
                spawner.onVehicleSpawned.AddListener(OnLoadoutVehicleSpawned);
            }
        }
    }

    protected void OnLoadoutVehicleSpawned(Vehicle vehicle)
    {
        if (vehicle != null)
        {
            vehicle.onDestroyed.AddListener(OnFighterDestroyed);
        }
    }

    protected void OnFighterDestroyed()
    {
        currentKills++;
        OnObjectiveChanged(); // Notify changes for UI (if tied to progress)

        if (currentKills >= requiredKills)
        {
            CheckIsCompleted();
        }
    }

    protected override bool IsCompleted()
    {
        return currentKills >= requiredKills;
    }

    // Optional: Expose progress for UI
    public override int NumSubObjectives()
    {
        return requiredKills;
    }

    public override int NumSubObjectivesCompleted()
    {
        return currentKills;
    }
}
