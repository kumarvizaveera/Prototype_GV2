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

        [Header("Custom Settings")]
        public bool enableAutoForward = true;
        
        [Tooltip("When enabled, automatically uses boost when boost fuel is available. Falls back to normal speed when boost is depleted.")]
        public bool enableAutoBoost = false;

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
            if (enableAutoForward)
            {
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

                // Auto-Boost Logic: Automatically boost when fuel is available
                if (enableAutoBoost)
                {
                    // Check if all boost resource handlers are ready (have fuel)
                    bool canBoost = true;
                    if (engines != null && engines.BoostResourceHandlers != null && engines.BoostResourceHandlers.Count > 0)
                    {
                        for (int i = 0; i < engines.BoostResourceHandlers.Count; ++i)
                        {
                            if (!engines.BoostResourceHandlers[i].Ready())
                            {
                                canBoost = false;
                                break;
                            }
                        }
                    }
                    
                    // Apply boost if resources available, otherwise fall back to normal speed
                    boostInputs.z = canBoost ? 1f : 0f;
                }
                else
                {
                    // Manual boost with W key (original behavior)
                    if (Keyboard.current != null)
                    {
                        boostInputs.z = Keyboard.current.wKey.isPressed ? 1f : 0f;
                    }
                }
            }

            // Keyboard overrides for Q/E and Arrow Keys
            // Bypass Input Actions for critical steering to prevent Mouse/Keyboard conflicts
            if (Keyboard.current != null)
            {
                bool keyboardSteering = false;

                // Q/E for Roll + Yaw
                if (Keyboard.current.qKey.isPressed) { steeringInputs.z = 1f; steeringInputs.y = -1f; keyboardSteering = true; }
                else if (Keyboard.current.eKey.isPressed) { steeringInputs.z = -1f; steeringInputs.y = 1f; keyboardSteering = true; }
                else if (lastSteeringInputDeviceType == InputDeviceType.Keyboard)
                {
                    steeringInputs.z = 0f;
                }

                if (!keyboardSteering)
                {
                    // Right/Left arrows for Yaw
                    if (Keyboard.current.rightArrowKey.isPressed) { steeringInputs.y = 1f; keyboardSteering = true; }
                    else if (Keyboard.current.leftArrowKey.isPressed) { steeringInputs.y = -1f; keyboardSteering = true; }
                    else if (lastSteeringInputDeviceType == InputDeviceType.Keyboard) { steeringInputs.y = 0f; }
                }

                // Up/Down arrows for Pitch (SCK inverted: -1 is up)
                if (Keyboard.current.upArrowKey.isPressed) { steeringInputs.x = -1f; keyboardSteering = true; }
                else if (Keyboard.current.downArrowKey.isPressed) { steeringInputs.x = 1f; keyboardSteering = true; }
                else if (lastSteeringInputDeviceType == InputDeviceType.Keyboard) { steeringInputs.x = 0f; }

                if (keyboardSteering)
                {
                    lastSteeringInputDeviceType = InputDeviceType.Keyboard;
                }
            }

            // 3. Call base to handle steering/mouse/applying values to engines
            base.OnInputUpdate();
        }
    }
}