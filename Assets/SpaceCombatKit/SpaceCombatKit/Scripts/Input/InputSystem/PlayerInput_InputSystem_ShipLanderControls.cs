using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.InputSystem;


namespace VSX.SpaceCombatKit
{

    /// <summary>
    /// Player input script for controlling takeoff/landing of a vehicle, using Unity's Input System.
    /// </summary>
    public class PlayerInput_InputSystem_ShipLanderControls : PlayerInput_Base_ShipLanderControls
    {
        public InputActionReference launchLandAction;

        public virtual void LaunchLand(InputAction.CallbackContext context)
        {
            if (context.started)
            {
                LaunchLand();
            }
        }

        // Get string to display the input on the UI.
        protected override string GetControlDisplayString()
        {
            return launchLandAction.action.GetBindingDisplayString();
        }
    }
}
