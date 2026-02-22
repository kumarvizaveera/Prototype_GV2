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
            // Inject the Target's NetworkId and Trackable into the weapon unit before firing
            if (weaponUnits[index] is ProjectileWeaponUnit projUnit)
            {
                projUnit.TargetIdForNextSpawn = default(Fusion.NetworkId);
                projUnit.TargetTrackableForNextSpawn = null;

                // Try to get target from our own TargetLocker first
                Trackable resolvedTarget = (targetLocker != null && targetLocker.Target != null) ? targetLocker.Target : null;

                // FALLBACK: If our TargetLocker has no target, search for the ship's TargetSelector.
                // On the client, the MissileWeapon.TargetLocker event chain from WeaponsController
                // may not be connected (timing issue), but the ship's TargetSelector has the target.
                if (resolvedTarget == null)
                {
                    // Try WeaponsController first (it has the weaponsTargetSelector)
                    var weaponsController = GetComponentInParent<WeaponsController>();
                    if (weaponsController != null && weaponsController.WeaponsTargetSelector != null)
                    {
                        resolvedTarget = weaponsController.WeaponsTargetSelector.SelectedTarget;
                        if (resolvedTarget != null)
                        {
                            Debug.Log($"[MissileWeapon] TriggerWeaponUnitOnce: FALLBACK got target from WeaponsController.TargetSelector: {resolvedTarget.name}");
                        }
                    }

                    // Try any TargetSelector in parent hierarchy
                    if (resolvedTarget == null)
                    {
                        var selectors = GetComponentsInParent<TargetSelector>();
                        foreach (var sel in selectors)
                        {
                            if (sel.SelectedTarget != null)
                            {
                                resolvedTarget = sel.SelectedTarget;
                                Debug.Log($"[MissileWeapon] TriggerWeaponUnitOnce: FALLBACK got target from TargetSelector: {resolvedTarget.name}");
                                break;
                            }
                        }
                    }
                }

                if (resolvedTarget != null)
                {
                    // Store the Trackable directly for visual dummy use
                    projUnit.TargetTrackableForNextSpawn = resolvedTarget;

                    // Store the NetworkId for the host-spawned networked missile
                    var netObj = resolvedTarget.GetComponentInParent<Fusion.NetworkObject>();
                    if (netObj != null)
                    {
                        projUnit.TargetIdForNextSpawn = netObj.Id;
                    }

                    Debug.Log($"[MissileWeapon] TriggerWeaponUnitOnce: target={resolvedTarget.name} | " +
                              $"targetId={projUnit.TargetIdForNextSpawn} | " +
                              $"lockState={targetLocker?.LockState} | source={(targetLocker?.Target != null ? "TargetLocker" : "Fallback")}");
                }
                else
                {
                    Debug.LogWarning($"[MissileWeapon] TriggerWeaponUnitOnce: No target found anywhere! " +
                                     $"targetLocker={targetLocker != null} | lockState={targetLocker?.LockState}");
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
            if (missileProjectile == null)
            {
                Debug.Log($"[MissileWeapon] OnMissileLaunched: projectile is NULL (client without visual dummy?)");
                return;
            }
            Missile missile = missileProjectile.GetComponent<Missile>();
            if (missile == null)
            {
                Debug.LogWarning("Launched missile has no Missile component. Cannot lock onto target.");
            }
            else
            {
                // Try our TargetLocker first, then fallback to ship's TargetSelector
                Trackable target = (targetLocker != null) ? targetLocker.Target : null;
                LockState lockState = (targetLocker != null) ? targetLocker.LockState : LockState.NoLock;

                if (target == null)
                {
                    var weaponsController = GetComponentInParent<WeaponsController>();
                    if (weaponsController != null && weaponsController.WeaponsTargetSelector != null)
                    {
                        target = weaponsController.WeaponsTargetSelector.SelectedTarget;
                        if (target != null) lockState = LockState.Locked; // Player clearly locked and fired
                    }
                }

                bool isVisualDummy = missileProjectile.IsVisualDummy;
                Debug.Log($"[MissileWeapon] OnMissileLaunched: isVisualDummy={isVisualDummy} | " +
                          $"target={(target != null ? target.name : "NULL")} | lockState={lockState}");

                // Set missile parameters
                missile.SetTarget(target);
                missile.SetLockState(lockState);
            }
        }
    }
}