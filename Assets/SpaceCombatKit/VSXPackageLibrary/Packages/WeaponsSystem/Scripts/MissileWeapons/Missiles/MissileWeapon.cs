using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VSX.RadarSystem;

namespace VSX.Weapons
{
    /// <summary>
    /// Base class for a missile weapon
    /// </summary>
    public class MissileWeapon : Weapon
    {
        [SerializeField]
        protected TargetLocker targetLocker;
        public TargetLocker TargetLocker { get { return targetLocker; } }

        public virtual float Agility
        {
            get { return 0; }
        }

        protected override void Reset()
        {

            base.Reset();

            // Get/add a Target Locker component
            targetLocker = GetComponentInChildren<TargetLocker>();
            if (targetLocker == null)
            {
                targetLocker = gameObject.AddComponent<TargetLocker>();
            }

            triggerable.DefaultTriggerIndex = 1;
            triggerable.TriggerModeSetting = Triggerable.TriggerMode.Single;

        }


        protected override void Awake()
        {
            base.Awake();
            
            for(int i = 0; i < weaponUnits.Count; ++i)
            {
                ProjectileWeaponUnit projectileWeaponUnit = weaponUnits[i].GetComponent<ProjectileWeaponUnit>();
                if (projectileWeaponUnit != null)
                {
                    projectileWeaponUnit.onProjectileLaunched.AddListener(OnMissileLaunched);
                }
            }

            if (module != null)
            {
                module.onActivated.AddListener(OnModuleActivated);
                module.onDeactivated.AddListener(OnModuleDeactivated);
            }
        }

        protected override void TriggerWeaponUnitOnce(int index)
        {
            // Inject the Target's NetworkId into the weapon unit before firing
            if (weaponUnits[index] is ProjectileWeaponUnit projUnit)
            {
                projUnit.TargetIdForNextSpawn = default(Fusion.NetworkId);

                if (targetLocker != null && targetLocker.Target != null)
                {
                    var netObj = targetLocker.Target.GetComponentInParent<Fusion.NetworkObject>();
                    if (netObj != null)
                    {
                        projUnit.TargetIdForNextSpawn = netObj.Id;
                    }
                }
            }

            base.TriggerWeaponUnitOnce(index);
        }


        protected void OnModuleActivated()
        {
            targetLocker.LockingEnabled = true;
        }


        protected void OnModuleDeactivated()
        {
            targetLocker.LockingEnabled = false;
        }


        /// <summary>
        /// Event called when a missile is launched.
        /// </summary>
        /// <param name="missileObject">The missile gameobject</param>
        public void OnMissileLaunched(Projectile missileProjectile)
        {
            if (missileProjectile == null) return;
            Missile missile = missileProjectile.GetComponent<Missile>();
            if (missile == null)
            {
                Debug.LogWarning("Launched missile has no Missile component. Cannot lock onto target.");
            }
            else
            {
                // Set missile parameters
                missile.SetTarget(targetLocker.Target);
                missile.SetLockState(targetLocker.LockState == LockState.Locked ? LockState.Locked : LockState.NoLock);
            }
        }
    }
}