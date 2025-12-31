using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

namespace VSX.UI
{
    /// <summary>
    /// Base class for a component that controls the color of something based on UI events.
    /// </summary>
    public abstract class UIEventColorController : UIEvents
    {
        [Tooltip("The colors to apply for different events.")]
        [SerializeField]
        protected UIEventColors colors;

        [Tooltip("The color transition duration.")]
        [SerializeField]
        protected float transitionDuration = 0.1f;

        protected float transitionStartTime;

        protected Coroutine transitionCoroutine;


        /// <summary>
        /// Called when this component is first added to a game object, or reset in the inspector.
        /// </summary>
        protected virtual void Reset()
        {
            colors = new UIEventColors();
            colors.normalColor = Color.white;
            colors.highlightedColor = Color.black;
            colors.selectedColor = Color.black;
            colors.pressedColor = Color.black;
        }


        protected virtual void OnEnable()
        {
            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == gameObject)
            {
                SetColor(colors.selectedColor);
            }
            else
            {
                SetColor(colors.normalColor);
            }
        }


        /// <summary>
        /// Get the current color of the element.
        /// </summary>
        /// <returns>The current color.</returns>
        protected virtual Color GetCurrentColor()
        {
            return Color.clear;
        }


        /// <summary>
        /// Set the color of the element.
        /// </summary>
        /// <param name="color">The color to set.</param>
        protected virtual void SetColor(Color color) { }


        /// <summary>
        /// Called when a UI event occurs.
        /// </summary>
        protected override void OnUIEvent()
        {
            if (isSelected)
            {
                BeginTransition(colors.selectedColor);
            }
            else
            {
                if (isPressed)
                {
                    BeginTransition(colors.pressedColor);
                }
                else
                {
                    BeginTransition(isHighlighted ? colors.highlightedColor : colors.normalColor);
                }
            }
        }


        /// <summary>
        /// Begin a color transition.
        /// </summary>
        /// <param name="targetColor">The color to transition to.</param>
        protected virtual void BeginTransition(Color targetColor)
        {
            if (transitionCoroutine != null) StopCoroutine(transitionCoroutine);

            if (gameObject.activeInHierarchy) transitionCoroutine = StartCoroutine(TransitionCoroutine(GetCurrentColor(), targetColor));
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
    }
}

