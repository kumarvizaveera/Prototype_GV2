using UnityEngine;
using Fusion;
using VSX.VehicleCombatKits;
using VSX.Health;

namespace GV.Network
{
    /// <summary>
    /// Synchronizes the VehicleHealth state across the network.
    /// Assumes VehicleHealth is authoritative on the State Authority (Host).
    /// </summary>
    public class NetworkedHealthSync : NetworkBehaviour
    {
        [SerializeField] private VehicleHealth vehicleHealth;

        [Networked]
        public float NetworkedCurrentHealth { get; set; }

        private void Awake()
        {
            if (vehicleHealth == null)
                vehicleHealth = GetComponent<VehicleHealth>();
        }

        private ChangeDetector _changes;

        public override void Spawned()
        {
            _changes = GetChangeDetector(ChangeDetector.Source.SimulationState);

            if (Object.HasStateAuthority)
            {
                // Initialize networked var from local state
                if (vehicleHealth != null && vehicleHealth.Damageables.Count > 0)
                {
                    // Assuming the first damageable is the main health/hull
                   NetworkedCurrentHealth = vehicleHealth.Damageables[0].CurrentHealth;
                }
            }
            else
            {
                // Initial sync for clients
                ApplyHealthToLocal();
            }
        }

        public override void Render()
        {
            foreach (var change in _changes.DetectChanges(this))
            {
                if (change == nameof(NetworkedCurrentHealth))
                {
                    ApplyHealthToLocal();
                }
            }
        }

        public override void FixedUpdateNetwork()
        {
            if (Object.HasStateAuthority)
            {
                if (vehicleHealth != null && vehicleHealth.Damageables.Count > 0)
                {
                    // Check if local health has changed and update networked var
                    float currentLocalHealth = vehicleHealth.Damageables[0].CurrentHealth;
                    if (!Mathf.Approximately(currentLocalHealth, NetworkedCurrentHealth))
                    {
                        NetworkedCurrentHealth = currentLocalHealth;
                    }
                }
            }
        }

        private void ApplyHealthToLocal()
        {
            // If we are NOT the state authority, we receive the health update
            if (!Object.HasStateAuthority)
            {
                if (vehicleHealth != null && vehicleHealth.Damageables.Count > 0)
                {
                     // We need to set the local health to match the networked value.
                     vehicleHealth.Damageables[0].SetHealth(NetworkedCurrentHealth);
                }
            }
        }
    }
}
