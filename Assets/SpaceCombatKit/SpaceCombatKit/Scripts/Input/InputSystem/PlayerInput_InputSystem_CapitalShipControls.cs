using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using VSX.Controls;
using VSX.VehicleCombatKits;


namespace VSX.SpaceCombatKit
{

    /// <summary>
    /// Player input script for controlling a capital ship, using Unity's Input System.
    /// </summary>
    public class PlayerInput_InputSystem_CapitalShipControls : PlayerInput_Base_CapitalShipControls
    {
        protected InputDeviceType lastLookInputDeviceType = InputDeviceType.None;


        public virtual void Look(InputAction.CallbackContext context)
        {
            if (!CanRunInput()) return;

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


        public virtual void Steer(InputAction.CallbackContext context)
        {
            if (!CanRunInput()) return;

            steeringInputValue.x = context.ReadValue<Vector2>().y;
            steeringInputValue.y = context.ReadValue<Vector2>().x;
        }


        public virtual void Strafe(InputAction.CallbackContext context)
        {
            if (!CanRunInput()) return;

            movementInputValue.x = context.ReadValue<Vector2>().x;
            movementInputValue.y = context.ReadValue<Vector2>().y;
        }


        public virtual void Throttle(InputAction.CallbackContext context)
        {
            if (!CanRunInput()) return;

            movementInputValue.z = context.ReadValue<float>();
        }


        public virtual void Boost(InputAction.CallbackContext context)
        {
            if (!CanRunInput()) return;

            boostInputValue.z = context.ReadValue<float>();
        }


        public virtual void Zoom(InputAction.CallbackContext context)
        {
            if (!CanRunInput()) return;

            zoomInputValue = context.ReadValue<float>();
        }


        protected override InputDeviceType GetLookInputDeviceType()
        {
            return lastLookInputDeviceType;
        }


        
    }
}
