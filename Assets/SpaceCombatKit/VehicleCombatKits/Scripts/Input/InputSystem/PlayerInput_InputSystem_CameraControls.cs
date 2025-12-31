using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;


namespace VSX.VehicleCombatKits
{
    /// <summary>
    /// Player input script for controlling camera functionality on a vehicle, using Unity's Input System.
    /// </summary>
    public class PlayerInput_InputSystem_CameraControls : PlayerInput_Base_CameraControls
    {
    
        public virtual void NextCameraView(InputAction.CallbackContext context)
        {
            if (context.started)
            {
                CycleCameraView(true);
            }
        }


        public virtual void PreviousCameraView(InputAction.CallbackContext context)
        {
            if (context.performed)
            {
                CycleCameraView(false);
            }
        }
    }
}