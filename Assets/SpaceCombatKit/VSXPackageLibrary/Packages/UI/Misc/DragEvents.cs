using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

namespace VSX.UI
{
    /// <summary>
    /// Call Unity events when drag events occur on this UI item.
    /// </summary>
    public class DragEvents : MonoBehaviour, IBeginDragHandler, IEndDragHandler
    {
        [Tooltip("Unity event called when this UI item starts being dragged.")]
        [SerializeField]
        protected UnityEvent onBeginDrag;

        [Tooltip("Unity event called when this UI item stops being dragged.")]
        [SerializeField]
        protected UnityEvent onEndDrag;

        [Tooltip("Unity event called when this UI item is dragged by some amount.")]
        [SerializeField]
        protected UnityEvent onDrag;


        /// <summary>
        /// Called when this UI item starts being dragged.
        /// </summary>
        /// <param name="eventData">The drag event data.</param>
        public virtual void OnBeginDrag(PointerEventData eventData)
        {
            onBeginDrag.Invoke();
        }


        /// <summary>
        /// Called when this UI item stops being dragged.
        /// </summary>
        /// <param name="eventData">The drag event data.</param>
        public virtual void OnEndDrag(PointerEventData eventData)
        {
            onEndDrag.Invoke();
        }


        /// <summary>
        /// Called when this UI item is dragged by some amount.
        /// </summary>
        /// <param name="eventData">The drag event data.</param>
        public void OnDrag(PointerEventData data)
        {
            onDrag.Invoke();
        }
    }
}

