using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using VSX.Pooling;
using VSX.Vehicles;
using VSX.Utilities;
using VSX.Health;
using Fusion;

namespace VSX.Weapons
{
    /// <summary>
    /// Unity event for running functions when a projectile is launched by a projectile launcher
    /// </summary>
    [System.Serializable]
    public class OnProjectileLauncherProjectileLaunchedEventHandler : UnityEvent<Projectile> { }

    /// <summary>
    /// This class spawns a projectile prefab at a specified interval and with a specified launch velocity.
    /// </summary>
    public class ProjectileWeaponUnit : WeaponUnit, IRootTransformUser, IGameAgentOwnable
    {
        protected GameAgent owner;
        public GameAgent Owner
        {
            get { return owner; }
            set { owner = value; }
        }

        [Header("Settings")]

        [SerializeField]
        protected Transform spawnPoint;

        public override void Aim(Vector3 aimPosition)
        {
            if (aimAssistEnabled) spawnPoint.LookAt(aimPosition, transform.up);
        }
        public override void ClearAim() { spawnPoint.localRotation = Quaternion.identity; }

        [SerializeField]
        protected Projectile projectilePrefab;
        public Projectile ProjectilePrefab
        {
            get { return projectilePrefab; }
            set { projectilePrefab = value; }
        }

        [SerializeField]
        protected bool usePoolManager;

        [SerializeField]
        protected bool addLauncherVelocityToProjectile;

        [Tooltip("Additional velocity to add to the projectile (relative to the launcher). Can be used e.g. to make a missile move out to the side to prevent exhaust blocking the view.")]
        [SerializeField]
        protected Vector3 projectileRelativeImpulseVelocity = Vector3.zero;

        [SerializeField]
        protected float maxInaccuracyAngle = 2;
        public float MaxInaccuracyAngle
        {
            get { return maxInaccuracyAngle; }
            set { maxInaccuracyAngle = value; }
        }

        [Range(0, 1)]
        [SerializeField]
        protected float accuracy = 1;
        public float Accuracy
        {
            get { return accuracy; }
            set { accuracy = value; }
        }

        [Header("Events")]

        // Projectile launched event
        public OnProjectileLauncherProjectileLaunchedEventHandler onProjectileLaunched;

        protected float damageMultiplier = 1;
        protected float healingMultiplier = 1;

        protected Transform rootTransform;
        public Transform RootTransform
        {
            set
            {
                rootTransform = value;

                if (rootTransform != null)
                {
                    rBody = rootTransform.GetComponent<Rigidbody>();
                }
                else
                {
                    rBody = null;
                }
            }
        }

        protected Rigidbody rBody;
        protected NetworkRunner runner;

        public override float Speed
        {
            get { return projectilePrefab != null ? projectilePrefab.Speed : 0; }
        }

        public override float Range
        {
            get { return projectilePrefab != null ? projectilePrefab.Range : 0; }
        }

        public override float Damage(HealthType healthType)
        {
            if (projectilePrefab != null)
            {
                return projectilePrefab.Damage(healthType);
            }
            else
            {
                return 0;
            }
        }

        public override float Healing(HealthType healthType)
        {
            if (projectilePrefab != null)
            {
                return projectilePrefab.Healing(healthType);
            }
            else
            {
                return 0;
            }
        }


        protected override void Reset()
        {
            base.Reset();

            spawnPoint = transform;

            Projectile defaultProjectilePrefab = Resources.Load<Projectile>("SCK/Projectile");
            if (defaultProjectilePrefab != null)
            {
                projectilePrefab = defaultProjectilePrefab;
            }
        }


        protected virtual void Awake()
        {

            if (rootTransform == null) rootTransform = transform.root;

            if (rootTransform != null)
            {
                rBody = rootTransform.GetComponent<Rigidbody>();
            }
        }

        protected virtual void Start()
        {
            if (usePoolManager && PoolManager.Instance == null)
            {
                usePoolManager = false;
                Debug.LogWarning("No PoolManager component found in scene, please add one to pool projectiles.");
            }
            
            // Try to find runner
            runner = FindFirstObjectByType<NetworkRunner>();
            if (runner == null && rootTransform != null) runner = rootTransform.GetComponentInParent<NetworkRunner>();
        }


        /// <summary>
        /// Set the damage multiplier for this weapon unit.
        /// </summary>
        /// <param name="damageMultiplier">The damage multiplier.</param>
        public override void SetDamageMultiplier(float damageMultiplier)
        {
            this.damageMultiplier = damageMultiplier;
        }


        public override void SetHealingMultiplier(float healingMultiplier)
        {
            this.healingMultiplier = healingMultiplier;
        }

        protected float speedMultiplier = 1;
        public void SetSpeedMultiplier(float speedMultiplier)
        {
            this.speedMultiplier = speedMultiplier;
        }

        protected float rangeMultiplier = 1;
        public void SetRangeMultiplier(float rangeMultiplier)
        {
            this.rangeMultiplier = rangeMultiplier;
        }


