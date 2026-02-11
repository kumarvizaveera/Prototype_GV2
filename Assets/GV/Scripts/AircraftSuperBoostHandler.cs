using UnityEngine;
using TMPro;
using System.Collections;
using UnityEngine.Events;
using Fusion;

namespace VSX.Engines3D
{
    // [RequireComponent(typeof(AircraftCharacterManager))] // Removed to allow flexible placement
    public class AircraftSuperBoostHandler : NetworkBehaviour
    {
        // Default values (internal fallback)
        private const float defaultSpeedMultiplier = 2.0f;
        private const float defaultSteeringMultiplier = 1.0f; 
        private const float defaultBoostMultiplier = 2.0f;
        private const float defaultDuration = 5.0f;

        [Header("Events")]
        public UnityEvent OnSuperBoostStart;
        public UnityEvent OnSuperBoostEnd;

        private AircraftCharacterManager characterManager;
        // private Coroutine boostCoroutine; // Replaced by Network Timer
        
        [Networked] public NetworkBool IsBoostActive { get; set; }
        [Networked] public TickTimer BoostTimer { get; set; }
        
        // Multipliers effectively need to be synced if they vary per pickup
        [Networked] public float CurrentSpeedMult { get; set; }
        [Networked] public float CurrentSteeringMult { get; set; }
        [Networked] public float CurrentBoostMult { get; set; }

        private ChangeDetector _changes;

        private TMP_Text timerText;
        private string timerFormat = "{0:0.0}";

        public void SetUI(TMP_Text timerText, string timerFormat)
        {
            this.timerText = timerText;
            this.timerFormat = timerFormat;
            if (this.timerText != null) this.timerText.gameObject.SetActive(false);
        }

        public override void Spawned()
        {
            _changes = GetChangeDetector(ChangeDetector.Source.SimulationState);
            characterManager = GetComponent<AircraftCharacterManager>();
            if (characterManager == null) characterManager = GetComponentInChildren<AircraftCharacterManager>();

            // Apply initial state
            if (IsBoostActive)
            {
                ApplyBoost(true);
            }
        }

        public override void Render()
        {
            foreach (var change in _changes.DetectChanges(this))
            {
                if (change == nameof(IsBoostActive))
                {
                    ApplyBoost(IsBoostActive);
                }
            }

            // UI Update (local only)
            if (IsBoostActive && timerText != null)
            {
                 float remaining = 0f;
                 if (BoostTimer.IsRunning)
                     remaining = (float)BoostTimer.RemainingTime(Runner);
                 
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
                if (IsBoostActive && BoostTimer.Expired(Runner))
                {
                    IsBoostActive = false;
                }
            }
        }

        /// <summary>
        /// Activates the super boost with specific multipliers and duration. (Server Only)
        /// </summary>
        public void ActivateSuperBoost(float speedMult, float steeringMult, float boostMult, float duration)
        {
            if (!Object.HasStateAuthority) return;

            CurrentSpeedMult = speedMult;
            CurrentSteeringMult = steeringMult;
            CurrentBoostMult = boostMult;
            
            IsBoostActive = true;
            BoostTimer = TickTimer.CreateFromSeconds(Runner, duration);
        }

        /// <summary>
        /// Activates super boost using default settings.
        /// </summary>
        public void ActivateSuperBoost()
        {
            ActivateSuperBoost(defaultSpeedMultiplier, defaultSteeringMultiplier, defaultBoostMultiplier, defaultDuration);
        }

        private void ApplyBoost(bool active)
        {
            if (active)
            {
                if (characterManager == null) 
                {
                    characterManager = GetComponent<AircraftCharacterManager>();
                    if (characterManager == null) characterManager = GetComponentInChildren<AircraftCharacterManager>();
                }
                
                if (characterManager != null)
                {
                    OnSuperBoostStart.Invoke();
                    
                    // Use networked multipliers
                    characterManager.SetSuperBoost(CurrentSpeedMult, CurrentSteeringMult, CurrentBoostMult);
                    
                    if (timerText != null) timerText.gameObject.SetActive(true);
                }
            }
            else
            {
                if (characterManager == null) 
                {
                    characterManager = GetComponent<AircraftCharacterManager>();
                    if (characterManager == null) characterManager = GetComponentInChildren<AircraftCharacterManager>();
                }

                if (characterManager != null)
                {
                    characterManager.SetSuperBoost(1f, 1f, 1f);
                    OnSuperBoostEnd.Invoke();
                    
                    if (timerText != null) timerText.gameObject.SetActive(false);
                }
            }
        }

        private void OnDisable()
        {
            // Safety reset if disabled while boosted
            // Check if Object is valid to avoid "Networked properties can only be accessed when Spawned()" error
            if (Object != null && Object.IsValid && IsBoostActive && characterManager != null)
            {
                characterManager.SetSuperBoost(1f, 1f, 1f);
            }
        }
    }
}
