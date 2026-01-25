using UnityEngine;
using UnityEditor;
using GV.Scripts;
using System.Collections.Generic;

public class CheckpointFuelManager : ScriptableWizard
{
    [Header("Base Settings (Majority)")]
    [Tooltip("The base amount of fuel for standard boxes.")]
    public float baseFuel = 10f;

    [Header("Bonus / Flux Settings")]
    [Tooltip("How many checkpoints should be Bonus/Flux boxes?")]
    public int bonusCount = 15;

    [Tooltip("The flux percentage (0-1) for Bonus boxes.")]
    [Range(0f, 1f)]
    public float fluxPercentage = 0.25f;

    [Header("Jackpot Settings")]
    [Tooltip("How many checkpoints should be Jackpots?")]
    public int jackpotCount = 4;

    [Tooltip("The amount of fuel for a Jackpot.")]
    public float jackpotFuel = 100f;

    [Header("Visuals")]
    public Material normalMaterial;
    public Material bonusMaterial;
    public Material jackpotMaterial;
    
    [Space]
    [Tooltip("The parent object containing all checkpoints (defaults to searching for 'Spline_2').")]
    public GameObject rootObject;

    [MenuItem("GV/Manage Checkpoint Fuel")]
    static void CreateWizard()
    {
        var wizard = ScriptableWizard.DisplayWizard<CheckpointFuelManager>("Manage Checkpoint Fuel", "Apply 3-Tier System");
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

        Undo.RecordObjects(allCheckpoints, "Update Fuel System");

        // Track available indices for randomization
        List<int> availableIndices = new List<int>();
        for (int i = 0; i < total; i++) availableIndices.Add(i);

        // 1. Reset everyone to BASE settings (Flux=0, Normal Material)
        foreach (var cp in allCheckpoints)
        {
            cp.baseFuel = baseFuel;
            cp.fluxPercentage = 0f; // Base boxes have no flux
            cp.jackpotFuel = jackpotFuel;
            cp.isJackpot = false;

            ApplyMaterial(cp, normalMaterial);
        }

        // 2. Assign Jackpots
        int assignedJackpots = 0;
        while (assignedJackpots < jackpotCount && availableIndices.Count > 0)
        {
            int randomIndex = Random.Range(0, availableIndices.Count);
            int cpIndex = availableIndices[randomIndex];
            
            var cp = allCheckpoints[cpIndex];
            cp.isJackpot = true;
            ApplyMaterial(cp, jackpotMaterial);
            
            availableIndices.RemoveAt(randomIndex);
            assignedJackpots++;
        }

        // 3. Assign Bonus/Flux Boxes
        int assignedBonus = 0;
        while (assignedBonus < bonusCount && availableIndices.Count > 0)
        {
            int randomIndex = Random.Range(0, availableIndices.Count);
            int cpIndex = availableIndices[randomIndex];
            
            var cp = allCheckpoints[cpIndex];
            cp.fluxPercentage = fluxPercentage; // Assign the bonus flux
            ApplyMaterial(cp, bonusMaterial);
            
            availableIndices.RemoveAt(randomIndex);
            assignedBonus++;
        }

        // Mark all as dirty
        foreach (var cp in allCheckpoints)
        {
            EditorUtility.SetDirty(cp);
        }

        Debug.Log($"System Updated: {assignedJackpots} Jackpots, {assignedBonus} Bonus Boxes, {availableIndices.Count} Base Boxes.");
    }

    void ApplyMaterial(CheckpointFuel cp, Material mat)
    {
        if (mat == null) return;
        var r = cp.GetComponent<Renderer>();
        if (r != null)
        {
            Undo.RecordObject(r, "Update Material");
            r.sharedMaterial = mat;
        }
    }
}
