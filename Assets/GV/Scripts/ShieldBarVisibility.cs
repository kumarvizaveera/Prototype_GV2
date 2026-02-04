using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VSX.Health;

namespace GV.UI
{
    /// <summary>
    /// Controls the visibility of the Shield UI Bar based on the shield's active state.
    /// </summary>
    public class ShieldBarVisibility : MonoBehaviour
    {
        [Tooltip("The EnergyShieldController to monitor.")]
        public EnergyShieldController shieldController;

        [Tooltip("The UI visualization object to hide/show (e.g. the Image or Parent Object). Do NOT assign the object containing this script.")]
        public GameObject uiVisuals;

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
            }
        }
    }
}
