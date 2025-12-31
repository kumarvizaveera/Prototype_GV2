using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace VSX.UI
{
    /// <summary>
    /// Represents a group of menus operating within a single context (e.g. Pause menu and Options menu).
    /// Handles passing of input to the active menu, makes other menus in the group aware when one of them is opened or closed, etc.
    /// </summary>
    public class MenuGroup : MonoBehaviour
    {
        [Tooltip("Whether to open this menu group when the scene starts.")]
        [SerializeField]
        protected bool openOnStart;

        [Tooltip("The root menu - also the one that is opened first when this Menu Group is opened.")]
        [SerializeField]
        protected Menu rootMenu;

        // All the menus belonging to this Menu Group.
        protected List<Menu> menus = new List<Menu>();

        protected Menu activeMenu;
        /// <summary>
        /// The currently active menu in this Menu Group.
        /// </summary>
        public Menu ActiveMenu { get { return activeMenu; } }

        /// <summary>
        /// Whether this menu group is currently open.
        /// </summary>
        public virtual bool IsOpen { get { return activeMenu != null; } }

        [Tooltip("Unity Event called when this menu group is opened.")]
        public UnityEvent onOpened;

        [Tooltip("Unity Event called when this menu group is closed.")]
        public UnityEvent onClosed;

        [Tooltip("Unity Action called when a menu in this group is opened.")]
        public UnityAction<Menu> onMenuOpened;

        protected bool menuToggledThisFrame = false;



        protected virtual void Awake()
        {
            menus = new List<Menu>(GetComponentsInChildren<Menu>(true));
            foreach(Menu menu in menus)
            {
                menu.onOpened.AddListener(() => { OnMenuOpened(menu); });
                menu.onClosed.AddListener(() => { OnMenuClosed(menu); });

                onMenuOpened += menu.OnMenuGroupMenuOpened;
            }

            rootMenu.onExited += Close;
        }


        protected virtual void Start()
        {
            if (openOnStart) Open();
        }


        /// <summary>
        /// Open the menu group.
        /// </summary>
        public virtual void Open()
        {
            if (menuToggledThisFrame) return;

            if (activeMenu != null) return;

            rootMenu.Open();

            onOpened.Invoke();

            StartCoroutine(MenuToggleResetCoroutine());
        }


        /// <summary>
        /// Close the menu group.
        /// </summary>
        public virtual void Close()
        {
            if (menuToggledThisFrame) return;

            if (!rootMenu.Exitable) return;

            if (activeMenu != null)
            {
                activeMenu.Close();
            }

            onClosed.Invoke();

            StartCoroutine(MenuToggleResetCoroutine());

        }


        /// <summary>
        /// Toggle the menu group open/closed.
        /// </summary>
        public virtual void ToggleMenu()
        {
            if (activeMenu != null)
            {
                Close();
            }
            else
            {
                Open();
            }
        }


        /// <summary>
        /// Coroutine to prevent the menu from being opened and closed in the same frame.
        /// </summary>
        /// <returns></returns>
        protected IEnumerator MenuToggleResetCoroutine()
        {
            menuToggledThisFrame = true;

            yield return new WaitForEndOfFrame();

            menuToggledThisFrame = false;
        }


        /// <summary>
        /// Called when a menu under this menu group is opened.
        /// </summary>
        /// <param name="menu">The menu that was opened.</param>
        protected virtual void OnMenuOpened(Menu menu)
        {
            activeMenu = menu;

            onMenuOpened.Invoke(menu);
        }


        /// <summary>
        /// Called when a menu under this menu group is closed.
        /// </summary>
        /// <param name="menu">The menu that was closed.</param>
        protected virtual void OnMenuClosed(Menu menu)
        {
            if (activeMenu == menu)
            {
                activeMenu = null;
            }
        }
        
   
        /// <summary>
        /// Perform the Cancel action on the currently active menu.
        /// </summary>
        public virtual void CancelAction()
        {
            if (!IsOpen) return;

            if (menuToggledThisFrame) return;

            activeMenu.CancelAction();
        }


        /// <summary>
        /// Open the next child menu of the currently active menu.
        /// </summary>
        /// <param name="loop">Whether to loop through the child menus.</param>
        public virtual void OpenNextChildMenu(bool loop = false)
        {
            if (activeMenu != null && activeMenu.ParentMenu != null)
            {
                activeMenu.ParentMenu.OpenNextChildMenu(loop);
            }
        }


        /// <summary>
        /// Open the previous child menu of the currently active menu.
        /// </summary>
        /// <param name="loop">Whether to loop through the child menus.</param>
        public virtual void OpenPreviousChildMenu(bool loop = false)
        {
            if (activeMenu != null && activeMenu.ParentMenu != null)
            {
                activeMenu.ParentMenu.OpenPreviousChildMenu(loop);
            }
        }
    }
}