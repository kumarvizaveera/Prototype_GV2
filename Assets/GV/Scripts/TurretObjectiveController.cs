using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VSX.Objectives;
using VSX.Health;

namespace VSX.VehicleCombatKits
{
    /// <summary>
    /// Manages a Destroy type objective that automatically finds all turrets under a parent.
    /// </summary>
    public class TurretObjectiveController : DestroyObjectiveController
    {
        [Tooltip("The parent object containing all the turrets.")]
        [SerializeField]
        protected Transform turretParent;

        protected override void Awake()
        {
            if (turretParent != null)
            {
                // Find all Damageable components in the children of the turret parent
                Damageable[] turretDamageables = turretParent.GetComponentsInChildren<Damageable>();
                
                // Add them to the targets list
                if (targets == null) targets = new List<Damageable>();
                
                foreach(Damageable turret in turretDamageables)
                {
                    if (turret != null && !targets.Contains(turret))
                    {
                        targets.Add(turret);
                    }
                }
            }
            else
            {
                Debug.LogWarning("TurretObjectiveController: Turret Parent not assigned!");
            }

            // Call base Awake to set up listeners on the targets
            base.Awake();
        }
    }
}
