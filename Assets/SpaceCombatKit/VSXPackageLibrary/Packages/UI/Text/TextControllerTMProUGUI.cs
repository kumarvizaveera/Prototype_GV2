using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

namespace VSX.UI
{
    /// <summary>
    /// Derived class for TMPro UGUI text
    /// </summary>
    public class TextControllerTMProUGUI : TextController
    {
        [Tooltip("The Text Mesh Pro text reference.")]
        [SerializeField]
        protected TextMeshProUGUI textTMProUGUI;


        /// <summary>
        /// Get/set the text contents.
        /// </summary>
        public override string text
        {
            get { return textTMProUGUI.text; }
            set { textTMProUGUI.text = value; }
        }


        /// <summary>
        /// Get/set the color.
        /// </summary>
        public override Color color
        {
            get { return textTMProUGUI.color; }
            set { textTMProUGUI.color = value; }
        }


        /// <summary>
        /// Called when the component is first added to a game object, or reset in the inspector.
        /// </summary>
        protected virtual void Reset()
        {
            textTMProUGUI = GetComponent<TextMeshProUGUI>();
        }
    }
}

