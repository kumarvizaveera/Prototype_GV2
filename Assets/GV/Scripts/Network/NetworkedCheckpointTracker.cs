using Fusion;
using UnityEngine;

namespace GV.Network
{
    /// <summary>
    /// Tracks checkpoint progress for a networked player.
    /// Attached to the Player Ship Prefab.
    /// </summary>
    public class NetworkedCheckpointTracker : NetworkBehaviour
    {
        [Networked] public int CurrentLap { get; set; }
        [Networked] public int CurrentCheckpointIndex { get; set; }
        
        [Header("Visual References")]
        // If we want to update local UI or the local CheckpointNetwork
        public CheckpointNetwork localCheckpointSystem;

        public override void Spawned()
        {
            // Initial setup
            CurrentLap = 1;
            CurrentCheckpointIndex = 0;
            
            // Find global system if needed
            if (localCheckpointSystem == null)
            {
                localCheckpointSystem = FindFirstObjectByType<CheckpointNetwork>();
            }
        }
        
        // This method should be called when the ship physically passes a checkpoint trigger
        // The trigger script (e.g., Checkpoint.cs) should call this on the attached tracker
        public void OnCheckpointPassed(int checkpointIndex)
        {
            // Only the server (State Authority) should validate and update progress
            if (!Object.HasStateAuthority) return;
            
            int nextIndex = localCheckpointSystem != null ? localCheckpointSystem.GetNextIndex(CurrentCheckpointIndex) : (CurrentCheckpointIndex + 1);
            
            // Simple validation: must be the next checkpoint
            if (checkpointIndex == nextIndex)
            {
                CurrentCheckpointIndex = checkpointIndex;
                
                // Lap completion logic (simplified)
                if (checkpointIndex == 0) // Assuming 0 is start/finish
                {
                    CurrentLap++;
                    Debug.Log($"[NetworkedCheckpointTracker] Player {Object.InputAuthority} completed Lap {CurrentLap-1}");
                    
                    if (NetworkedGameManager.Instance != null && CurrentLap > NetworkedGameManager.Instance.totalLaps)
                    {
                        // Finish race for this player
                        Debug.Log($"[NetworkedCheckpointTracker] Player {Object.InputAuthority} FINISHED RACE!");
                    }
                }
            }
        }
    }
}
