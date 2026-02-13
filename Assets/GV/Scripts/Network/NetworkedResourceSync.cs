using UnityEngine;
using Fusion;
using VSX.ResourceSystem;

namespace GV.Scripts.Network
{
    /// <summary>
    /// Synchronizes a VSX ResourceContainer over the network.
    /// Place this on the same GameObject as the ResourceContainer you want to sync.
    /// </summary>
    public class NetworkedResourceSync : NetworkBehaviour
    {
        [SerializeField]
        private ResourceContainer resourceContainer;

        [Networked]
        public float NetworkedAmount { get; set; }

        private void Awake()
        {
            if (resourceContainer == null)
            {
                resourceContainer = GetComponent<ResourceContainer>();
            }
        }

        public override void Spawned()
        {
            if (Object.HasStateAuthority)
            {
                // Init networked var from local state
                if (resourceContainer != null)
                {
                    NetworkedAmount = resourceContainer.CurrentAmountFloat;
                }
            }
            else
            {
                // Init local state from networked var
                if (resourceContainer != null)
                {
                    resourceContainer.SetAmount(NetworkedAmount);
                }
            }
        }

        public override void FixedUpdateNetwork()
        {
            if (Object.HasStateAuthority)
            {
                // Server: Sync local value to Networked variable
                if (resourceContainer != null)
                {
                     NetworkedAmount = resourceContainer.CurrentAmountFloat;
                }
            }
            else
            {
                // Client (Resimulation): Update local container from Networked variable
                // This ensures logic running in FUN sees the correct amount
                if (resourceContainer != null)
                {
                    resourceContainer.SetAmount(NetworkedAmount);
                }
            }
        }

        public override void Render()
        {
            // Client (Visuals): Ensure visuals are synced in Render as well for smoothness between ticks
            if (!Object.HasStateAuthority && resourceContainer != null)
            {
                 resourceContainer.SetAmount(NetworkedAmount);
            }
        }
    }
}
