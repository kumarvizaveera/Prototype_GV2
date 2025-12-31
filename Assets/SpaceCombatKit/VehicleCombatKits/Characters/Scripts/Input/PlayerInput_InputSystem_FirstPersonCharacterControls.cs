using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.InputSystem;
using VSX.VehicleCombatKits;

namespace VSX.Characters
{

    /// <summary>
    /// Player input script for controlling a first person character, using Unity's Input System.
    /// </summary>
    public class PlayerInput_InputSystem_FirstPersonCharacterControls : PlayerInput_Base_FirstPersonCharacterControls
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


        protected override InputDeviceType GetLookInputDeviceType()
        {
            return lastLookInputDeviceType;
        }


        public virtual void Move(InputAction.CallbackContext context)
        {
            if (!CanRunInput()) return;

            movementInputValue.x = context.ReadValue<Vector2>().x;
            movementInputValue.z = context.ReadValue<Vector2>().y;
        }


        public virtual void Jump(InputAction.CallbackContext context)
        {
            if (!CanRunInput()) return;

            if (context.started)
            {
                Jump();
            }
        }


        public virtual void Run(InputAction.CallbackContext context)
        {
            if (!CanRunInput()) return;

            if (context.started)
            {
                StartRunning();
            } 
            else if (context.canceled)
            {
                StopRunning();
            }
        }
    }
}