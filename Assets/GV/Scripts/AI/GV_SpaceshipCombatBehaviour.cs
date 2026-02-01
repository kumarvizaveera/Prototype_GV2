using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VSX.SpaceCombatKit;
using VSX.Utilities;
using VSX.Engines3D;

namespace GV.AI
{
    public class GV_SpaceshipCombatBehaviour : SpaceshipCombatBehaviour
    {
        [Header("Leash Settings")]
        
        [Tooltip("The center point of the combat zone. If null, uses the initial position.")]
        [SerializeField]
        protected Transform leashAnchor;

        [Tooltip("Maximum distance from the anchor before the ship is forced to return.")]
        [SerializeField]
        protected float leashRadius = 1000f;

        [Tooltip("Throttle/Speed when returning to the leash center.")]
        [SerializeField]
        [Range(0f, 1f)]
        protected float returnSpeedFactor = 1.0f;

        protected Vector3 anchorPosition;
        protected bool isReturning = false;

        protected new void Start()
        {
            if (leashAnchor != null)
            {
                anchorPosition = leashAnchor.position;
            }
            else
            {
                anchorPosition = transform.position;
            }
        }

        public override void StartBehaviour()
        {
            base.StartBehaviour();
            isReturning = false;
        }

        protected void ReturnToLeashUpdate()
        {
            Vector3 toAnchor = anchorPosition - vehicle.transform.position;
            
            // If we are back within a safe margin (e.g., 50% of radius), resume combat
            // This prevents rapid flickering between combat and return at the boundary
            if (toAnchor.magnitude < leashRadius * 0.5f)
            {
                isReturning = false;
                // Resume previous state or pick a new one
                if (combatState == CombatState.Attacking) StartAttack();
                else StartEvade();
                return;
            }

            // Execute Return Logic
            // Turn toward anchor
            Maneuvring.TurnToward(vehicle.transform, anchorPosition, maxRotationAngles, shipPIDController.steeringPIDController);
            engines.SetSteeringInputs(shipPIDController.steeringPIDController.GetControlValues());

            // Move forward
            engines.SetMovementInputs(new Vector3(0, 0, returnSpeedFactor));
        }

        public override bool BehaviourUpdate()
        {
            // Check Leash
            float dist = Vector3.Distance(vehicle.transform.position, anchorPosition);
            
            if (!isReturning && dist > leashRadius)
            {
                isReturning = true;
                // Stop other sub-behaviors
                attackBehaviour.StopBehaviour();
                evadeBehaviour.StopBehaviour();
            }

            if (isReturning)
            {
                ReturnToLeashUpdate();
                return true;
            }

            // Normal Combat Logic
            return base.BehaviourUpdate();
        }
    }
}
