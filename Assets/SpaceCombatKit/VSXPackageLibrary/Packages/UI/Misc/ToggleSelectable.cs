using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace VSX.UI
{
    /// <summary>
    /// A Selectable component that triggers a Toggle component. Enables multiple Selectable UI objects to control the same Toggle.
    /// </summary>
    [RequireComponent(typeof(Selectable))]
    public class ToggleSelectable : MonoBehaviour, ISelectHandler, IDeselectHandler, IPointerEnterHandler, IPointerClickHandler, ISubmitHandler
    {
        [Tooltip("The Selectable component.")]
        [SerializeField]
        protected Selectable selectable;
        public virtual Selectable Selectable { get => selectable; }

        [Tooltip("The Toggle to control with this Selectable.")]
        [SerializeField] 
        protected Toggle toggle;
        public virtual Toggle Toggle { get => toggle; set => toggle = value; }

        [Tooltip("Unity Event called when the Selectable is selected.")]
        public UnityEvent onSelect;

        [Tooltip("Unity Event called when the Selectable is deselected.")]
        public UnityEvent onDeselect;


        protected virtual void Awake()
        {
            selectable = GetComponent<Selectable>();
        }


        /// <summary>
        /// Called when the pointer enters this Selectable.
        /// </summary>
        /// <param name="eventData">The event data.</param>
        public virtual void OnPointerEnter(PointerEventData eventData)
        {
            selectable.Select();
        }


        /// <summary>
        /// Called when the pointer clicks this Selectable.
        /// </summary>
        /// <param name="eventData">The pointer event data.</param>
        public virtual void OnPointerClick(PointerEventData eventData)
        {
            toggle.isOn = !toggle.isOn;
        }


        /// <summary>
        /// Called when a Submit event occurs on this Selectable.
        /// </summary>
        /// <param name="eventData">The event data.</param>
        public virtual void OnSubmit(BaseEventData eventData)
        {
            toggle.isOn = !toggle.isOn;
        }


        /// <summary>
        /// Called when this Selectable is selected by the event system.
        /// </summary>
        /// <param name="eventData">The event data.</param>
        public virtual void OnSelect(BaseEventData eventData)
        {
            onSelect.Invoke();
        }


        /// <summary>
        /// Called when this Selectable is deselected by the Event System.
        /// </summary>
        /// <param name="eventData">The event data.</param>
        public virtual void OnDeselect(BaseEventData eventData)
        {
            onDeselect.Invoke();
        }
    }
}

