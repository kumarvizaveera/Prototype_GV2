using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;

namespace VSX.UI
{
    /// <summary>
    /// Enables functions to be called when a toggle is switched on or off, using Unity Events in the inspector.
    /// </summary>
    [RequireComponent(typeof(Toggle))]
    public class ToggleEvents : MonoBehaviour
    {
        protected Toggle toggle;

        [Tooltip("Unity Event called when the toggle is toggled on.")]
        public UnityEvent onToggledOn;

        [Tooltip("Unity Event called when the toggle is toggled off.")]
        public UnityEvent onToggledOff;


        protected virtual void Awake()
        {
            toggle = GetComponent<Toggle>();
            toggle.onValueChanged.AddListener(OnToggleValueChanged);
        }


        /// <summary>
        /// Called when the toggle's value is changed.
        /// </summary>
        /// <param name="isOn">Whether the toggle is toggled on.</param>
        public virtual void OnToggleValueChanged(bool isOn)
        {
            if (isOn)
            {
                onToggledOn.Invoke();
            }
            else
            {
                onToggledOff.Invoke();
            }
        }
    }
}

