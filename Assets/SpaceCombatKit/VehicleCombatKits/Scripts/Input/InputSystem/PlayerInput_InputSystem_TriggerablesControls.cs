using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;


namespace VSX.VehicleCombatKits
{
    /// <summary>
    /// Player input script for controlling triggerables (e.g. weapons) on a vehicle, using Unity's Input System.
    /// </summary>
    public class PlayerInput_InputSystem_TriggerablesControls : PlayerInput_Base_TriggerablesControls
    {

     
        public virtual void FirePrimary(InputAction.CallbackContext context)
        {
            if (!CanRunInput()) return;
            if (context.started)
            {
                StartFiring(primaryWeaponTriggerIndex);
            }
            else if (context.canceled)
            {
                StopFiring(primaryWeaponTriggerIndex);
            }
        }


        public virtual void FireSecondary(InputAction.CallbackContext context)
        {
            if (!CanRunInput()) return;

            if (context.started)
            {
                StartFiring(secondaryWeaponTriggerIndex);
            }
            else if (context.canceled)
            {
                StopFiring(secondaryWeaponTriggerIndex);
            }
        }
    }
}