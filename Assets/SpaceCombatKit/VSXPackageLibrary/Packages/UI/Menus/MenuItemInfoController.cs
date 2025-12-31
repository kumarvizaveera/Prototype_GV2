using System.Collections;
using System.Collections.Generic;
using UnityEngine;


namespace VSX.UI
{
    /// <summary>
    /// Show information for a menu item (label, description, etc) on the UI.
    /// </summary>
    public class MenuItemInfoController : MonoBehaviour
    {
        [Tooltip("The game object to toggle to show/hide the information.")]
        [SerializeField]
        protected GameObject handle;

        [Tooltip("The text displaying the menu item's label.")]
        [SerializeField]
        protected TextController labelText;

        [Tooltip("The text displaying the menu item's description.")]
        [SerializeField]
        protected TextController descriptionText;



        protected virtual void Reset()
        {
            handle = gameObject;
        }


        /// <summary>
        /// Show information for a menu item.
        /// </summary>
        /// <param name="item">The menu item.</param>
        public virtual void Show(MenuItem item)
        {
            if (item != null)
            {
                labelText.text = item.Label;
                descriptionText.text = item.Description;

                handle.SetActive(true);
            }
            else
            {
                Close();
            }
        }


        /// <summary>
        /// Hide the information.
        /// </summary>
        public virtual void Close()
        {
            handle.SetActive(false);
        }
    }
}


