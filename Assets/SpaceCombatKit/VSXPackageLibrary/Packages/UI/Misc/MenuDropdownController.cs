using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;


namespace VSX.UI
{
    /// <summary>
    /// Controls a dropdown consisting of gameobjects on the UI.
    /// </summary>
    public class MenuDropdownController : MonoBehaviour
    {
        [Tooltip("The text displaying the label of the dropdown.")]
        [SerializeField] 
        protected TextController labelText;

        [Tooltip("The text displaying the description to display for the dropdown (e.g. when the pointer hovers on it).")]
        [SerializeField]
        protected TextController descriptionText;

        [Tooltip("Whether the dropdown is folded or not.")]
        [SerializeField]
        protected bool folded = true;
        public virtual bool Folded
        {
            get => folded;
            set => SetIsFolded(value);
        }

        [Tooltip("The game objects under the menu.")]
        [SerializeField]
        protected List<GameObject> elements = new List<GameObject>();

        [Tooltip("The Unity Event called when the dropdown is folded.")]
        public UnityEvent onFolded;

        [Tooltip("The Unity Event called when the dropdown is unfolded.")]
        public UnityEvent onUnfolded;



        protected virtual void Awake()
        {
            if (folded)
            {
                Fold();
            }
            else
            {
                Unfold();
            }
        }


        /// <summary>
        /// Set the label of the dropdown.
        /// </summary>
        /// <param name="value">The new label.</param>
        public virtual void SetLabel(string value)
        {
            if (labelText != null) labelText.text = value;
        }


        /// <summary>
        /// Set the description of the dropdown.
        /// </summary>
        /// <param name="value">The description.</param>
        public virtual void SetDescription(string value)
        {
            if (descriptionText != null) descriptionText.text = value;
        }


        /// <summary>
        /// Add a gameobject to the dropdown.
        /// </summary>
        /// <param name="newElement">The gameobject to add.</param>
        public virtual void AddElement(GameObject newElement)
        {
            if (elements.IndexOf(newElement) == -1)
            {
                elements.Add(newElement);
                newElement.SetActive(!folded);
            }
        }


        /// <summary>
        /// Remove a gameobject from the dropdown.
        /// </summary>
        /// <param name="elementToRemove">The gameobject to remove.</param>
        public virtual void RemoveElement(GameObject elementToRemove)
        {
            if (elements.IndexOf(elementToRemove) != -1)
            {
                elements.Remove(elementToRemove);
            }
        }


        /// <summary>
        /// Toggle the dropdown.
        /// </summary>
        public virtual void Toggle()
        {
            SetIsFolded(!folded);
        }


        /// <summary>
        /// Set the dropdown to folded or unfolded.
        /// </summary>
        /// <param name="folded">Whether to set folded.</param>
        public virtual void SetIsFolded(bool folded)
        {
            if (folded)
            {
                Fold();
            }
            else
            {
                Unfold();
            }
        }


        /// <summary>
        /// Set the dropdown to unfolded or folded.
        /// </summary>
        /// <param name="unfolded">Whether to set unfolded.</param>
        public virtual void SetIsUnfolded(bool unfolded)
        {
            SetIsFolded(!unfolded);
        }


        /// <summary>
        /// Fold the dropdown.
        /// </summary>
        public virtual void Fold()
        {
            foreach (GameObject element in elements)
            {
                element.SetActive(false);
            }

            folded = true;

            onFolded.Invoke();
        }


        /// <summary>
        /// Unfold the dropdown.
        /// </summary>
        public virtual void Unfold()
        {
            foreach (GameObject element in elements)
            {
                element.SetActive(true);
            }

            folded = false;

            onUnfolded.Invoke();
        }
    }
}

