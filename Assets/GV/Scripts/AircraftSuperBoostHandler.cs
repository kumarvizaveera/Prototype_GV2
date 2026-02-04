using UnityEngine;
using System.Collections;
using UnityEngine.Events;

namespace VSX.Engines3D
{
    [RequireComponent(typeof(AircraftCharacterManager))]
    public class AircraftSuperBoostHandler : MonoBehaviour
    {
        // Default values (internal fallback)
        private const float defaultMultiplier = 2.0f;
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
        /// Activates the super boost with a specific multiplier and duration.
        /// </summary>
        /// <param name="multiplier">Speed multiplier (e.g. 2.0 for 2x speed).</param>
        /// <param name="duration">Duration in seconds.</param>
        public void ActivateSuperBoost(float multiplier, float duration)
        {
            if (characterManager == null) return;

            if (boostCoroutine != null)
            {
                StopCoroutine(boostCoroutine);
            }

            boostCoroutine = StartCoroutine(BoostRoutine(multiplier, duration));
        }

        /// <summary>
        /// Activates super boost using default settings.
        /// </summary>
        public void ActivateSuperBoost()
        {
            ActivateSuperBoost(defaultMultiplier, defaultDuration);
        }

        private IEnumerator BoostRoutine(float multiplier, float duration)
        {
            if (!isBoosted)
            {
                isBoosted = true;
                OnSuperBoostStart.Invoke();
            }

            // Apply boost
            characterManager.SetSuperBoost(multiplier);

            // Wait for duration
            yield return new WaitForSeconds(duration);

            // Reset boost
            characterManager.SetSuperBoost(1f);
            
            isBoosted = false;
            OnSuperBoostEnd.Invoke();
            boostCoroutine = null;
        }

        private void OnDisable()
        {
            // Safety reset if disabled while boosted
            if (isBoosted && characterManager != null)
            {
                characterManager.SetSuperBoost(1f);
                isBoosted = false;
            }
        }
    }
}
