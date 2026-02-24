using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VSX.RadarSystem;

namespace VSX.Weapons
{
    /// <summary>
    /// Base class for an independent weapon controller with its own control logic (e.g. a turret).
    /// </summary>
    public class WeaponController : MonoBehaviour
    {

        [Tooltip("The weapon that this weapon controller controls.")]
        [SerializeField]
        protected Weapon weapon;
        public virtual Weapon Weapon { get => weapon; }


        /// <summary>
        /// Set the target of this weapon controller.
        /// </summary>
        /// <param name="target">The new target.</param>
        public virtual void SetTarget(Trackable target) { }


        protected bool activated = true;
        /// <summary>
        /// Set or read the activation state of the weapon controller.
        /// </summary>
        public virtual bool Activated
        {
            get { return activated; }
            set
            {
                if (value != activated)
                {
                    Debug.Log($"[WC-DBG] {gameObject.name} — Activated changing: {activated} → {value} (caller={new System.Diagnostics.StackTrace().GetFrame(1)?.GetMethod()?.Name ?? "unknown"})");
                }

                if (value && !activated)
                {
                    OnActivated();
                }
                else if (!value && activated)
                {
                    OnDeactivated();
                }
            }
        }


        // Called when the weapon controller is activated.
        protected virtual void OnActivated() { }


        // Called when the weapon controller is deactivated.
        protected virtual void OnDeactivated() 
        {
            if (weapon != null) weapon.Triggerable.StopTriggering();
        }
    }
}
