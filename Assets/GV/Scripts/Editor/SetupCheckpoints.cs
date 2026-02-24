using UnityEngine;
using UnityEditor;
using GV.Scripts;

public class SetupCheckpoints : MonoBehaviour
{
    [MenuItem("GV/Setup Checkpoints")]
    public static void Setup()
    {
        GameObject spline2 = GameObject.Find("Spline_2");
        if (spline2 == null)
        {
            Debug.LogError("Spline_2 not found!");
            return;
        }

        int count = 0;
        foreach (Transform child in spline2.transform)
        {
            // Apply to all children of Spline_2 as requested
            CheckpointFuel fuel = child.GetComponent<CheckpointFuel>();
            if (fuel == null)
            {
                fuel = child.gameObject.AddComponent<CheckpointFuel>();
                count++;
            }
            // Ensure collider is a trigger
            Collider col = child.GetComponent<Collider>();
            if (col != null)
            {
                 col.isTrigger = true;
            }
        }
        
        Debug.Log($"Added CheckpointFuel to {count} checkpoints under Spline_2.");
    }

    [MenuItem("GV/Clear Checkpoint Fuels")]
    public static void ClearCheckpointFuels()
    {
        GameObject spline2 = GameObject.Find("Spline_2");
        if (spline2 == null)
        {
            Debug.LogError("Spline_2 not found!");
            return;
        }

        CheckpointFuel[] fuels = spline2.GetComponentsInChildren<CheckpointFuel>(true);

        if (fuels.Length == 0)
        {
            Debug.Log("No CheckpointFuel components found under Spline_2.");
            return;
        }

        int count = fuels.Length;
        foreach (var fuel in fuels)
        {
            Undo.DestroyObjectImmediate(fuel);
        }

        Debug.Log($"Removed {count} CheckpointFuel components from checkpoints under Spline_2.");
    }
}
