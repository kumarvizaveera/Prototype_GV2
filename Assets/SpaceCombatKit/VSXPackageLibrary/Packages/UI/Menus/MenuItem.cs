using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace VSX.UI
{
    /// <summary>
    /// Designates a menu item for a menu.
    /// </summary>
    public class MenuItem : MonoBehaviour, ISelectHandler, IDeselectHandler, IPointerEnterHandler, IPointerClickHandler, ISubmitHandler
    {
        [Tooltip("Whether to select this menu item when the a Pointer Enter event is detected on it.")]
        [SerializeField]
        protected bool selectOnPointerEnter = true;

        protected bool isSelected;
        /// <summary>
        /// Whether this menu item is selected.
        /// </summary>
        public bool IsSelected { get => isSelected; }

        /// <summary>
        /// The label of this menu item.
        /// </summary>
        public virtual string Label { get => ""; }

        /// <summary>
        /// The description of this menu item.
        /// </summary>
        public virtual string Description { get => ""; }

        [Tooltip("Unity Event called when this menu item is selected on the menu.")]
        public UnityEvent onSelected;

        [Tooltip("Unity Event called when this menu item is deselected on the menu.")]
        public UnityEvent onDeselected;

        protected Selectable selectable;
        public virtual Selectable Selectable { get => selectable; }



        protected virtual void Awake()
        {
            if (selectable == null)
            {
                selectable = GetComponent<Selectable>();
            }
        }


        /// <summary>
        /// Set this menu item as selected or unselected.
        /// </summary>
        /// <param name="selected">Whether to set selected.</param>
        public virtual void SetSelected(bool selected)
        {
            if (selected == isSelected) return;

            isSelected = selected;

            if (isSelected)
            {
                OnSelected();
            }
            else
            {
                OnDeselected();
            }
        }


        /// <summary>
        /// Called when this menu item becomes selected.
        /// </summary>
        protected virtual void OnSelected() 
        {
            onSelected.Invoke();
        }


        /// <summary>
        /// Called when this menu item becomes deselected.
        /// </summary>
        protected virtual void OnDeselected() 
        {
            onDeselected.Invoke();
        }


        /// <summary>
        /// Update this menu item's UI.
        /// </summary>
        public virtual void UpdateUI() { }


        /// <summary>
        /// Called when this UI object is selected by the Event System.
        /// </summary>
        /// <param name="eventData">The event data.</param>
        public virtual void OnSelect(BaseEventData eventData)
        {
            if (isSelected) return;

            SetSelected(true);
        }


        /// <summary>
        /// Called when this UI object is deselected by the Event System.
        /// </summary>
        /// <param name="eventData">The event data.</param>
        public virtual void OnDeselect(BaseEventData eventData)
        {
            if (!isSelected) return;

            SetSelected(false);
        }


        /// <summary>
        /// Called when the pointer enters this menu item.
        /// </summary>
        /// <param name="eventData">The event data.</param>
        public virtual void OnPointerEnter(PointerEventData eventData)
        {
            if (selectOnPointerEnter)
            {
                if (selectable != null)
                {
                    selectable.Select();
                }
            }
        }
        

        /// <summary>
        /// Called when a pointer clicks on this menu item.
        /// </summary>
        /// <param name="eventData">The event data.</param>
        public virtual void OnPointerClick(PointerEventData eventData) { }


        /// <summary>
        /// Called when a submit event (e.g. press enter) occurs on this menu item.
        /// </summary>
        /// <param name="eventData">The event data.</param>
        public virtual void OnSubmit(BaseEventData eventData) { }
    }
}

