using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VSX.RadarSystem;
using UnityEngine.Events;
using VSX.Health;
using VSX.Engines3D;
using VSX.Utilities;

namespace VSX.Weapons
{

    /// <summary>
    /// Base class for a guided missile.
    /// </summary>
    public class Missile : RigidbodyProjectile
    {
        // Debug: track logging so we don't spam
        private bool _missileSpawnLogged = false;
        private float _lastDebugLogTime = 0f;

        [Header("Settings")]

        [Tooltip("How long before the missile explodes after losing lock.")]
        [SerializeField]
        protected float noLockLifetime = 4;

        [Tooltip("The missile's explosion trigger mode.")]
        [SerializeField]
        protected TargetProximityTriggerMode triggerMode = TargetProximityTriggerMode.OnDistanceIncrease;

        [Tooltip("The distance within which the missile's target proximity trigger can be triggered.")]
        [SerializeField]
        protected float triggerDistance = 49;

        [Tooltip("The distance within which the missile aims for the lead target position rather than the target itself. Prevents issues with lead target aiming and locking angle when far away from target.")]
        [SerializeField]
        protected float leadTargetThreshold = 1000;

        protected bool targetWasInsideTrigger = false;

        protected bool triggered = false;

        [Header("Guidance")]

        [Tooltip("The PID controller for the missile's steering.")]
        [SerializeField]
        protected PIDController3D steeringPIDController;

        [Header("Components")]

        [Tooltip("The missile's locking component.")]
        [SerializeField]
        protected TargetLocker targetLocker;

        [Tooltip("The missile's engine.")]
        [SerializeField]
        protected VehicleEngines3D engines;   

        protected bool locked = false;

        public UnityEvent onTargetLocked;
        public UnityEvent onTargetLockLost;


        /// <summary>
        /// Missiles use Rigidbody physics + engine steering (via Missile.Update) for movement.
        /// Disable the manual transform.Translate proxy simulation in Projectile.Render() to
        /// prevent double movement that causes missiles to overshoot targets on the client screen.
        /// </summary>
        protected override bool UseManualProxyMovement => engines == null;


        /// <summary>
        /// Get the missile's speed.
        /// </summary>
        public override float Speed
        {
            get { return engines.GetDefaultMaxSpeedByAxis(false).z; }
        }


        /// <summary>
        /// Get the range of the missile (how far it can go).
        /// </summary>
        public override float Range
        {
            get { return targetLocker.LockingRange; }
        }


        /// <summary>
        /// Get the damage the missile is capable of for a specified health type.
        /// </summary>
        /// <param name="healthType">The health type to get damage for.</param>
        /// <returns>The damage value.</returns>
        public override float Damage(HealthType healthType)
        {
            for (int i = 0; i < healthModifier.DamageOverrideValues.Count; ++i)
            {
                if (healthModifier.DamageOverrideValues[i].HealthType == healthType)
                {
                    return healthModifier.DamageOverrideValues[i].Value;
                }
            }

            return healthModifier.DefaultDamageValue;
        }


        /// <summary>
        /// Called when the component is first added to a gameobject or reset in the inspector.
        /// </summary>
        protected override void Reset()
        {

            base.Reset();

            m_Rigidbody.useGravity = false;
            m_Rigidbody.mass = 1;
            m_Rigidbody.linearDamping = 3;
            m_Rigidbody.angularDamping = 4;

            // Add/get engines
            engines = transform.GetComponentInChildren<VehicleEngines3D>();
            if (engines == null)
            {
                engines = gameObject.AddComponent<VehicleEngines3D>();
            }

            // Add/get target locker
            targetLocker = transform.GetComponentInChildren<TargetLocker>();
            if (targetLocker == null)
            {
                targetLocker = gameObject.AddComponent<TargetLocker>();
            }

            detonator.DetonatingDuration = 2;

            disableAfterDistanceCovered = false;

            areaEffect = true;

            healthModifier.DefaultDamageValue = 1000;
        }


