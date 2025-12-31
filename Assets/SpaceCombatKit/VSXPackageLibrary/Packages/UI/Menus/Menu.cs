using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace VSX.UI
{
    /// <summary>
    /// Represents a single menu in the scene.
    /// </summary>
    public class Menu : MonoBehaviour
    {
        [Tooltip("Whether to open the menu when the scene starts.")]
        [SerializeField]
        protected bool openOnStart;

        [Tooltip("Whether this menu can be exited (e.g. disable for a main menu).")]
        [SerializeField]
        protected bool exitable = true;
        public virtual bool Exitable { get => exitable; }

        [Tooltip("The object to activate when this menu is opened, and to deactivate when this menu is closed.")]
        [SerializeField]
        protected GameObject handle;

        [Tooltip("The gameobject to parent menu items to when they are added to the menu.")]
        [SerializeField]
        protected Transform menuItemsParent;

        [Tooltip("The gameobject (e.g. button) that the event system should select when this menu is opened.")]
        [SerializeField]
        protected GameObject firstSelectedUIObject;
        public GameObject FirstSelectedUIObject
        {
            get { return firstSelectedUIObject; }
            set { firstSelectedUIObject = value; }
        }

        [Tooltip("The menu to open when this menu is exited.")]
        [SerializeField]
        protected Menu exitMenu;      

        [Tooltip("The toggle that opens this menu (as a child menu of another one).")]
        [SerializeField]
        protected Toggle toggle;

        [Tooltip("The index of the child menu to open when this menu is opened. Ignored if this menu has no child menus.")]
        [SerializeField]
        protected int startingChildMenuIndex = 0;

        [Tooltip("The component that displays information about the currently selected menu item.")]
        [SerializeField]
        protected MenuItemInfoController menuItemInfo;

        [Tooltip("Unity Event called when this menu is opened.")]
        public UnityEvent onOpened;

        [Tooltip("Unity Event called when this menu is closed.")]
        public UnityEvent onClosed;

        [Tooltip("Unity Event called when this menu is exited.")]
        public UnityAction onExited;

        [Tooltip("Unity Action called when an item is selected on the menu.")]
        public UnityAction<MenuItem> onMenuItemSelected;

        // The parent menu.
        protected Menu parentMenu;
        public Menu ParentMenu { get => parentMenu; }

        // The child menus of this menu.
        protected List<Menu> childMenus = new List<Menu>();
        protected int activeChildMenuIndex = -1;
        public Menu ActiveChildMenu { get => activeChildMenuIndex == -1 ? null : childMenus[activeChildMenuIndex]; }

        protected bool isOpen; 
        public bool IsOpen { get => isOpen; }

        protected List<MenuItem> menuItems = new List<MenuItem>();


        protected MenuItem lastSelectedMenuItem;
        public virtual MenuItem LastSelectedMenuItem { get => lastSelectedMenuItem; }


        protected MenuItem selectedMenuItem;
        public virtual MenuItem SelectedMenuItem { get => selectedMenuItem; }


        protected GameObject lastSelectedEventSystemObject;



        protected virtual void Awake()
        {
            Menu[] menusInHierarchy = GetComponentsInChildren<Menu>(true);
            
            foreach (Menu menu in menusInHierarchy)
            {
                if (menu == this) continue;

                menu.SetParentMenu(this);
                menu.onOpened.AddListener(() => { OnChildMenuOpened(menu); });
                menu.onClosed.AddListener(() => { OnChildMenuClosed(menu); });

                childMenus.Add(menu);
            }

            if (!isOpen) handle.SetActive(false);

            MenuItem[] menuItemsInHierarchy = GetComponentsInChildren<MenuItem>(true);
            foreach(MenuItem item in menuItemsInHierarchy)
            {
                bool ignore = false;
                foreach(Menu childMenu in childMenus)
                {
                    if (item.transform.IsChildOf(childMenu.transform))
                    {
                        ignore = true;
                        break;
                    }
                }

                if (ignore) continue;

                OnMenuItemAdded(item);
            }
        }


        protected virtual void Start()
        {
            if (openOnStart)
            {
                Open();
            }
        }


        /// <summary>
        /// Open the menu.
        /// </summary>
        public virtual void Open()
        {
            if (isOpen) return;

            isOpen = true;

            handle.SetActive(true);

            if (toggle != null)
            {
                toggle.isOn = true;
            }

            SetSelectedUIObject(firstSelectedUIObject);

            onOpened.Invoke();

            if (startingChildMenuIndex >= 0 && childMenus.Count > startingChildMenuIndex)
            {
                OpenChildMenu(startingChildMenuIndex);
            }

            UpdateUI();
        }

 
        /// <summary>
        /// Close the menu.
        /// </summary>
        public virtual void Close()
        {            
            if (!isOpen) return;

            handle.SetActive(false);

            isOpen = false;

            if (childMenus.Count > 0)
            {
                CloseActiveChildMenu();
            }

            foreach(MenuItem menuItem in menuItems)
            {
                menuItem.SetSelected(false);
            }

            onClosed.Invoke();
        }


        /// <summary>
        /// Exit the menu.
        /// </summary>
        public virtual void Exit()
        {
            if (!exitable) return;

            if (parentMenu == null)
            {
                Close();

                if (exitMenu != null)
                {
                    exitMenu.Open();
                }
            }
            else
            {
                parentMenu.Exit();
            }

            onExited?.Invoke();
        }


        /// <summary>
        /// Perform the menu's Cancel action.
        /// </summary>
        public virtual void CancelAction()
        {
            if (parentMenu != null)
            {
                parentMenu.CancelAction();
            }
            else
            {
                Exit();
            }  
        }


        /// <summary>
        /// Called when a menu item is added to the menu.
        /// </summary>
        /// <param name="menuItem">The added menu item.</param>
        protected virtual void OnMenuItemAdded(MenuItem menuItem)
        {
            if (menuItems.IndexOf(menuItem) == -1)
            {
                menuItems.Add(menuItem);
            }

            if (firstSelectedUIObject == null && menuItem.Selectable != null)
            {
                firstSelectedUIObject = menuItem.Selectable.gameObject;
            }

            menuItem.onSelected.AddListener(() => { OnMenuItemSelected(menuItem); });
            menuItem.onDeselected.AddListener(() => { OnMenuItemDeselected(menuItem); });

        }


        /// <summary>
        /// Called by the Menu Group that owns this menu when any of its menus are opened.
        /// </summary>
        /// <param name="menu">The menu that was opened.</param>
        public virtual void OnMenuGroupMenuOpened(Menu menu)
        {
            if (menu == this) return;

            if (IsParentMenuOf(menu)) return;

            Close();
        }


        /// <summary>
        /// Set the parent menu of this menu.
        /// </summary>
        /// <param name="parentMenu">The parent menu.</param>
        public virtual void SetParentMenu(Menu parentMenu)
        {
            this.parentMenu = parentMenu;
        }


        /// <summary>
        /// Whether this menu is a parent of (or deep parent of) another menu.
        /// </summary>
        /// <param name="other">The other menu to check against.</param>
        /// <returns>Whether this menu is a parent of (or deep parent of) the other menu.</returns>
        public virtual bool IsParentMenuOf(Menu other)
        {
            if (other == this) return false;

            Menu checkMenu = other;
            while (true)
            {
                if (checkMenu.ParentMenu == null)
                {
                    return false;
                }
                else
                {
                    if (checkMenu.ParentMenu == this)
                    {
                        return true;
                    }
                    else
                    {
                        checkMenu = checkMenu.ParentMenu;
                    }
                }
            }
        }


        /// <summary>
        /// Open a child menu by index.
        /// </summary>
        /// <param name="childMenuIndex">The index of the child menu to open.</param>
        protected virtual void OpenChildMenu(int childMenuIndex)
        {
            if (childMenuIndex != -1 && childMenuIndex < childMenus.Count)
            {
                OpenChildMenu(childMenus[childMenuIndex]);
            }
        }


        /// <summary>
        /// Open a child menu by reference.
        /// </summary>
        /// <param name="childMenu">The child menu to open.</param>
        public virtual void OpenChildMenu(Menu childMenu)
        {
            if (childMenus.IndexOf(childMenu) == -1) return;

            childMenu.Open();
        }


        /// <summary>
        /// Close the active child menu.
        /// </summary>
        protected virtual void CloseActiveChildMenu()
        {
            if (activeChildMenuIndex != -1)
            {
                childMenus[activeChildMenuIndex].Close();
            }
        }


        /// <summary>
        /// Open the next child menu.
        /// </summary>
        /// <param name="loop">Whether to loop through the child menus.</param>
        public virtual void OpenNextChildMenu(bool loop = false)
        {
            if (childMenus.Count == 0) return;

            int nextChildMenu = activeChildMenuIndex + 1;
            if (nextChildMenu == childMenus.Count)
            {
                if (loop)
                {
                    nextChildMenu = 0;
                }
                else
                {
                    return;
                }
            }

            OpenChildMenu(nextChildMenu);
        }


        /// <summary>
        /// Open the previous child menu.
        /// </summary>
        /// <param name="loop">Whether to loop through the child menus.</param>
        public virtual void OpenPreviousChildMenu(bool loop = false)
        {
            if (childMenus.Count == 0) return;

            int nextChildMenu = activeChildMenuIndex - 1;
            if (nextChildMenu == -1)
            {
                if (loop)
                {
                    nextChildMenu = childMenus.Count - 1;
                }
                else
                {
                    return;
                }
            }

            OpenChildMenu(nextChildMenu);
        }


        /// <summary>
        /// Called when a child menu is opened.
        /// </summary>
        /// <param name="childMenu">The child menu that was opened.</param>
        protected virtual void OnChildMenuOpened(Menu childMenu)
        {
            activeChildMenuIndex = childMenus.IndexOf(childMenu);
        }


        /// <summary>
        /// Called when a child menu is closed.
        /// </summary>
        /// <param name="childMenu">The child menu that was closed.</param>
        protected virtual void OnChildMenuClosed(Menu childMenu)
        {
            if (childMenus.IndexOf(childMenu) == activeChildMenuIndex)
            {
                activeChildMenuIndex = -1;
            }
        }


        /// <summary>
        /// Called when a menu item under this menu is selected on the UI.
        /// </summary>
        /// <param name="menuItem">The menu item.</param>
        protected virtual void OnMenuItemSelected(MenuItem menuItem)
        {
            if (menuItem != null)
            {
                if (menuItemInfo != null)
                {
                    menuItemInfo.Show(menuItem);
                }
            }
            else
            {
                if (menuItemInfo != null)
                {
                    menuItemInfo.Close();
                }
            }

            OnSelectedMenuItemChanged(menuItem);

            onMenuItemSelected?.Invoke(menuItem);
        }


        /// <summary>
        /// Called when a menu item under this menu is deselected on the UI.
        /// </summary>
        /// <param name="menuItem">The menu item.</param>
        protected virtual void OnMenuItemDeselected(MenuItem menuItem)
        {
            if (menuItem == selectedMenuItem)
            {
                OnMenuItemSelected(null);
            }
        }


        /// <summary>
        /// Called when the selected menu item under this menu changes.
        /// </summary>
        /// <param name="newSelectedMenuItem">The new selected menu item.</param>
        protected virtual void OnSelectedMenuItemChanged(MenuItem newSelectedMenuItem)
        {
            if (newSelectedMenuItem != null)
            {
                foreach(MenuItem item in menuItems)
                {
                    if (item != newSelectedMenuItem)
                    {
                        item.SetSelected(false);
                    }
                }

                lastSelectedMenuItem = newSelectedMenuItem;
            }

            selectedMenuItem = newSelectedMenuItem;

            UpdateUI();
        }


        /// <summary>
        /// Set the selected UI object.
        /// </summary>
        /// <param name="m_Object">The object to set selected.</param>
        protected virtual void SetSelectedUIObject(GameObject m_Object)
        {
            if (EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(m_Object);
            }
        }


        /// <summary>
        /// Update the menu UI (e.g. when the user changes something.
        /// </summary>
        protected virtual void UpdateUI() { }


        protected virtual void OnEventSystemSelectedGameObjectChanged()
        {
            // Keep the last selected menu item always selected.
            if (EventSystem.current.currentSelectedGameObject == null)
            {
                if (lastSelectedMenuItem == null)
                {
                    EventSystem.current.SetSelectedGameObject(null);
                }
                else
                {
                    if (lastSelectedMenuItem.gameObject.activeInHierarchy)
                    {
                        EventSystem.current.SetSelectedGameObject(lastSelectedMenuItem.gameObject);
                    }
                }  
            }
        }


        protected virtual void LateUpdate()
        {
            if (EventSystem.current != null)
            {
                if (EventSystem.current.currentSelectedGameObject != lastSelectedEventSystemObject)
                {
                    lastSelectedEventSystemObject = EventSystem.current.currentSelectedGameObject;
                    OnEventSystemSelectedGameObjectChanged();
                }
            }
        }
    }
}

