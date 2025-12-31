using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace VSX.UI
{
    /// <summary>
    /// Auto scroll a menu's Scroll Rect to keep the selected UI object always visible.
    /// </summary>
    public class AutoScroll : MonoBehaviour
    {
        [Tooltip("The menu that the Scroll Rect is part of.")]
        [SerializeField]
        protected Menu menu;

        [Tooltip("The Scroll Rect component to control.")]
        [SerializeField]
        protected ScrollRect scrollRect;

        [Tooltip("The Scroll Rect's visible content window (the mask).")]
        [SerializeField]
        protected RectTransform contentWindow;

        [Tooltip("The item content parent for the Scroll Rect.")]
        [SerializeField]
        protected RectTransform scrollContent;

        [Tooltip("How fast the scroll rect moves to keep the selected item visible.")]
        [SerializeField]
        protected float animationDuration = 1;

        [Tooltip("The movement animation curve for the Scroll Rect positioning.")]
        [SerializeField]
        protected AnimationCurve scrollAnimationCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

        protected Coroutine animationCoroutine;

        protected bool autoScrollEnabled = true;
        public bool AutoScrollEnabled { get => autoScrollEnabled; set => autoScrollEnabled = value; }


        protected virtual void Awake()
        {
            menu.onOpened.AddListener(OnMenuOpened);
            menu.onMenuItemSelected += OnMenuItemSelected;
        }


        /// <summary>
        /// Called when the menu is opened.
        /// </summary>
        protected virtual void OnMenuOpened()
        {
            scrollRect.normalizedPosition = new Vector2(scrollRect.normalizedPosition.x, 1);
        }


        protected virtual void OnMenuItemSelected(MenuItem item)
        {
            if (!autoScrollEnabled) return;

            if (animationCoroutine != null) return;

            if (menu.SelectedMenuItem != null)
            {
                UpdateScroll(menu.SelectedMenuItem.gameObject);
            }
        }


        /// <summary>
        /// Scroll to keep the selected object visible.
        /// </summary>
        /// <param name="selectedObject">The selected object on the UI.</param>
        protected virtual void UpdateScroll(GameObject selectedObject)
        {
            RectTransform selectedObjectRT = selectedObject.GetComponent<RectTransform>();
            if (selectedObjectRT != null)
            {
                float outOfViewDistance = OutOfViewDistance(selectedObjectRT);
                if (!Mathf.Approximately(outOfViewDistance, 0))
                {
                    Vector2 targetPos = new Vector2(scrollRect.normalizedPosition.x, scrollRect.normalizedPosition.y + (outOfViewDistance / (scrollContent.sizeDelta.y - contentWindow.rect.height)));
                    if (animationCoroutine != null) StopCoroutine(animationCoroutine);
                    animationCoroutine = StartCoroutine(ScrollAnimation(targetPos));
                }
            }
        }


        /// <summary>
        /// Get how far out of view a Rect Transform is.
        /// </summary>
        /// <param name="item">The Rect Transform.</param>
        /// <returns>How far out of view it is (how far the Scroll Rect needs to be scrolled).</returns>
        protected virtual float OutOfViewDistance(RectTransform item)
        {

            Vector3[] windowWorldCorners = new Vector3[4];
            contentWindow.GetWorldCorners(windowWorldCorners);

            Vector3[] itemWorldCorners = new Vector3[4];
            item.GetWorldCorners(itemWorldCorners);

            if (itemWorldCorners[1].y > windowWorldCorners[1].y)
            {
                return itemWorldCorners[1].y - windowWorldCorners[1].y;
            }
            else if (itemWorldCorners[0].y < windowWorldCorners[0].y)
            {
                return itemWorldCorners[0].y - windowWorldCorners[0].y;
            }
            else
            {
                return 0;
            }
        }


        /// <summary>
        /// Perform an auto scroll animation.
        /// </summary>
        /// <param name="targetNormalizedPosition">The target position for the Scroll Rect.</param>
        /// <returns></returns>
        protected virtual IEnumerator ScrollAnimation(Vector2 targetNormalizedPosition)
        {
            if (!Mathf.Approximately(animationDuration, 0))
            {
                Vector2 startNormalizedPosition = scrollRect.normalizedPosition;

                float startTime = Time.unscaledTime;

                while (Time.unscaledTime - startTime < animationDuration)
                {
                    float amount = (Time.unscaledTime - startTime) / animationDuration;

                    scrollRect.normalizedPosition = Vector2.Lerp(startNormalizedPosition, targetNormalizedPosition, scrollAnimationCurve.Evaluate(amount));
                    yield return null;
                }
            }

            scrollRect.normalizedPosition = targetNormalizedPosition;

            animationCoroutine = null;
        }
    }
}

