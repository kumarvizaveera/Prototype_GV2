using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace VSX.UI
{
    /// <summary>
    /// Enables the game to enter a different state when a menu is opened or closed, using Unity Events.
    /// </summary>
    [DefaultExecutionOrder(10)]
    public class MenuConditions : MonoBehaviour
    {
        [Tooltip("The time scale to apply when the menu is opened.")]
        [SerializeField] 
        protected float timeScale = 0;

        [Tooltip("Whether the cursor should be visible when the menu is open.")]
        [SerializeField]
        protected bool cursorVisible = true;

        [Tooltip("The cursor Lock Mode to apply when the menu is open.")]
        [SerializeField]
        protected CursorLockMode cursorLockMode = CursorLockMode.None;

        // The conditions that existed before the menu was opened.
        protected float startingTimeScale;
        protected bool wasCursorVisible;
        protected CursorLockMode startingCursorLockMode;
        protected CursorLockMode? lastCursorLockMode = null;


        /// <summary>
        /// Called when the menu is opened.
        /// </summary>
        public virtual void OnMenuOpen()
        {
            startingTimeScale = Time.timeScale;
            wasCursorVisible = Cursor.visible;
            startingCursorLockMode = Cursor.lockState;

            Time.timeScale = timeScale;
            Cursor.visible = cursorVisible;
            Cursor.lockState = cursorLockMode;
            lastCursorLockMode = cursorLockMode;
        }


        /// <summary>
        /// Called when the menu is closed.
        /// </summary>
        public virtual void OnMenuClose()
        {
            Time.timeScale = startingTimeScale;
            Cursor.visible = wasCursorVisible;
            Cursor.lockState = startingCursorLockMode;
            lastCursorLockMode = startingCursorLockMode;
        }


        /// <summary>
        /// Maintain the correct cursor lock mode when focus returns to the application.
        /// </summary>
        /// <param name="hasFocus">Whether the application has focus.</param>
        protected virtual void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus)
            {
                if (lastCursorLockMode != null) Cursor.lockState = lastCursorLockMode.Value;
            }
        }


        // Restore the timescale when the scene is unloaded. Prevents time scale from getting stuck frozen
        // e.g. when restarting the game from the menu.
        protected virtual void OnDisable()
        {
            Time.timeScale = 1;
        }
    }
}
