using UnityEngine;
using UnityEditor;
using GV.Scripts;

public class CheckpointFuelManager : ScriptableWizard
{
    [Tooltip("The amount of fuel to set on all checkpoints.")]
    public float newFuelAmount = 10f;
    
    [Tooltip("The parent object containing all checkpoints (defaults to searching for 'Spline_2').")]
    public GameObject rootObject;

    [MenuItem("GV/Manage Checkpoint Fuel")]
    static void CreateWizard()
    {
        var wizard = ScriptableWizard.DisplayWizard<CheckpointFuelManager>("Manage Checkpoint Fuel", "Apply to All");
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
            // Try one last find
            rootObject = GameObject.Find("Spline_2");
            if (rootObject == null)
            {
                Debug.LogError("CheckpointFuelManager: Root object 'Spline_2' not found. Please assign it manually.");
                return;
            }
        }

        int count = 0;
        Undo.RecordObjects(rootObject.GetComponentsInChildren<CheckpointFuel>(true), "Update Fuel Amount");

        foreach (var fuelScript in rootObject.GetComponentsInChildren<CheckpointFuel>(true))
        {
            fuelScript.fuelAmount = newFuelAmount;
            EditorUtility.SetDirty(fuelScript);
            count++;
        }

        Debug.Log($"Updated fuel amount to {newFuelAmount} for {count} checkpoints.");
    }
}
