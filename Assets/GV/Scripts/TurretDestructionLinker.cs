using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VSX.Weapons;

namespace GV
{
    /// <summary>
    /// Disables or destroys a target object (e.g., Gyro Ring) when a referenced Turret detonates.
    /// </summary>
    public class TurretDestructionLinker : MonoBehaviour
    {
        [Tooltip("The detonator of the turret to monitor.")]
        [SerializeField]
        protected Detonator turretDetonator;

        [Tooltip("The object to disable/destroy. If empty, uses this GameObject.")]
        [SerializeField]
        protected GameObject targetObject;

        [Tooltip("If true, the object is destroyed. If false, it is just deactivated.")]
        [SerializeField]
        protected bool destroyObject = false;

        protected virtual void Reset()
        {
            if (targetObject == null) targetObject = gameObject;
        }

        protected virtual void Awake()
        {
            if (targetObject == null) targetObject = gameObject;
        }

        protected virtual void OnEnable()
        {
            if (turretDetonator != null)
            {
                turretDetonator.onDetonated.AddListener(OnTurretDetonated);
            }
        }

        protected virtual void OnDisable()
        {
            if (turretDetonator != null)
            {
                turretDetonator.onDetonated.RemoveListener(OnTurretDetonated);
            }
        }

        protected virtual void OnTurretDetonated()
        {
            if (targetObject != null)
            {
                if (destroyObject)
                {
                    Destroy(targetObject);
                }
                else
                {
                    targetObject.SetActive(false);
                }
            }
        }
    }
}
