using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VSX.SpaceCombatKit;

namespace GV.AI
{
    public class GV_SpaceshipAttackBehaviour : SpaceshipAttackBehaviour
    {
        [Header("Movement Settings")]
        [Tooltip("Multiplier for the ship's throttle/speed during attacks (0-1).")]
        [SerializeField]
        [Range(0f, 1f)]
        protected float attackSpeedFactor = 0.8f;

        public override bool BehaviourUpdate()
        {
            // Run the base physics/steering logic
            if (!base.BehaviourUpdate()) return false;

            // Get the current control inputs that the base class set
            Vector3 currentMovementInputs = shipPIDController.movementPIDController.GetControlValues();
            
            // Apply our speed factor to the Z component (throttle)
            // The base class sets inputs as new Vector3(0, 0, movementInputs.z)
            // We'll just overwrite it with our scaled value.
            // Note: shipPIDController stores the *calculated* PID values. 
            // The actual values sent to the engine are what matters.
            
            // Re-apply the inputs with the scaler
            engines.SetMovementInputs(new Vector3(0, 0, currentMovementInputs.z * attackSpeedFactor));

            return true;
        }
    }
}
