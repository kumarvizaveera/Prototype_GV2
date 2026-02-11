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

        [Header("UI")]
        public TMPro.TMP_Text timerText;
        public string timerFormat = "Shield: {0:0.0}";

        public void SetUI(TMPro.TMP_Text timerText, string timerFormat)
        {
            this.timerText = timerText;
            this.timerFormat = timerFormat;
            if (this.timerText != null) this.timerText.gameObject.SetActive(false);
        }

        public override void Spawned()
        {
            _changes = GetChangeDetector(ChangeDetector.Source.SimulationState);
            
            if (shieldController == null)
                shieldController = GetComponentInChildren<EnergyShieldController>(true);

            // Auto-assign UI from MasterController
            if (PowerSphereMasterController.Instance != null && PowerSphereMasterController.Instance.shieldTimerText != null)
            {
                SetUI(PowerSphereMasterController.Instance.shieldTimerText, "Shield: {0:0.0}");
            }

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
            
            // UI Update (local only)
            if (IsShieldActive && timerText != null)
            {
                 float remaining = 0f;
                 if (ShieldTimer.IsRunning)
                     remaining = (float)ShieldTimer.RemainingTime(Runner);
                 
                 if (remaining > 0)
                 {
                     timerText.text = string.Format(timerFormat, remaining);
                 }
                 else
                 {
                     timerText.gameObject.SetActive(false);
                 }
            }
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

            if (timerText != null)
            {
                if (IsShieldActive) timerText.gameObject.SetActive(true);
                else timerText.gameObject.SetActive(false); 
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
