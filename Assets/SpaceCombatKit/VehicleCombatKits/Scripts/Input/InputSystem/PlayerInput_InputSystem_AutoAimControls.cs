using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;


namespace VSX.VehicleCombatKits
{
    /// <summary>
    /// Player input script for controlling a vehicle's auto aim functionality, using Unity's Input System.
    /// </summary>
    public class PlayerInput_InputSystem_AutoAimControls : PlayerInput_Base_AutoAimControls
    {
        public virtual void ToggleAutoAim(InputAction.CallbackContext context)
        {
            if (!CanRunInput()) return;

            if (context.performed)
            {
                ToggleAutoAim();
            }
        }
    }
}