        // Launch a projectile
        public override void TriggerOnce()
        {
            if (projectilePrefab != null)
            {
                float nextMaxInaccuracyAngle = maxInaccuracyAngle * (1 - accuracy);
                spawnPoint.Rotate(new Vector3(Random.Range(-nextMaxInaccuracyAngle, nextMaxInaccuracyAngle),
                                                Random.Range(-nextMaxInaccuracyAngle, nextMaxInaccuracyAngle),
                                                Random.Range(-nextMaxInaccuracyAngle, nextMaxInaccuracyAngle)));

                // Get/instantiate the projectile
                Projectile projectileController = null;

                bool spawnedViaNetwork = false;

                // Network Spawning (Only if we are the StateAuthority/Host, OR if we want to predict spawning which is complex for projectiles)
                // For simplest implementation: Host spawns projectiles.
                // Clients just fire visually or wait for replication?
                // Standard approach: InputAuthority requests fire -> Host spawns -> Replicated to all.
                // WE ARE IN TRIGGERONCE, which usually runs on InputAuthority (client pressing button) AND StateAuthority (Host processing input).
                // BUT TriggerOnce is typically called by the Weapon class which responds to Input.
                // We need to check if we can spawn.
                
                if (runner != null && runner.IsRunning && runner.IsServer)
                {
                     // Server spawns the network object.
                     // CRITICAL: Use onBeforeSpawned to set [Networked] properties BEFORE the first snapshot.
                     // Without this, the proxy receives NetworkedSpeed=0 and projectiles are invisible
                     // because Render() translates by speed*deltaTime = 0.
                     float finalSpeed = projectilePrefab.Speed * speedMultiplier;
                     float finalMaxDistance = projectilePrefab.Range * rangeMultiplier;
                     Vector3 spawnPos = spawnPoint.position;
                     Quaternion spawnRot = spawnPoint.rotation;

                     var networkObject = runner.Spawn(projectilePrefab, spawnPos, spawnRot, runner.LocalPlayer,
                         onBeforeSpawned: (runner, obj) =>
                         {
                             var proj = obj.GetComponent<Projectile>();
                             if (proj != null)
                             {
                                 proj.NetworkedSpeed = finalSpeed;
                                 proj.NetworkedMaxDistance = finalMaxDistance;
                                 proj.NetworkedDamageMultiplier = damageMultiplier;
                                 proj.NetworkedHealingMultiplier = healingMultiplier;
                                 // CRITICAL: Set spawn position/rotation so proxies can apply it.
                                 // Without NetworkTransform, Fusion does NOT replicate the transform.
                                 proj.NetworkedSpawnPosition = spawnPos;
                                 proj.NetworkedSpawnRotation = spawnRot;

                                 if (rootTransform != null) 
                                 {
                                     var parentNetObj = rootTransform.GetComponentInParent<NetworkObject>();
                                     if (parentNetObj != null) proj.OwnerId = parentNetObj.Id;
                                 }
                             }
                         });
                     projectileController = networkObject.GetComponent<Projectile>();
                     spawnedViaNetwork = true;
                }
                else if (runner != null && runner.IsRunning && !runner.IsServer)
                {
                    // CLIENT-SIDE PREDICTION (Visual Dummy)
                    // Spawn a local visual-only copy instantly to prevent perceived spawn lag.
                    NetworkObject netObj = rootTransform.GetComponentInParent<NetworkObject>();
                    if (netObj != null && netObj.HasInputAuthority && !netObj.HasStateAuthority)
                    {
                        if (usePoolManager)
                        {
                            projectileController = PoolManager.Instance.Get(projectilePrefab.gameObject, spawnPoint.position, spawnPoint.rotation).GetComponent<Projectile>();
                        }
                        else
                        {
                            projectileController = GameObject.Instantiate(projectilePrefab, spawnPoint.position, spawnPoint.rotation);
                        }
                        
                        if (projectileController != null)
                        {
                            projectileController.SetVisualDummy();
                        }
                    }
                }
                else if (runner == null || !runner.IsRunning)
                {
                     // Legacy/Offline fallback
                    if (usePoolManager)
                    {
                        projectileController = PoolManager.Instance.Get(projectilePrefab.gameObject, spawnPoint.position, spawnPoint.rotation).GetComponent<Projectile>();
                    }
                    else
                    {
                        projectileController = GameObject.Instantiate(projectilePrefab, spawnPoint.position, spawnPoint.rotation);
                    }
                }
                
                if (projectileController != null)
                {
                    projectileController.SetOwner(owner);
                    projectileController.SetSenderRootTransform(rootTransform);

                    if (spawnedViaNetwork)
                    {
                        // Speed, MaxDistance, Damage, Healing were already set in onBeforeSpawned
                        // (before the first snapshot). Only set lifetime here.
                        projectileController.SetLifetime(100f);
                    }
                    else
                    {
                        // Legacy/Offline: apply modifiers the old way
                        projectileController.SetDamageMultiplier(damageMultiplier);
                        projectileController.SetHealingMultiplier(healingMultiplier);
                        projectileController.Speed *= speedMultiplier;
                        projectileController.SetMaxDistance(projectileController.Range * rangeMultiplier);
                        projectileController.SetLifetime(100f);
                    }

                    if (addLauncherVelocityToProjectile && rBody != null)
                    {
                        projectileController.AddVelocity(rBody.linearVelocity);
                        projectileController.AddVelocity(transform.TransformDirection(projectileRelativeImpulseVelocity));
                    }

                    // Only invoke events (audio, muzzle flash) if we are the authority or the shooter.
                    // This prevents proxy ships from duplicating the firing audio when the host's prediction matches the client's.
                    NetworkObject netObj = rootTransform.GetComponentInParent<NetworkObject>();
                    if (runner == null || !runner.IsRunning || netObj == null || netObj.HasStateAuthority || netObj.HasInputAuthority)
                    {
                        onProjectileLaunched.Invoke(projectileController);
                    }
                }
                else
                {
                    // For Clients that don't spawn projectiles locally, still invoke the event with null
                    // so VFX and Audio (which ignore the projectile argument) will play.
                    // DO NOT do this for Proxy ships (Host's ship on Client, or other clients on Client).
                    NetworkObject netObj = rootTransform.GetComponentInParent<NetworkObject>();
                    if (runner == null || !runner.IsRunning || netObj == null || netObj.HasStateAuthority || netObj.HasInputAuthority)
                    {
                        onProjectileLaunched.Invoke(null);
                    }
                }
            }

            ClearAim();
        }
    }
}