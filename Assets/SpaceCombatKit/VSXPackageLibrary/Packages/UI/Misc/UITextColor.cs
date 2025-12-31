using System.Collections;
using UnityEngine;

namespace VSX.UI
{
    /// <summary>
    /// Set the color of a Text Controller component, based on UI events.
    /// </summary>
    public class UITextColor : UIEventColorController
    {
        [Tooltip("The text controller to control the color of.")]
        [SerializeField]
        protected TextController text;


        /// <summary>
        /// Get the current text color.
        /// </summary>
        /// <returns>The current text color.</returns>
        protected override Color GetCurrentColor()
        {
            return text.color;
        }


        /// <summary>
        /// Set the text color.
        /// </summary>
        /// <param name="color">The color to set.</param>
        protected override void SetColor(Color color)
        {
            text.color = color;
        }
    }
}

