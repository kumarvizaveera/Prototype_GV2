using UnityEngine;
using TMPro;
using VSX.Vehicles;
using System.Collections;
using System.Collections.Generic;

namespace GV.Scripts
{
    [System.Serializable]
    public class MissileMountEntry
    {
        public ModuleMount mount;
        public string label; // Custom label for UI
        [HideInInspector] public bool isUnlocked = false; 
    }

    /// <summary>
    /// Manages a collection of separate Missile Mounts (each representing a missile type),
    /// allowing the player to cycle between only the UNLOCKED ones.
    /// </summary>
    public class MissileCycleController : MonoBehaviour
    {
        public static MissileCycleController Instance; // Singleton for easy access

        [Header("Configuration")]
        [Tooltip("The list of all missile mounts available in the game.")]
        public List<MissileMountEntry> missileMounts = new List<MissileMountEntry>();

        [Tooltip("Key to cycle the missiles.")]
        public KeyCode cycleKey = KeyCode.M;

        [Header("UI")]
        public TMP_Text notificationText;
        public float notificationDuration = 2f;
        public string messagePrefix = "Active Missile: ";

        private int currentIndex = -1;
        private Coroutine hideCoroutine;

        private void Awake()
        {
            Instance = this;

            // Ensure all controlled mounts start disabled
            foreach (var entry in missileMounts)
            {
                if (entry.mount != null)
                {
                    entry.mount.gameObject.SetActive(false);
                }
            }
        }

        private void Start()
        {
            if (notificationText != null) notificationText.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (Input.GetKeyDown(cycleKey))
            {
                CycleNext();
            }
        }

        /// <summary>
        /// Called by UnlockMountOnObjective when a mount should be made available.
        /// </summary>
        public void UnlockMount(ModuleMount mountToUnlock)
        {
            bool wasEmpty = GetUnlockedCount() == 0;

            foreach (var entry in missileMounts)
            {
                if (entry.mount == mountToUnlock)
                {
                    entry.isUnlocked = true;
                    Debug.Log($"MissileCycleController: Unlocked {entry.label}");
                    
                    // Always equip the newly unlocked missile to provide immediate feedback
                    Equip(entry);
                    
                    return;
                }
            }
        }

        public void CycleNext()
        {
            if (GetUnlockedCount() <= 1) return; // No need to cycle if 0 or 1 active

            int attempts = 0;
            int nextIndex = currentIndex;
            
            // Find next unlocked index
            do
            {
                nextIndex = (nextIndex + 1) % missileMounts.Count;
                attempts++;
            } 
            while (!missileMounts[nextIndex].isUnlocked && attempts < missileMounts.Count);

            if (missileMounts[nextIndex].isUnlocked && nextIndex != currentIndex)
            {
                Equip(missileMounts[nextIndex]);
            }
        }

        private void Equip(MissileMountEntry entry)
        {
            // Disable current
            if (currentIndex >= 0 && currentIndex < missileMounts.Count)
            {
                if (missileMounts[currentIndex].mount != null)
                    missileMounts[currentIndex].mount.gameObject.SetActive(false);
            }

            // Enable new
            if (entry.mount != null)
            {
                entry.mount.gameObject.SetActive(true);
                currentIndex = missileMounts.IndexOf(entry);
                ShowNotification(entry.label);
            }
        }
        
        private int GetUnlockedCount()
        {
            int count = 0;
            foreach (var m in missileMounts) if (m.isUnlocked) count++;
            return count;
        }

        private void ShowNotification(string label)
        {
            if (notificationText == null) return;

            string finalName = string.IsNullOrEmpty(label) ? "Unknown" : label;
            notificationText.text = messagePrefix + finalName;
            notificationText.gameObject.SetActive(true);

            if (hideCoroutine != null) StopCoroutine(hideCoroutine);
            hideCoroutine = StartCoroutine(HideTextRoutine());
        }

        private IEnumerator HideTextRoutine()
        {
            yield return new WaitForSeconds(notificationDuration);
            if (notificationText != null)
            {
                notificationText.gameObject.SetActive(false);
            }
        }
    }
}
