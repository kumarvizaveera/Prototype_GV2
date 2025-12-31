using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

namespace VSX.UI
{
    /// <summary>
    /// A UI popup (e.g. for a menu).
    /// </summary>
    public class PopupController : MonoBehaviour
    {
        [Tooltip("The UI object to select when the popup appears (e.g. a button, to enable UI navigation).")]
        [SerializeField]
        protected GameObject firstSelectedUIObject;


        protected bool isOpen;
        /// <summary>
        /// Whether this popup is showing.
        /// </summary>
        public virtual bool IsOpen { get => isOpen; }


        /// <summary>
        /// Open the popup.
        /// </summary>
        public virtual void Open()
        {
            isOpen = true;

            gameObject.SetActive(true);

            StartCoroutine(SetFirstSelectedUIObjectCoroutine());
        }


        /// <summary>
        /// Close the popup.
        /// </summary>
        public virtual void Close()
        {
            isOpen = false;
            gameObject.SetActive(false);
        }


        /// <summary>
        /// Coroutine that selects the First Selected UI Object at the end of the frame. Need to wait until end of frame, otherwise 
        /// PointerEnter events may get called on the cursor and change the Event System's selected object in the same frame.
        /// </summary>
        /// <returns></returns>
        protected virtual IEnumerator SetFirstSelectedUIObjectCoroutine()
        {
            yield return new WaitForEndOfFrame();
            if (isOpen)
            {
                if (firstSelectedUIObject != null)
                {
                    EventSystem.current.SetSelectedGameObject(firstSelectedUIObject);
                }
            }
        }
    }
}

