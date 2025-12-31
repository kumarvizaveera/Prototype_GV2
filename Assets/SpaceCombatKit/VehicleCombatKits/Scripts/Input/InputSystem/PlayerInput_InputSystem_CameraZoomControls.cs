using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace VSX.VehicleCombatKits
{
    /// <summary>
    /// Player input script for controlling camera zoom controls on a vehicle, using Unity's Input System.
    /// </summary>
    public class PlayerInput_InputSystem_CameraZoomControls : PlayerInput_Base_CameraZoomControls
    {
        protected virtual void Zoom(InputAction.CallbackContext context)
        {
            if (!CanRunInput()) return;

            zoomInputValue = context.ReadValue<float>();
        }
    }
}

