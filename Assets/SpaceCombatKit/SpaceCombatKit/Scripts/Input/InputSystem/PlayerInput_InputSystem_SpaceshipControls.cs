using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using VSX.VehicleCombatKits;

namespace VSX.SpaceCombatKit
{
    /// <summary>
    /// Player input script for controlling a spacefighter style ship, using Unity's Input System.
    /// </summary>
    public class PlayerInput_InputSystem_SpaceshipControls : PlayerInput_Base_SpaceshipControls
    {

        protected InputDeviceType lastSteeringInputDeviceType = InputDeviceType.None;


        protected override void Awake()
        {
            base.Awake();

            InputSystem.onDeviceChange += OnDeviceChange;

        }


        public virtual void Steer(InputAction.CallbackContext context)
        {
            if (!CanRunInput() || !steeringEnabled) return;

            steeringInputs.x = context.ReadValue<Vector2>().y;
            steeringInputs.y = context.ReadValue<Vector2>().x;

            if (context.action.activeControl.device is Mouse)
            {
                lastSteeringInputDeviceType = InputDeviceType.Mouse;
            }
            else if (context.action.activeControl.device is Gamepad)
            {
                lastSteeringInputDeviceType = InputDeviceType.Gamepad;
            }
            else if (context.action.activeControl.device is Keyboard)
            {
                lastSteeringInputDeviceType = InputDeviceType.Keyboard;
            }
            else if (context.action.activeControl.device is Joystick)
            {
                lastSteeringInputDeviceType = InputDeviceType.Joystick;
            }
        }

        
        protected override InputDeviceType GetSteeringInputDeviceType()
        {
            return lastSteeringInputDeviceType;
        }


        public virtual void Strafe(InputAction.CallbackContext context)
        {
            if (!CanRunInput() || !movementEnabled) return;

            movementInputs.x = context.ReadValue<Vector2>().x;
            movementInputs.y = context.ReadValue<Vector2>().y;
        }


        public virtual void Throttle(InputAction.CallbackContext context)
        {
            if (!CanRunInput() || !movementEnabled) return;
            movementInputs.z = context.ReadValue<float>();
        }


        public virtual void Boost(InputAction.CallbackContext context)
        {
            if (!CanRunInput() || !movementEnabled) return;

            boostInputs.z = context.ReadValue<float>();
        }


        public virtual void Roll(InputAction.CallbackContext context)
        {
            if (!CanRunInput() || !steeringEnabled) return;

            steeringInputs.z = context.ReadValue<float>();

            if (context.action.activeControl.device is Mouse)
            {
                lastSteeringInputDeviceType = InputDeviceType.Mouse;
            }
            else if (context.action.activeControl.device is Gamepad)
            {
                lastSteeringInputDeviceType = InputDeviceType.Gamepad;
            }
            else if (context.action.activeControl.device is Joystick)
            {
                lastSteeringInputDeviceType = InputDeviceType.Joystick;
            }
        }


        protected virtual void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (Mouse.current == null || !mouseEnabled || Gamepad.current != null)
            {
                if (controlHUDCursor && hudCursor != null) hudCursor.CenterCursor();
                reticleViewportPosition = new Vector3(0.5f, 0.5f, 0);
            }
        }


        protected override Vector3 GetMouseViewportPosition()
        {
            return new Vector3(Mouse.current.position.ReadValue().x / Screen.width, Mouse.current.position.ReadValue().y / Screen.height, 0);
        }


        protected override void OnInputUpdate()
        {
            // --- Custom Overrides ---

            // Auto-Forward Logic with Brake Override
            if (Keyboard.current != null && Keyboard.current.sKey.isPressed)
            {
                 // S is pressed: Brake / Reverse
                 movementInputs.z = -1f;
            }
            else
            {
                 // S not pressed: Auto-Forward
                 movementInputs.z = 1f;
            }
            
            setThrottle = true; 

            // 2. W = Boost: Manually check the W key on the current keyboard
            if (Keyboard.current != null)
            {
                boostInputs.z = Keyboard.current.wKey.isPressed ? 1f : 0f;
            }

            // 3. Call base to handle steering/mouse/applying values to engines
            base.OnInputUpdate();
        }
    }
}