using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;


namespace VSX.VehicleCombatKits
{
    /// <summary>
    /// Player input script for controlling a vehicle's radar functionality, using Unity's Input System.
    /// </summary>
    public class PlayerInput_InputSystem_RadarControls : PlayerInput_Base_RadarControls
    {

        public virtual void TargetNext(InputAction.CallbackContext context)
        {
            if (context.started)
            {
                TargetNext();
            }
        }


        public virtual void TargetPrevious(InputAction.CallbackContext context)
        {
            if (context.started)
            {
                TargetPrevious();
            }
        }


        public virtual void TargetNearest(InputAction.CallbackContext context)
        {
            if (context.started)
            {
                TargetNearest();
            }
        }


        public virtual void TargetFront(InputAction.CallbackContext context)
        {
            if (context.started)
            {
                TargetFront();
            }
        }


        public virtual void TargetUnderCursor(InputAction.CallbackContext context)
        {
            if (context.started)
            {
                TargetUnderCursor();
            }
        }
    }
}

