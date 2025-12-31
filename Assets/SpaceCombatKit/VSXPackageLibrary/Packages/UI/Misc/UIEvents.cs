using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace VSX.UI
{
    /// <summary>
    /// Base class for a component that responds to UI events.
    /// </summary>
    public abstract class UIEvents : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler,
                                                IDeselectHandler, IPointerDownHandler, IPointerUpHandler
    {
        // Whether this UI object is highlighted.
        protected bool isHighlighted;

        // Whether this UI object is selected.
        protected bool isSelected;

        // Whether this UI object is pressed.
        protected bool isPressed;



        protected virtual void OnDisable()
        {
            isHighlighted = false;
            isSelected = false;
            isPressed = false;
        }


        /// <summary>
        /// Called when the pointer enters this UI element.
        /// </summary>
        /// <param name="eventData">The pointer event data.</param>
        public virtual void OnPointerEnter(PointerEventData eventData)
        {
            isHighlighted = true;

            OnUIEvent();
        }


        /// <summary>
        /// Called when the pointer exits this UI element.
        /// </summary>
        /// <param name="eventData">The pointer event data.</param>
        public virtual void OnPointerExit(PointerEventData eventData)
        {
            isHighlighted = false;
            isPressed = false;

            OnUIEvent();
        }


        /// <summary>
        /// Called a pointer down event occurs on this UI element.
        /// </summary>
        /// <param name="eventData">The pointer event data.</param>
        public virtual void OnPointerDown(PointerEventData eventData)
        {
            isPressed = true;

            OnUIEvent();
        }


        /// <summary>
        /// Called a pointer up event occurs on this UI element.
        /// </summary>
        /// <param name="eventData">The pointer event data.</param>
        public virtual void OnPointerUp(PointerEventData eventData)
        {
            isPressed = false;

            OnUIEvent();
        }


        /// <summary>
        /// Called when this UI element is selected.
        /// </summary>
        /// <param name="eventData">The event data.</param>
        public virtual void OnSelect(BaseEventData eventData)
        {
            isSelected = true;

            OnUIEvent();
        }


        /// <summary>
        /// Called when this UI element is deselected.
        /// </summary>
        /// <param name="eventData">The event data.</param>
        public virtual void OnDeselect(BaseEventData eventData)
        {
            isSelected = false;

            OnUIEvent();
        }


        /// <summary>
        /// Called when a UI event occurs.
        /// </summary>
        protected virtual void OnUIEvent() { }
    }
}
