using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Fusion;
using VSX.Health;

namespace GV.Network
{
    public class NetworkShieldHandler : NetworkBehaviour
    {
        [SerializeField] private EnergyShieldController shieldController;

        [Networked] public NetworkBool IsShieldActive { get; set; }

        private ChangeDetector _changes;
        
        // Server-side timer to disable shield
        [Networked] private TickTimer ShieldTimer { get; set; }

        public override void Spawned()
        {
            _changes = GetChangeDetector(ChangeDetector.Source.SimulationState);
            
            if (shieldController == null)
                shieldController = GetComponentInChildren<EnergyShieldController>(true);

            UpdateShieldState();
        }

        public override void Render()
        {
            foreach (var change in _changes.DetectChanges(this))
            {
                if (change == nameof(IsShieldActive))
                {
                    UpdateShieldState();
                }
            }
            
            // Optional: Sync timer to UI if needed, but for now just sync state
        }

        public override void FixedUpdateNetwork()
        {
            if (Object.HasStateAuthority)
            {
                // Check timer
                if (IsShieldActive && ShieldTimer.Expired(Runner))
                {
                    IsShieldActive = false;
                }
            }
        }

        private void UpdateShieldState()
        {
            if (shieldController != null)
            {
                // We use SetShieldActive directly.
                // If true, we might want to ensure the visual is fully on.
                shieldController.SetShieldActive(IsShieldActive);
                
                // If we want the local controller to handle the fade in/out or hit effects, 
                // SetShieldActive usually toggles the mesh renderer. 
            }
        }

        // Called by Server via PowerUp or Event
        public void ActivateShield(float duration)
        {
            if (Object.HasStateAuthority)
            {
                IsShieldActive = true;
                ShieldTimer = TickTimer.CreateFromSeconds(Runner, duration);
            }
        }
    }
}
