using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace VSX.UI
{
    /// <summary>
    /// Base class for a reference to any text component (e.g. UGUI and TMPro).
    /// </summary>
    public abstract class TextController : MonoBehaviour
    {
        /// <summary>
        /// Get/set the text content.
        /// </summary>
        public virtual string text
        {
            get { return ""; }
            set { }
        }


        /// <summary>
        /// Get/set the color of the text.
        /// </summary>
        public virtual Color color
        {
            get { return Color.black; }
            set { }
        }


        /// <summary>
        /// Set the color of the text.
        /// </summary>
        /// <param name="color">The text color.</param>
        public virtual void SetColor(Color color) { }
    }
}
