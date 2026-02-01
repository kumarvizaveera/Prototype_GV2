using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VSX.Objectives;
using VSX.VehicleCombatKits;

public class FighterObjectiveController : ObjectiveController
{
    [Tooltip("The parent object containing the PilotedVehicleSpawn components.")]
    [SerializeField]
    protected Transform spawnerParent;

    [Tooltip("List of specific spawners to track if not using parent.")]
    [SerializeField]
    protected List<PilotedVehicleSpawn> spawners = new List<PilotedVehicleSpawn>();

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
        }

        // Subscribe to events
        foreach (PilotedVehicleSpawn spawner in spawners)
        {
            if (spawner != null)
            {
                spawner.onDestroyed.AddListener(OnFighterDestroyed);
            }
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
