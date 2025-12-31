using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace VSX.UI
{
    /// <summary>
    /// Control the color of a Toggle text when UI events occur.
    /// </summary>
    [RequireComponent(typeof(Toggle))]
    public class ToggleTextColor : UIEvents
    {
        [Tooltip("The text to control the color of.")]
        [SerializeField]
        protected TextController text;

        [Tooltip("The colors to apply to the text when the Toggle is on.")]
        [SerializeField]
        protected UIEventColors toggleOnColors;

        [Tooltip("The colors to apply to the text when the Toggle is off.")]
        [SerializeField]
        protected UIEventColors toggleOffColors;

        [Tooltip("The color transition duration.")]
        [SerializeField]
        protected float transitionDuration = 0.1f;

        // The Toggle component
        protected Toggle toggle;

        protected float transitionStartTime;

        protected Coroutine transitionCoroutine;


        /// <summary>
        /// Called when this component is first added to a game object, or reset in the inspector.
        /// </summary>
        protected virtual void Reset()
        {
            toggleOffColors.normalColor = Color.white;
            toggleOffColors.highlightedColor = Color.white;
            toggleOffColors.selectedColor = Color.white;
            toggleOffColors.pressedColor = Color.white;

            toggleOnColors.normalColor = Color.black;
            toggleOnColors.highlightedColor = Color.black;
            toggleOnColors.selectedColor = Color.black;
            toggleOnColors.pressedColor = Color.black;
        }


        protected virtual void Awake()
        {
            toggle = GetComponent<Toggle>();
            toggle.onValueChanged.AddListener(OnToggleValueChanged);
        }


        protected virtual void OnEnable()
        {
            text.color = toggle.isOn ? toggleOnColors.normalColor : toggleOffColors.normalColor;
        }


        /// <summary>
        /// Called when the Toggle's value changes.
        /// </summary>
        /// <param name="isOn">Whether the toggle is on.</param>
        public virtual void OnToggleValueChanged(bool isOn)
        {
            OnUIEvent();
        }


        /// <summary>
        /// Begin a color transition.
        /// </summary>
        /// <param name="targetColor">The color to transition to.</param>
        protected virtual void BeginTransition(Color targetColor)
        {
            if (transitionCoroutine != null) StopCoroutine(transitionCoroutine);

            if (gameObject.activeInHierarchy) transitionCoroutine = StartCoroutine(TransitionCoroutine(text.color, targetColor));
        }


        /// <summary>
        /// Color transition coroutine.
        /// </summary>
        /// <param name="targetColor">The color to transition to.</param>
        /// <returns></returns>
        protected virtual IEnumerator TransitionCoroutine(Color startColor, Color targetColor)
        {
            transitionStartTime = Time.time;

            while (Time.unscaledTime - transitionStartTime < transitionDuration)
            {
                SetColor(Color.Lerp(startColor, targetColor, (Time.unscaledTime - transitionStartTime) / transitionDuration));
                yield return null;
            }

            SetColor(targetColor);
        }


        /// <summary>
        /// Set the color of the Toggle text.
        /// </summary>
        /// <param name="color">The color.</param>
        protected virtual void SetColor(Color color)
        {
            text.color = color;
        }


        /// <summary>
        /// Called when a UI event occurs.
        /// </summary>
        protected override void OnUIEvent()
        {
            if (isSelected)
            {
                BeginTransition(toggle.isOn ? toggleOnColors.selectedColor : toggleOffColors.selectedColor);
            }
            else
            {
                if (isPressed)
                {
                    BeginTransition(toggle.isOn ? toggleOnColors.pressedColor : toggleOffColors.pressedColor);
                }
                else
                {
                    BeginTransition(isHighlighted ? (toggle.isOn ? toggleOnColors.highlightedColor : toggleOffColors.highlightedColor) :
                                                    (toggle.isOn ? toggleOnColors.normalColor : toggleOffColors.normalColor));
                }
            }
        }
    }
}

