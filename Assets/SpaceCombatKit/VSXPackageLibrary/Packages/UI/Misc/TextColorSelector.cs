using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VSX.Utilities;

namespace VSX.UI
{
    /// <summary>
    /// Set a Text Controller's color to different colors from a list, based on e.g. Unity Events.
    /// </summary>
    public class TextColorSelector : ColorSelector
    {
        [Tooltip("The Text Controller to set the color of.")]
        [SerializeField]
        protected TextController text;


        /// <summary>
        /// Set the color of the Text Controller.
        /// </summary>
        /// <param name="color">The new color.</param>
        protected override void SetColor(Color color)
        {
            text.color = color;
        }
    }
}
