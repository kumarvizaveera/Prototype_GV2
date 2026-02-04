using UnityEngine;
using System.Collections;
using UnityEngine.Events;

namespace VSX.Engines3D
{
    [RequireComponent(typeof(AircraftCharacterManager))]
    public class AircraftSuperBoostHandler : MonoBehaviour
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
        private Coroutine boostCoroutine;
        private bool isBoosted = false;

        private void Awake()
        {
            characterManager = GetComponent<AircraftCharacterManager>();
        }

        /// <summary>
        /// Activates the super boost with specific multipliers and duration.
        /// </summary>
        public void ActivateSuperBoost(float speedMult, float steeringMult, float boostMult, float duration)
        {
            if (characterManager == null) return;

            if (boostCoroutine != null)
            {
                StopCoroutine(boostCoroutine);
            }

            boostCoroutine = StartCoroutine(BoostRoutine(speedMult, steeringMult, boostMult, duration));
        }

        /// <summary>
        /// Activates super boost using default settings.
        /// </summary>
        public void ActivateSuperBoost()
        {
            ActivateSuperBoost(defaultSpeedMultiplier, defaultSteeringMultiplier, defaultBoostMultiplier, defaultDuration);
        }

        private IEnumerator BoostRoutine(float speedMult, float steeringMult, float boostMult, float duration)
        {
            if (!isBoosted)
            {
                isBoosted = true;
                OnSuperBoostStart.Invoke();
            }

            // Apply boost
            characterManager.SetSuperBoost(speedMult, steeringMult, boostMult);

            // Wait for duration
            yield return new WaitForSeconds(duration);

            // Reset boost
            characterManager.SetSuperBoost(1f, 1f, 1f);
            
            isBoosted = false;
            OnSuperBoostEnd.Invoke();
            boostCoroutine = null;
        }

        private void OnDisable()
        {
            // Safety reset if disabled while boosted
            if (isBoosted && characterManager != null)
            {
                characterManager.SetSuperBoost(1f, 1f, 1f);
                isBoosted = false;
            }
        }
    }
}
