using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace VSX.Utilities
{
    /// <summary>
    /// Base class for a component that can set the color of something to different colors, based on e.g. Unity Events.
    /// </summary>
    public abstract class ColorSelector : MonoBehaviour
    {
        [Tooltip("The list of colors that can be selected.")]
        [SerializeField]
        protected List<Color> colors = new List<Color>();

        [Tooltip("The list index of the color to initially apply.")]
        [SerializeField]
        protected int initialColorIndex = 0;


        /// <summary>
        /// Set the color by index.
        /// </summary>
        /// <param name="index">The color index.</param>
        public virtual void SetColor(int index)
        {
            if (index >= 0 && index < colors.Count)
            {
                SetColor(colors[index]);
            }
        }


        /// <summary>
        /// Set the color.
        /// </summary>
        /// <param name="color">The new color.</param>
        protected virtual void SetColor(Color color) { }
    }
}

