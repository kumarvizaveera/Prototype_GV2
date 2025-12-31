using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VSX.Controls;
using UnityEngine.Events;
using VSX.UI;

namespace VSX.VehicleCombatKits
{
    /// <summary>
    /// Base class for a player input script that interacts with a generic menu (e.g. pause menu).
    /// </summary>
    public class PlayerInput_Base_MenuControls : GeneralInput
    {

        [Tooltip("The menu to control.")]
        [SerializeField]
        protected MenuGroup menuController;


        // Unity Event called when Back is pressed
        public UnityEvent onBackPressed;


        protected bool menuOpenedThisFrame = false;



        protected override void Reset()
        {
            base.Reset();

            menuController = GetComponentInChildren<MenuGroup>();
        }


        protected override void Awake()
        {
            base.Awake();

            if (menuController == null)
            {
                menuController = FindAnyObjectByType<MenuGroup>();
            }

            if (menuController != null) menuController.onOpened.AddListener(OnMenuOpened);
        }


        protected virtual void ToggleMenu()
        {
            if (menuController == null) return;

            if (menuController.IsOpen)
            {
                menuController.Close();
            }
            else
            {
                menuController.Open();
            }
        }


        protected virtual void OpenMenu()
        {
            if (menuController == null) return;

            if (menuController.IsOpen) return;

            menuController.Open();
        }


        protected virtual void Back()
        {
            if (menuController == null) return;

            if (!menuController.IsOpen) return;

            if (menuOpenedThisFrame) return;

            if (menuController.ActiveMenu == null) return;

            menuController.ActiveMenu.Exit();

            onBackPressed.Invoke();
        }


        protected virtual void OnMenuOpened()
        {
            menuOpenedThisFrame = true;

            StartCoroutine(ResetFrameParameters());
        }


        protected virtual IEnumerator ResetFrameParameters()
        {
            yield return new WaitForEndOfFrame();

            menuOpenedThisFrame = false;
        }
    }
}
