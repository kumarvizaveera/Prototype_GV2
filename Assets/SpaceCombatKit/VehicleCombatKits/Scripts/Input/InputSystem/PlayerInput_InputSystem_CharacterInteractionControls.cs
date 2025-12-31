using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using VSX.VehicleCombatKits;


namespace VSX.Characters
{
    /// <summary>
    /// Player input script for controlling character interactions, using Unity's Input System.
    /// </summary>
    public class PlayerInput_InputSystem_CharacterInteractionControls : PlayerInput_Base_CharacterInteractionControls
    {
        public InputActionReference interactAction;


        public virtual void Interact (InputAction.CallbackContext context)
        {
            if (context.started)
            {
                Interact();
            }
        }

        // Get the string to display the input on the UI.
        protected override string GetControlDisplayString()
        {
            return interactAction.action.GetBindingDisplayString();
        }
    }
}

