using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using VSX.Utilities;

namespace VSX.UI
{
    /// <summary>
    /// Enables the setting of an Image component's color by list index, based on e.g. Unity Events.
    /// </summary>
    public class ImageColorSelector : ColorSelector
    {
        [Tooltip("The Image component to set the color of.")]
        [SerializeField]
        protected Image m_Image;


        /// <summary>
        /// Set the color of the image.
        /// </summary>
        /// <param name="color">The color to set.</param>
        protected override void SetColor(Color color)
        {
            m_Image.color = color;
        }
    }
}
