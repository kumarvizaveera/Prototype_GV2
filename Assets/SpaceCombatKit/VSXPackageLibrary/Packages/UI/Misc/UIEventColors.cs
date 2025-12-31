using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace VSX.UI
{
    /// <summary>
    /// Stores colors for different UI states/events.
    /// </summary>
    [System.Serializable]
    public class UIEventColors
    {
        [Tooltip("The default color.")]
        public Color normalColor;

        [Tooltip("The color to apply when the UI object is highlighted.")]
        public Color highlightedColor;

        [Tooltip("The color to apply when the UI object is pressed.")]
        public Color pressedColor;

        [Tooltip("The color to apply when the UI object is selected.")]
        public Color selectedColor;
    }
}
