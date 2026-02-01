using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VSX.SpaceCombatKit;

namespace GV.AI
{
    public class GV_SpaceshipEvadeBehaviour : SpaceshipEvadeBehaviour
    {
        [Header("Movement Settings")]
        [Tooltip("Multiplier for the ship's throttle/speed during evasion (0-1).")]
        [SerializeField]
        [Range(0f, 1f)]
        protected float evadeSpeedFactor = 1.0f;

        public override bool BehaviourUpdate()
        {
            // Run base logic
            if (!base.BehaviourUpdate()) return false;

            // Base class sets movement inputs to (0, 0, 1) [Full Throttle]
            // We override this with our factor
            engines.SetMovementInputs(new Vector3(0, 0, evadeSpeedFactor));

            return true;
        }
    }
}
