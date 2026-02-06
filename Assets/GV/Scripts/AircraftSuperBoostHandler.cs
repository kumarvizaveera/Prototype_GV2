using UnityEngine;
using TMPro;
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

        private TMP_Text timerText;
        private string timerFormat = "{0:0.0}";

        public void SetUI(TMP_Text timerText, string timerFormat)
        {
            this.timerText = timerText;
            this.timerFormat = timerFormat;
            if (this.timerText != null) this.timerText.gameObject.SetActive(false);
        }

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
            float remaining = duration;
            if (timerText != null) timerText.gameObject.SetActive(true);

            while (remaining > 0)
            {
                if (timerText != null)
                {
                   timerText.text = string.Format(timerFormat, remaining);
                }
                remaining -= Time.deltaTime;
                yield return null;
            }

            if (timerText != null) timerText.gameObject.SetActive(false);

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
