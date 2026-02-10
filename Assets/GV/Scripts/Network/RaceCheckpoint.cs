using UnityEngine;
using GV.Network;

namespace GV.Race
{
    /// <summary>
    /// Attached to each checkpoint object.
    /// Knows its index and notifies the player's tracker when passed.
    /// </summary>
    public class RaceCheckpoint : MonoBehaviour
    {
        [Tooltip("Auto-assigned by CheckpointNetwork")]
        public int checkoutPointIndex = -1;

        private void OnTriggerEnter(Collider other)
        {
            // Find the tracker on the player
            var tracker = other.GetComponentInParent<NetworkedCheckpointTracker>();
            if (tracker != null)
            {
                tracker.OnCheckpointPassed(checkoutPointIndex);
            }
        }
    }
}
