using UnityEngine;
using System.Collections;

public class SetStartAtCheckpoint : MonoBehaviour
{
    [Tooltip("The index of the checkpoint to start at (1-based if your CheckpointNetwork is 1-based).")]
    public int checkpointIndex = 1; 
    
    [Tooltip("If true, the aircraft effectively takes the rotation of the checkpoint.")]
    public bool alignRotation = true;

    IEnumerator Start()
    {
        // 1. Wait efficiently for CheckpointNetwork to be ready
        //    (It uses DefaultExecutionOrder(-1000) so it *should* be ready, but safety first)
        while (CheckpointNetwork.Instance == null || CheckpointNetwork.Instance.Count == 0)
        {
            yield return null;
        }

        // 2. Get the specific checkpoint transform
        Transform cp = CheckpointNetwork.Instance.GetCheckpoint(checkpointIndex);
        
        if (cp != null)
        {
            // 3. Move the GameObject (Teleport)
            transform.position = cp.position;
            
            if (alignRotation)
            {
                transform.rotation = cp.rotation;
            }

            // 4. Reset Physics to ensure no carrying over of weird momentum if any
            Rigidbody rb = GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                
                // If the ship uses physics-based engines, sometimes sleeping them helps reset
                rb.Sleep(); 
                yield return null; // Wait a frame 
                rb.WakeUp();
            }

            Debug.Log($"[SetStartAtCheckpoint] Moved '{name}' to Checkpoint {checkpointIndex}");
        }
        else
        {
            Debug.LogWarning($"[SetStartAtCheckpoint] Could not find Checkpoint {checkpointIndex}. Total checkpoints: {CheckpointNetwork.Instance.Count}");
        }
    }
}
