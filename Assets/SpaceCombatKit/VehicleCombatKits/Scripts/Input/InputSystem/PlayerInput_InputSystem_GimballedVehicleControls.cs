using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.InputSystem;


namespace VSX.VehicleCombatKits
{

    /// <summary>
    /// Player input script for controlling a vehicle with a main gimbal (e.g. turret), using Unity's Input System.
    /// </summary>
    public class PlayerInput_InputSystem_GimballedVehicleControls : PlayerInput_Base_GimballedVehicleControls
    {

        protected InputDeviceType lastLookInputDeviceType = InputDeviceType.None;


        protected virtual void Look(InputAction.CallbackContext context)
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
    }
}