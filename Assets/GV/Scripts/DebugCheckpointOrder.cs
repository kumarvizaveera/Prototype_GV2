using UnityEngine;
using System.Text;

namespace VSX.SpaceCombatKit
{
    public class DebugCheckpointOrder : MonoBehaviour
    {
        [ContextMenu("Log Checkpoint Order")]
        public void LogOrder()
        {
            if (CheckpointNetwork.Instance == null)
            {
                Debug.LogError("No CheckpointNetwork Instance found!");
                return;
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("<b>[DebugCheckpointOrder] Checkpoint List:</b>");
            
            // Assuming 1-based indexing for the log if the network is 1-based
            int count = CheckpointNetwork.Instance.Count;
            bool oneBased = CheckpointNetwork.Instance.oneBased;
            
            for (int i = 0; i < count; i++)
            {
                int index = oneBased ? i + 1 : i;
                Transform cp = CheckpointNetwork.Instance.GetCheckpoint(index);
                
                string cpName = cp != null ? cp.name : "NULL";
                sb.AppendLine($"Index {index}: \t{cpName}");
            }
            
            Debug.Log(sb.ToString());
        }

        void Start()
        {
            // Auto-log on start for convenience
            LogOrder();
        }
    }
}
