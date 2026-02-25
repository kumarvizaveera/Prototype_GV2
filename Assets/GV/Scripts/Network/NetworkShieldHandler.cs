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

        // Track local state to detect changes (replaces unreliable ChangeDetector)
        private bool _localShieldActive;

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
            if (shieldController == null)
                shieldController = GetComponentInChildren<EnergyShieldController>(true);

            // Auto-assign UI from MasterController — only for the local player (InputAuthority)
            // so that other players' handlers don't hijack our shared UI text.
            if (Object.HasInputAuthority &&
                PowerSphereMasterController.Instance != null && PowerSphereMasterController.Instance.shieldTimerText != null)
            {
                SetUI(PowerSphereMasterController.Instance.shieldTimerText, PowerSphereMasterController.Instance.shieldTimerFormat);
            }

            _localShieldActive = IsShieldActive;
            UpdateShieldState();

            Debug.Log($"[NetworkShieldHandler] Spawned on {gameObject.name} | isAuth={Object.HasStateAuthority} | shieldActive={IsShieldActive}");
        }

        public override void Render()
        {
            // Direct polling — always check networked state vs local cache.
            // ChangeDetector can miss rapid changes; this is more reliable.
            if (IsShieldActive != _localShieldActive)
            {
                Debug.Log($"[NetworkShieldHandler] Shield state changed on {gameObject.name}: {_localShieldActive} → {IsShieldActive} | isAuth={Object.HasStateAuthority}");
                _localShieldActive = IsShieldActive;
                UpdateShieldState();
            }

            // UI Update — only for the local player (InputAuthority)
            // Without this guard, another player's handler on our machine
            // would overwrite our shared timer UI with THEIR power duration.
            if (!Object.HasInputAuthority) return;

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
                    Debug.Log($"[NetworkShieldHandler] Shield timer expired on {gameObject.name}");
                    IsShieldActive = false;
                }
            }
        }

        private void UpdateShieldState()
        {
            if (shieldController != null)
            {
                shieldController.SetShieldActive(IsShieldActive);
                Debug.Log($"[NetworkShieldHandler] UpdateShieldState: {IsShieldActive} on {gameObject.name} | meshEnabled={shieldController.IsShieldActive}");
            }

            // Only toggle timer UI for the local player
            if (Object.HasInputAuthority && timerText != null)
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
                Debug.Log($"[NetworkShieldHandler] ActivateShield({duration}s) on {gameObject.name}");
                IsShieldActive = true;
                ShieldTimer = TickTimer.CreateFromSeconds(Runner, duration);
            }
        }
    }
}