        protected override void OnEnable()
        {
            base.OnEnable();
            targetWasInsideTrigger = false;
            triggered = false;
        }

        /// <summary>
        /// Override Detonate to also stop physics on proxies so the missile doesn't
        /// keep flying after visual detonation while waiting for host despawn.
        /// </summary>
        public override void Detonate()
        {
            bool isAuth = Object != null && Object.HasStateAuthority;
            string role = isAuth ? "HOST" : (Object != null ? "PROXY" : "LOCAL");
            Debug.Log($"[Missile] DETONATE called on {role} | pos={transform.position} | " +
                      $"detonated={detonated} | triggered={triggered} | " +
                      $"target={(targetLocker?.Target != null ? targetLocker.Target.name : "NULL")} | " +
                      $"lockState={targetLocker?.LockState} | " +
                      $"rbVel={(m_Rigidbody != null ? m_Rigidbody.linearVelocity.magnitude.ToString("F1") : "N/A")} | " +
                      $"engines={engines != null}");

            // Stop engines before base Detonate so the missile doesn't keep thrusting
            if (engines != null)
            {
                engines.SetSteeringInputs(Vector3.zero);
                engines.SetMovementInputs(Vector3.zero);
            }

            base.Detonate();

            // On proxy: freeze the Rigidbody so the missile body doesn't keep drifting
            if (Object != null && !Object.HasStateAuthority && m_Rigidbody != null)
            {
                Debug.Log($"[Missile] PROXY freeze Rigidbody: vel was {m_Rigidbody.linearVelocity.magnitude:F1}, setting kinematic");
                m_Rigidbody.linearVelocity = Vector3.zero;
                m_Rigidbody.angularVelocity = Vector3.zero;
                m_Rigidbody.isKinematic = true;
            }
        }


        public override void Spawned()
        {
            base.Spawned();

            bool isAuth = Object.HasStateAuthority;
            string role = isAuth ? "HOST" : "PROXY";

            Debug.Log($"[Missile] SPAWNED ({role}) | pos={transform.position} | " +
                      $"NetworkedTargetId={NetworkedTargetId} valid={NetworkedTargetId.IsValid} | " +
                      $"engines={engines != null} | UseManualProxyMovement={UseManualProxyMovement} | " +
                      $"rb.isKinematic={(m_Rigidbody != null ? m_Rigidbody.isKinematic.ToString() : "N/A")} | " +
                      $"rb.velocity={(m_Rigidbody != null ? m_Rigidbody.linearVelocity.magnitude.ToString("F1") : "N/A")}");

            // Try to resolve the initial networked target lock
            if (NetworkedTargetId.IsValid && targetLocker != null)
            {
                if (Runner.TryFindObject(NetworkedTargetId, out Fusion.NetworkObject targetNetObj))
                {
                    var trackable = targetNetObj.GetComponentInChildren<Trackable>(true);
                    if (trackable == null)
                    {
                        trackable = targetNetObj.GetComponentInParent<Trackable>();
                    }

                    if (trackable != null)
                    {
                        targetLocker.SetTarget(trackable);
                        SetLockState(LockState.Locked);
                        Debug.Log($"[Missile] ({role}) Target resolved: {trackable.name} | LockState={targetLocker.LockState}");
                    }
                    else
                    {
                        Debug.LogWarning($"[Missile] ({role}) Target NetworkObject found but NO Trackable component! netObj={targetNetObj.name}");
                    }
                }
                else
                {
                    Debug.LogWarning($"[Missile] ({role}) Failed to find NetworkObject for targetId={NetworkedTargetId}");
                }
            }
            else
            {
                Debug.LogWarning($"[Missile] ({role}) No valid target ID on spawn. NetworkedTargetId={NetworkedTargetId}, targetLocker={targetLocker != null}");
            }
        }


        protected override void Awake()
        {
            base.Awake();

            if (collisionScanner != null)
            {
                collisionScanner.Rigidbody = m_Rigidbody;
            }
        }


