using UnityEngine;
using UnityEditor;
using GV.Scripts;
using System.Collections.Generic;

public class CheckpointFuelManager : ScriptableWizard
{
    [Header("Fuel Settings")]
    [Tooltip("The base amount of fuel to set on all checkpoints.")]
    public float baseFuel = 10f;

    [Tooltip("The flux percentage (0-1) to set.")]
    [Range(0f, 1f)]
    public float fluxPercentage = 0.05f;

    [Header("Jackpot Settings")]
    [Tooltip("How many checkpoints should be Jackpots?")]
    public int jackpotCount = 4;

    [Tooltip("The amount of fuel for a Jackpot.")]
    public float jackpotFuel = 100f;
    
    [Space]
    [Tooltip("The parent object containing all checkpoints (defaults to searching for 'Spline_2').")]
    public GameObject rootObject;

    [MenuItem("GV/Manage Checkpoint Fuel")]
    static void CreateWizard()
    {
        var wizard = ScriptableWizard.DisplayWizard<CheckpointFuelManager>("Manage Checkpoint Fuel", "Apply Settings & Roll Jackpots");
        // Try to auto-find the root
        if (wizard.rootObject == null)
        {
            wizard.rootObject = GameObject.Find("Spline_2");
        }
    }

    void OnWizardCreate()
    {
        if (rootObject == null)
        {
            rootObject = GameObject.Find("Spline_2");
            if (rootObject == null)
            {
                Debug.LogError("CheckpointFuelManager: Root object 'Spline_2' not found.");
                return;
            }
        }

        var allCheckpoints = rootObject.GetComponentsInChildren<CheckpointFuel>(true);
        int total = allCheckpoints.Length;

        if (total == 0)
        {
            Debug.LogWarning("No CheckpointFuel scripts found!");
            return;
        }

        Undo.RecordObjects(allCheckpoints, "Update Fuel & Jackpots");

        // 1. Reset everyone to base settings
        foreach (var cp in allCheckpoints)
        {
            cp.baseFuel = baseFuel;
            cp.fluxPercentage = fluxPercentage;
            cp.jackpotFuel = jackpotFuel;
            cp.isJackpot = false; // Reset first
        }

        // 2. Pick Random Jackpots
        if (jackpotCount > 0)
        {
            List<int> availableIndices = new List<int>();
            for (int i = 0; i < total; i++) availableIndices.Add(i);

            int assigned = 0;
            while (assigned < jackpotCount && availableIndices.Count > 0)
            {
                int randomIndex = Random.Range(0, availableIndices.Count);
                int cpIndex = availableIndices[randomIndex];
                
                // Set as Jackpot
                allCheckpoints[cpIndex].isJackpot = true;
                
                // Remove from list so we don't pick it again
                availableIndices.RemoveAt(randomIndex);
                assigned++;
            }
            Debug.Log($"Rolled {assigned} Jackpots out of {total} checkpoints.");
        }

        // Mark all as dirty to save changes
        foreach (var cp in allCheckpoints)
        {
            EditorUtility.SetDirty(cp);
        }

        Debug.Log($"Updated settings for {total} checkpoints.");
    }
}
