using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;


namespace VSX.VehicleCombatKits
{
    /// <summary>
    /// Player input script for interacting with a generic menu (e.g. pause menu), using Unity's Input System.
    /// </summary>
    public class PlayerInput_InputSystem_MenuControls : PlayerInput_Base_MenuControls
    {
        public virtual void ToggleMenu(InputAction.CallbackContext context)
        {
            if (!CanRunInput()) return;

            if (context.performed)
            {
                ToggleMenu();
            }
        }


        public virtual void OpenMenu(InputAction.CallbackContext context)
        {
            if (!CanRunInput()) return;

            if (context.performed)
            {
                OpenMenu();
            }
        }


        public virtual void Back(InputAction.CallbackContext context)
        {
            if (!CanRunInput()) return;

            if (context.performed)
            {
                Back();
            }
        }
    }
}
