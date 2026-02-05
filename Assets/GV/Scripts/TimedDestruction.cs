using UnityEngine;

namespace GV
{
    /// <summary>
    /// Destroys the GameObject after a specified delay.
    /// Useful for temporary effects like audio or particles.
    /// </summary>
    public class TimedDestruction : MonoBehaviour
    {
        [Tooltip("Time in seconds before the object is destroyed.")]
        public float delay = 2.0f;

        [Tooltip("If true, attempts to set the delay automatically based on an AudioSource clip length.")]
        public bool autoSetFromAudio = true;

        void Start()
        {
            float duration = delay;

            if (autoSetFromAudio)
            {
                AudioSource audioSource = GetComponent<AudioSource>();
                if (audioSource != null && audioSource.clip != null)
                {
                    // Add a tiny buffer to ensure it doesn't cut off exactly at the end
                    duration = audioSource.clip.length + 0.1f;
                }
            }

            Destroy(gameObject, duration);
        }
    }
}
