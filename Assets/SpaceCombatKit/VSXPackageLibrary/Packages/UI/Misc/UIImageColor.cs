using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace VSX.UI
{
    /// <summary>
    /// Set the color of a UGUI Image component based on UI events.
    /// </summary>
    public class UIImageColor : UIEventColorController
    {
        [Tooltip("The Image component to control the color of.")]
        [SerializeField] 
        protected Image m_Image;


        /// <summary>
        /// Get the Image component's current color.
        /// </summary>
        /// <returns>The Image component's current color.</returns>
        protected override Color GetCurrentColor()
        {
            return m_Image.color;
        }


        /// <summary>
        /// Set the Image component's color.
        /// </summary>
        /// <param name="color">The color to set to.</param>
        protected override void SetColor(Color color)
        {
            m_Image.color = color;
        }
    }
}
