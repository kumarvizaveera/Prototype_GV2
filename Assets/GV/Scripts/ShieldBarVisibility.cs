using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using VSX.Health;

namespace GV.UI
{
    /// <summary>
    /// Controls the visibility AND fill amount of the Shield UI Bar
    /// based on the shield's active state and current health.
    /// </summary>
    public class ShieldBarVisibility : MonoBehaviour
    {
        [Tooltip("The EnergyShieldController to monitor.")]
        public EnergyShieldController shieldController;

        [Tooltip("The UI visualization object to hide/show (e.g. the Image or Parent Object). Do NOT assign the object containing this script.")]
        public GameObject uiVisuals;

        [Tooltip("Optional: Image whose fillAmount is driven by shield health (0-1). " +
                 "If left empty, only visibility is controlled.")]
        public Image shieldFillBar;

        void Update()
        {
            if (shieldController != null && uiVisuals != null)
            {
                bool shouldBeVisible = shieldController.IsShieldActive;

                if (uiVisuals.activeSelf != shouldBeVisible)
                {
                    if (uiVisuals == gameObject)
                    {
                        Debug.LogError("[ShieldBarVisibility] ERROR: 'UI Visuals' is assigned to the same GameObject as this script! The script has disabled itself and cannot run anymore. Please assign a child object to 'UI Visuals'.");
                    }
                    uiVisuals.SetActive(shouldBeVisible);
                }

                // Drive the fill bar based on shield health fraction
                if (shouldBeVisible && shieldFillBar != null)
                {
                    shieldFillBar.fillAmount = shieldController.ShieldHealthFraction;
                }
            }
        }
    }
}