        /// <summary>
        /// Set the target.
        /// </summary>
        /// <param name="target">The new target.</param>
        public virtual void SetTarget(Trackable target)
        {
            if (targetLocker != null)
            {
                targetLocker.SetTarget(target);
                SetLockState(LockState.Locked);
            }
        }


        /// <summary>
        /// Set the lock state of the missile.
        /// </summary>
        /// <param name="lockState">The new lock state.</param>
        public virtual void SetLockState(LockState lockState)
        {
            if (targetLocker != null) targetLocker.SetLockState(lockState);

            if (lockState == LockState.Locked)
            {
                locked = true;
                onTargetLocked.Invoke();
            }
        }


        // Check if the trigger should be activated
        protected virtual void CheckTrigger()
        {
            if (detonated) return;

            if (triggered) return;

            if (targetLocker.Target == null) return;

            if (targetLocker.LockState != LockState.Locked) return;

            bool targetInsideTrigger = false;
            float distToTarget = Vector3.Distance(transform.position, targetLocker.Target.transform.position);

            Collider[] colliders = Physics.OverlapSphere(transform.position, triggerDistance, collisionScanner.HitMask);
            for (int i = 0; i < colliders.Length; ++i)
            {
                if (colliders[i].transform.IsChildOf(targetLocker.Target.transform))
                {
                    targetInsideTrigger = true;

                    bool triggerNow = false;

                    switch (triggerMode)
                    {
                        case TargetProximityTriggerMode.OnTargetInRange:

                            triggerNow = true;
                            break;

                        case TargetProximityTriggerMode.OnDistanceIncrease:

                            float dist0 = GetClosestDistanceToTarget(0, colliders[i]);
                            float dist1 = GetClosestDistanceToTarget(Time.deltaTime, colliders[i]);

                            triggerNow = Mathf.Abs(dist1 - dist0) > 0.01f && dist1 > dist0;

                            Vector3 toTarget = targetLocker.Target.transform.position - transform.position;
                            Vector3 toTargetNext = (targetLocker.Target.transform.position + (targetLocker.Target.Rigidbody == null ? Vector3.zero : targetLocker.Target.Rigidbody.linearVelocity * Time.deltaTime)) -
                                                    (transform.position + (m_Rigidbody == null ? Vector3.zero : m_Rigidbody.linearVelocity * Time.deltaTime));

                            if (toTargetNext.magnitude < toTarget.magnitude) triggerNow = false;

                            break;
                    }

                    if (triggerNow)
                    {
                        string role = (Object != null && Object.HasStateAuthority) ? "HOST" : "PROXY";
                        Debug.Log($"[Missile] CheckTrigger TRIGGERED ({role}) | triggerMode={triggerMode} | " +
                                  $"distToTarget={distToTarget:F1} | triggerDist={triggerDistance} | " +
                                  $"targetInsideTrigger={targetInsideTrigger} | pos={transform.position}");
                        triggered = true;
                        Detonate();
                        return;
                    }
                }
            }

            if (!targetInsideTrigger && targetWasInsideTrigger)
            {
                string role = (Object != null && Object.HasStateAuthority) ? "HOST" : "PROXY";
                Debug.Log($"[Missile] CheckTrigger PAST-TARGET ({role}) | target left trigger zone | " +
                          $"distToTarget={distToTarget:F1} | triggerDist={triggerDistance}");
                triggered = true;
                Detonate();
            }
            else
            {
                targetWasInsideTrigger = targetInsideTrigger;
            }
        }


        // Get the closest distance to the target based on a time projection (necessary because things can move very fast and the target can
        // change position a lot in one frame).
        protected virtual float GetClosestDistanceToTarget(float timeProjection, Collider targetCollider)
        {
            Vector3 targetOffset = targetLocker.Target.Rigidbody != null ? targetLocker.Target.Rigidbody.linearVelocity * timeProjection : Vector3.zero;

            if (m_Rigidbody != null) targetOffset -= m_Rigidbody.linearVelocity * timeProjection;

            Vector3 closestPoint = targetCollider.ClosestPoint(transform.position + targetOffset);

            return (closestPoint - transform.position).magnitude;
        }


