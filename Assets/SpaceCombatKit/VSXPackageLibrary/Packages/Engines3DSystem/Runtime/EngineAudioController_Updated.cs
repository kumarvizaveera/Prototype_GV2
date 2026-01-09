using System.Reflection;
using UnityEngine;

namespace VSX.Engines3D
{
    /// <summary>
    /// Swap-safe EngineAudioController:
    /// - Subscribes/unsubscribes in OnEnable/OnDisable (works when audio roots are toggled).
    /// - Starts the looping audio on enable if engines are already active (prevents "no sound after swap").
    /// </summary>
    public class EngineAudioController : MonoBehaviour
    {
        [Tooltip("The engines component that this component controls sound effects for.")]
        [SerializeField] protected VehicleEngines3D engines;

        [Tooltip("The audio source.")]
        [SerializeField] protected AudioSource m_Audio;

        [Tooltip("How fast the audio changes in response to changes in the engine control.")]
        [SerializeField] protected float changeSpeed = 3f;

        float currentLevel = 0f;

        public enum EngineAudioControlType { Movement, Boost, Steering }

        [Tooltip("The type of engine control that the audio is for.")]
        [SerializeField] protected EngineAudioControlType controlType;

        public enum AxisContributionType { Maximum, Cumulative }

        [Tooltip("How the three axes (X, Y, Z) contribute together to make up the audio effect. ")]
        [SerializeField] protected AxisContributionType axisContribution;

        [Tooltip("How much the X axis contributes (for movement/boost, this is left/right, for steering this is nose up/down).")]
        [SerializeField] protected float xAxisContribution = 1f;

        [Tooltip("How much the Y axis contributes (for movement/boost, this is up/down, for steering this is nose left/right).")]
        [SerializeField] protected float yAxisContribution = 1f;

        [Tooltip("How much the Z axis contributes (for movement/boost, this is forward/back, for steering this is roll).")]
        [SerializeField] protected float zAxisContribution = 1f;

        [Header("Volume")]
        [SerializeField] protected float minVolume = 0f;
        [SerializeField] protected float maxVolume = 1f;
        [SerializeField] protected AnimationCurve volumeCurve = AnimationCurve.Linear(0, 0, 1, 1);

        [Header("Pitch")]
        [SerializeField] protected float minPitch = 0f;
        [SerializeField] protected float maxPitch = 1f;
        [SerializeField] protected AnimationCurve pitchCurve = AnimationCurve.Linear(0, 1, 1, 1);

        protected virtual void Reset()
        {
            engines = transform.root.GetComponentInChildren<VehicleEngines3D>(true);
            m_Audio = GetComponentInChildren<AudioSource>(true);
        }

        protected virtual void Awake()
        {
            // Do not subscribe here; audio roots may be toggled. Use OnEnable/OnDisable.
            currentLevel = CalculateLevel();
        }

        protected virtual void OnEnable()
        {
            // Re-acquire references (important when enabling later due to swaps)
            if (engines == null) engines = transform.root.GetComponentInChildren<VehicleEngines3D>(true);
            if (m_Audio == null) m_Audio = GetComponentInChildren<AudioSource>(true);

            if (engines != null)
            {
                engines.onEnginesActivated.RemoveListener(OnEnginesActivated);
                engines.onEnginesDeactivated.RemoveListener(OnEnginesDeactivated);

                engines.onEnginesActivated.AddListener(OnEnginesActivated);
                engines.onEnginesDeactivated.AddListener(OnEnginesDeactivated);
            }

            // Keep play-on-awake off; we control playback explicitly
            if (m_Audio != null)
            {
                m_Audio.playOnAwake = false;
                m_Audio.loop = true;
            }

            // Sync immediately so volume/pitch are correct on first frame
            currentLevel = CalculateLevel();
            SetAudioLevel(currentLevel);

            // If engines are already active (common during swaps), start the loop now
            if (m_Audio != null && EnginesAreActive())
            {
                if (!m_Audio.isPlaying)
                {
                    m_Audio.volume = 0f;
                    m_Audio.loop = true;
                    m_Audio.Play();
                }
            }
        }

        protected virtual void OnDisable()
        {
            if (engines != null)
            {
                engines.onEnginesActivated.RemoveListener(OnEnginesActivated);
                engines.onEnginesDeactivated.RemoveListener(OnEnginesDeactivated);
            }

            if (m_Audio != null && m_Audio.isPlaying)
            {
                m_Audio.Stop();
            }
        }

