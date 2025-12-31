using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace VSX.UI
{
    /// <summary>
    /// Derived class for controlling a UGUI Text component.
    /// </summary>
    public class TextControllerUGUI : TextController
    {

        [Tooltip("The UGUI text component reference.")]
        [SerializeField]
        protected Text textUGUI;


        /// <summary>
        /// Get/set the text contents.
        /// </summary>
        public override string text
        {
            get { return textUGUI.text; }
            set { textUGUI.text = value; }
        }


        /// <summary>
        /// Get/set the text color.
        /// </summary>
        public override Color color
        {
            get { return textUGUI.color; }
            set { textUGUI.color = value; }
        }


        /// <summary>
        /// Called when the component is first added to a game object, or reset in the inspector.
        /// </summary>
        protected virtual void Reset()
        {
            textUGUI = GetComponent<Text>();
        }
    }
}