        // Called every frame
        protected override void Update()
        {
            base.Update();

            // After detonation on proxy, stop all missile logic — the missile is dead,
            // just waiting for the host to despawn the NetworkObject.
            if (detonated)
            {
                if (engines != null)
                {
                    engines.SetSteeringInputs(Vector3.zero);
                    engines.SetMovementInputs(Vector3.zero);
                }
                return;
            }

            // Periodic debug log (every 0.5s) to track missile flight state
            if (Time.time - _lastDebugLogTime > 0.5f)
            {
                _lastDebugLogTime = Time.time;
                string role = (Object != null && Object.HasStateAuthority) ? "HOST" : (Object != null ? "PROXY" : "LOCAL");
                float distToTarget = targetLocker?.Target != null ? Vector3.Distance(transform.position, targetLocker.Target.transform.position) : -1f;
                Debug.Log($"[Missile] UPDATE ({role}) | pos={transform.position} | " +
                          $"lockState={targetLocker?.LockState} | target={(targetLocker?.Target != null ? targetLocker.Target.name : "NULL")} | " +
                          $"distToTarget={distToTarget:F1} | triggerDist={triggerDistance} | " +
                          $"rbVel={(m_Rigidbody != null ? m_Rigidbody.linearVelocity.magnitude.ToString("F1") : "N/A")} | " +
                          $"rbKinematic={(m_Rigidbody != null ? m_Rigidbody.isKinematic.ToString() : "N/A")} | " +
                          $"detonated={detonated} | triggered={triggered}");
            }

            CheckTrigger();

            if (targetLocker.LockState == LockState.Locked)
            {
                if (engines != null)
                {
                    // Steer
                    Vector3 targetVelocity = targetLocker.Target.Rigidbody != null ? targetLocker.Target.Rigidbody.linearVelocity : Vector3.zero;
                    Vector3 toTarget = targetLocker.Target.transform.position - transform.position;

                    Vector3 targetPos = targetLocker.Target.transform.position;
                    if (toTarget.magnitude < leadTargetThreshold)
                    {
                        targetPos = TargetLeader.GetLeadPosition(transform.position, Speed, targetLocker.Target.transform.position, targetVelocity);
                    }

                    Maneuvring.TurnToward(transform, targetPos, new Vector3(360, 360, 0), steeringPIDController);
                    engines.SetSteeringInputs(steeringPIDController.GetControlValues());
                    engines.SetMovementInputs(new Vector3(0, 0, 1));
                }

            }
            else
            {
                // Detonate after lifetime
                if (locked)
                {
                    onTargetLockLost.Invoke();
                    detonator.Detonate(noLockLifetime);
                }

                // Clear steering inputs
                if (engines != null)
                {
                    engines.SetSteeringInputs(Vector3.zero);
                    engines.SetMovementInputs(new Vector3(0, 0, 1));
                }

            }
        }


        /// <summary>
        ///  Editor only, draws gizmos in scene view.
        /// </summary>
        protected override void OnDrawGizmosSelected()
        {
            base.OnDrawGizmosSelected();

            Color c = Gizmos.color;

            Gizmos.color = new Color(1, 0, 0);
            Gizmos.DrawWireSphere(transform.position, triggerDistance);

            Gizmos.color = c;
        }


        /// <summary>
        /// Update the missile's movement every frame.
        /// </summary>
        protected override void MovementUpdate()
        {
            if (engines == null)
            {
                base.MovementUpdate();
            }
        }


        /// <summary>
        /// Update the missile's movement in fixed update.
        /// </summary>
        protected override void MovementFixedUpdate()
        {
            if (engines == null)
            {
                base.MovementFixedUpdate();
            }
        }
    }
}