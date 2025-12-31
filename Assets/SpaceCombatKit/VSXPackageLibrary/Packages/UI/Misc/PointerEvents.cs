using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

namespace VSX.UI
{
    /// <summary>
    /// A unity event passing Pointer Event Data.
    /// </summary>
    [System.Serializable]
    public class PointerEvent : UnityEvent<PointerEventData> { }

    /// <summary>
    /// A unity event passing Base Event Data.
    /// </summary>
    [System.Serializable]
    public class BaseEvent : UnityEvent<BaseEventData> { }


    /// <summary>
    /// Call Unity Events based on pointer UI events.
    /// </summary>
    public class PointerEvents : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler, ISubmitHandler
    {
        [Tooltip("Unity Event called when the pointer enters this UI object.")]
        public PointerEvent onPointerEnter;

        [Tooltip("Unity Event called when the pointer exits this UI object.")]
        public PointerEvent onPointerExit;

        [Tooltip("Unity Event called when a pointer up event occurs on this UI object.")]
        public PointerEvent onPointerUp;

        [Tooltip("Unity Event called when a pointer down event occurs on this UI object.")]
        public PointerEvent onPointerDown;

        [Tooltip("Unity Event called when the pointer clicks on this UI object.")]
        public PointerEvent onPointerClick;

        [Tooltip("Unity Event called when the a Submit event occurs on this UI object.")]
        public BaseEvent onSubmit;


        /// <summary>
        /// Called when the pointer enters this UI object.
        /// </summary>
        /// <param name="eventData">The event data.</param>
        public virtual void OnPointerEnter(PointerEventData eventData)
        {
            onPointerEnter.Invoke(eventData);
        }


        /// <summary>
        /// Called when the pointer exits this UI object.
        /// </summary>
        /// <param name="eventData">The event data.</param>
        public virtual void OnPointerExit(PointerEventData eventData)
        {
            onPointerExit.Invoke(eventData);
        }


        /// <summary>
        /// Called when a pointer down event occurs on this UI object.
        /// </summary>
        /// <param name="eventData">The event data.</param>
        public void OnPointerDown(PointerEventData eventData)
        {
            onPointerDown.Invoke(eventData);
        }


        /// <summary>
        /// Called when a pointer up event occurs on this UI object.
        /// </summary>
        /// <param name="eventData">The event data.</param>
        public void OnPointerUp(PointerEventData eventData)
        {
            onPointerUp.Invoke(eventData);
        }


        /// <summary>
        /// Called when a pointer click event occurs on this UI object.
        /// </summary>
        /// <param name="eventData">The event data.</param>
        public virtual void OnPointerClick(PointerEventData eventData)
        {
            onPointerClick.Invoke(eventData);
        }


        /// <summary>
        /// Called when a Submit event occurs on this UI object.
        /// </summary>
        /// <param name="eventData">The event data.</param>
        public virtual void OnSubmit(BaseEventData eventData)
        {
            onSubmit.Invoke(eventData);
        }
    }
}