        protected virtual void OnEnginesActivated()
        {
            if (m_Audio != null)
            {
                m_Audio.volume = 0f;
                m_Audio.loop = true;
                if (!m_Audio.isPlaying) m_Audio.Play();
            }
        }

        protected virtual void OnEnginesDeactivated()
        {
            if (m_Audio != null)
            {
                m_Audio.Stop();
            }
        }

        protected virtual void SetAudioLevel(float level)
        {
            if (m_Audio == null) return;

            float volumeAmount = volumeCurve.Evaluate(level);
            m_Audio.volume = volumeAmount * maxVolume + (1f - volumeAmount) * minVolume;

            float pitchAmount = pitchCurve.Evaluate(level);
            m_Audio.pitch = pitchAmount * maxPitch + (1f - pitchAmount) * minPitch;
        }

        protected virtual float CalculateLevel()
        {
            if (engines == null) return 0f;

            float level = 0f;

            switch (axisContribution)
            {
                case AxisContributionType.Maximum:
                    switch (controlType)
                    {
                        case EngineAudioControlType.Movement:
                            level = Mathf.Max(level, Mathf.Abs(engines.ModulatedMovementInputs.x) * xAxisContribution);
                            level = Mathf.Max(level, Mathf.Abs(engines.ModulatedMovementInputs.y) * yAxisContribution);
                            level = Mathf.Max(level, Mathf.Abs(engines.ModulatedMovementInputs.z) * zAxisContribution);
                            break;

                        case EngineAudioControlType.Boost:
                            level = Mathf.Max(level, Mathf.Abs(engines.ModulatedBoostInputs.x) * xAxisContribution);
                            level = Mathf.Max(level, Mathf.Abs(engines.ModulatedBoostInputs.y) * yAxisContribution);
                            level = Mathf.Max(level, Mathf.Abs(engines.ModulatedBoostInputs.z) * zAxisContribution);
                            break;

                        case EngineAudioControlType.Steering:
                            level = Mathf.Max(level, Mathf.Abs(engines.ModulatedSteeringInputs.x) * xAxisContribution);
                            level = Mathf.Max(level, Mathf.Abs(engines.ModulatedSteeringInputs.y) * yAxisContribution);
                            level = Mathf.Max(level, Mathf.Abs(engines.ModulatedSteeringInputs.z) * zAxisContribution);
                            break;
                    }
                    break;

                default: // Cumulative
                    switch (controlType)
                    {
                        case EngineAudioControlType.Movement:
                            level += Mathf.Abs(engines.ModulatedMovementInputs.x) * xAxisContribution;
                            level += Mathf.Abs(engines.ModulatedMovementInputs.y) * yAxisContribution;
                            level += Mathf.Abs(engines.ModulatedMovementInputs.z) * zAxisContribution;
                            break;

                        case EngineAudioControlType.Boost:
                            level += Mathf.Abs(engines.ModulatedBoostInputs.x) * xAxisContribution;
                            level += Mathf.Abs(engines.ModulatedBoostInputs.y) * yAxisContribution;
                            level += Mathf.Abs(engines.ModulatedBoostInputs.z) * zAxisContribution;
                            break;

                        case EngineAudioControlType.Steering:
                            level += Mathf.Abs(engines.ModulatedSteeringInputs.x) * xAxisContribution;
                            level += Mathf.Abs(engines.ModulatedSteeringInputs.y) * yAxisContribution;
                            level += Mathf.Abs(engines.ModulatedSteeringInputs.z) * zAxisContribution;
                            break;
                    }
                    break;
            }

            return level;
        }

        protected virtual void LateUpdate()
        {
            if (engines == null) return;

            currentLevel = Mathf.Lerp(currentLevel, CalculateLevel(), changeSpeed * Time.deltaTime);
            SetAudioLevel(currentLevel);
        }

        // Best-effort detection: if we can’t read a flag, assume active (common for always-on flight engines).
        private bool EnginesAreActive()
        {
            if (engines == null) return false;

            string[] boolNames =
            {
                "EnginesActivated", "enginesActivated",
                "EnginesActive", "IsEnginesActive",
                "IsActive", "Active",
                "IsActivated", "Activated"
            };

            var t = engines.GetType();

            foreach (var n in boolNames)
            {
                var p = t.GetProperty(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p != null && p.PropertyType == typeof(bool) && p.GetIndexParameters().Length == 0)
                {
                    return (bool)p.GetValue(engines, null);
                }

                var f = t.GetField(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null && f.FieldType == typeof(bool))
                {
                    return (bool)f.GetValue(engines);
                }
            }

            return true;
        }
    }
}
