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
        public bool startUnlocked = false;
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

        [Header("UI - Active Status")]
        [Tooltip("The TextMeshPro UGUI component to display the active missile name when cycling.")]
        public TMP_Text activeNotificationText;
        public string activeMessagePrefix = "Active Missile: ";

        [Header("UI - Unlock Popup")]
        [Tooltip("The TextMeshPro UGUI component to display the unlock popup.")]
        public TMP_Text unlockNotificationText;
        public string unlockMessagePrefix = "WEAPON UNLOCKED: ";

        public float notificationDuration = 2f;

        private int currentIndex = -1;
        private Coroutine activeHideCoroutine;
        private Coroutine unlockHideCoroutine;

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
            // Initialize unlocks based on startUnlocked setting
            bool anyUnlocked = false;
            foreach (var entry in missileMounts)
            {
                if (entry.startUnlocked)
                {
                    entry.isUnlocked = true;
                    anyUnlocked = true;
                }
            }

            if (unlockNotificationText != null) unlockNotificationText.gameObject.SetActive(false);

            // Initial state
            if (anyUnlocked)
            {
                // Find the first unlocked one and equip it
                for (int i = 0; i < missileMounts.Count; i++)
                {
                    if (missileMounts[i].isUnlocked)
                    {
                        Equip(missileMounts[i], true); // Show active notification for initial equip
                        break;
                    }
                }
            }
            else
            {
                ShowActiveNotification("None");
            }
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
                    
                    // Show Unlock Popup
                    ShowUnlockNotification(entry.label);

                    // Always equip the newly unlocked missile to provide immediate feedback
                    Equip(entry, true); // Active text is now persistent, so we update it even on unlock.
                    
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
                Equip(missileMounts[nextIndex], true);
            }
        }

        private void Equip(MissileMountEntry entry, bool showActiveNotification = true)
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
                
                if (showActiveNotification)
                {
                    ShowActiveNotification(entry.label);
                }
            }
        }
        
        private int GetUnlockedCount()
        {
            int count = 0;
            foreach (var m in missileMounts) if (m.isUnlocked) count++;
            return count;
        }

        private void ShowActiveNotification(string label)
        {
            if (activeNotificationText == null) return;

            string finalName = string.IsNullOrEmpty(label) ? "Unknown" : label;
            activeNotificationText.text = activeMessagePrefix + finalName;
            activeNotificationText.gameObject.SetActive(true);

            // Persistent: Do NOT hide this text.
            if (activeHideCoroutine != null) StopCoroutine(activeHideCoroutine);
        }

        private void ShowUnlockNotification(string label)
        {
            if (unlockNotificationText == null) return;

            string finalName = string.IsNullOrEmpty(label) ? "Unknown" : label;
            unlockNotificationText.text = unlockMessagePrefix + finalName;
            unlockNotificationText.gameObject.SetActive(true);

            if (unlockHideCoroutine != null) StopCoroutine(unlockHideCoroutine);
            unlockHideCoroutine = StartCoroutine(HideTextRoutine(unlockNotificationText));
        }

        private IEnumerator HideTextRoutine(TMP_Text textComp)
        {
            yield return new WaitForSeconds(notificationDuration);
            if (textComp != null)
            {
                textComp.gameObject.SetActive(false);
            }
        }
    }
}
