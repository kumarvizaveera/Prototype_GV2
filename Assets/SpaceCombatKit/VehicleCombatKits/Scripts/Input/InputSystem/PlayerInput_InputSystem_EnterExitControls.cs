using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.InputSystem;


namespace VSX.VehicleCombatKits
{

    /// <summary>
    /// Player input script for controlling characters entering and exiting vehicles, using Unity's Input System.
    /// </summary>
    public class PlayerInput_InputSystem_EnterExitControls : PlayerInput_Base_EnterExitControls
    {
        public InputActionReference enterExitAction;


        public virtual void EnterExit (InputAction.CallbackContext context)
        {
            if (context.started)
            {
                EnterExit();
            }
        }


        // Get the string to display the input on the UI.
        protected override string GetControlDisplayString()
        {
            return enterExitAction.action.GetBindingDisplayString();
        }
    }
}