using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;


namespace VSX.VehicleCombatKits
{
    /// <summary>
    /// Player input script for controlling camera free look mode on a vehicle, using Unity's Input System.
    /// </summary>
    public class PlayerInput_InputSystem_CameraFreeLookControls : PlayerInput_Base_CameraFreeLookControls
    {
  
        protected InputDeviceType lastLookInputDeviceType = InputDeviceType.None;



        public virtual void FreeLook(InputAction.CallbackContext context)
        {
            lookInputValue = context.ReadValue<Vector2>();

            if (context.action.activeControl.device is Mouse)
            {
                lastLookInputDeviceType = InputDeviceType.Mouse;
            }
            else if (context.action.activeControl.device is Gamepad)
            {
                lastLookInputDeviceType = InputDeviceType.Gamepad;
            }
            else if (context.action.activeControl.device is Keyboard)
            {
                lastLookInputDeviceType = InputDeviceType.Keyboard;
            }
            else if (context.action.activeControl.device is Joystick)
            {
                lastLookInputDeviceType = InputDeviceType.Joystick;
            }
        }


        protected override InputDeviceType GetLookInputDeviceType()
        {
            return lastLookInputDeviceType;
        }


        public virtual void FreeLookMode(InputAction.CallbackContext context)
        {
            if (context.started)
            {
                EnterFreeLookMode();
            }
            else if (context.canceled)
            {
                ExitFreeLookMode();
            }
        }
    }
}