using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using VSX.UI;

namespace VSX.RadarSystem
{
 
    /// <summary>
    /// Provides ability to lock onto a target with specified locking parameters
    /// </summary>
    public class TargetLocker : MonoBehaviour
    {

        [SerializeField]
        protected Trackable target;
        public Trackable Target { get { return target; } }

        public UIFillBar lockingFillBar;

        public bool disableBarOnNoLock;

        [Header("Settings")]       

        [SerializeField]
        protected bool lockingEnabled = true;
        public bool LockingEnabled
        {
            get { return lockingEnabled; }
            set 
            {
                lockingEnabled = value;

                if (!lockingEnabled && lockState != LockState.NoLock)
                {
                    SetLockState(LockState.NoLock);
                }
            }
        }

        [SerializeField]
        protected float lockingTime = 3;
        public float LockingTime { get { return lockingTime; } }

        [SerializeField]
        protected float lockingAngle = 7;
        public float LockingAngle { get { return lockingAngle; } }

        [Tooltip("Wider angle used to maintain an existing lock. If 0, uses lockingAngle.")]
        [SerializeField]
        protected float lockMaintenanceAngle = 45;
        public float LockMaintenanceAngle { get { return lockMaintenanceAngle; } }

        [Tooltip("Grace period (seconds) before a lock is lost when target leaves the maintenance cone.")]
        [SerializeField]
        protected float lockLossGracePeriod = 1.5f;
        public float LockLossGracePeriod { get { return lockLossGracePeriod; } }

        public float ignoreLockingAngleDistance = 0;

        [SerializeField]
        protected float lockingRange = 1000;
        public float LockingRange { get { return lockingRange; } }

        [SerializeField]
        protected Transform lockingReferenceTransform;
        public Transform LockingReferenceTransform
        {
            get { return lockingReferenceTransform; }
            set { lockingReferenceTransform = value; }
        }
        
        protected LockState lockState = LockState.NoLock;
        public LockState LockState { get { return lockState; } }

        protected float lockStateChangeTime = 0;
        public float LockStateChangeTime { get { return lockStateChangeTime; } }

        protected float currentLockAmount = 0;
        public float CurrentLockAmount { get { return currentLockAmount; } }

        // Tracks when the target first left the maintenance cone (for grace period)
        protected float lockLossGraceTimer = -1f;

        [SerializeField]
        protected LockState startingLockStateForNewTarget = LockState.NoLock;

        [Header("Audio")]

        [SerializeField]
        protected bool audioEnabled = true;
        public bool AudioEnabled
        {
            get { return audioEnabled; }
            set 
            { 
                audioEnabled = value; 
                if (!audioEnabled)
                {
                    lockingAudio.Stop();
                    lockedAudio.Stop();                
                }
            }
        }

        [SerializeField]
        protected AudioSource lockingAudio;

        [SerializeField]
        protected AudioSource lockedAudio;

        [Header("Events")]

        // Target locking event
        public UnityEvent onLocking;

        // Target locked event
        public UnityEvent onLocked;

        // Target not locked event
        public UnityEvent onNoLock;

        public UnityEvent onTargetChanged;


        protected virtual void Reset()
        {
            if (lockingReferenceTransform == null) lockingReferenceTransform = transform;
        }


        /// <summary>
        /// Set the target for this target locker.
        /// </summary>
        /// <param name="newTarget">The new target.</param>
        public virtual void SetTarget(Trackable newTarget)
        {
            SetTarget(newTarget, startingLockStateForNewTarget);
        }


        /// <summary>
        /// Set the target for this target locker.
        /// </summary>
        /// <param name="newTarget">The new target.</param>
        /// <param name="lockState">The starting lock state for the new target.</param>
        public virtual void SetTarget(Trackable newTarget, LockState lockState)
        {
            target = newTarget;
            SetLockState(lockState);

            onTargetChanged.Invoke();
        }


        /// <summary>
        /// Clear lock and all target information.
        /// </summary>
        public virtual void ClearTarget()
        {
            SetTarget(null, LockState.NoLock);
        }



        /// <summary>
        /// Check if the target is in the lock zone (narrow cone for acquiring lock).
        /// </summary>
        /// <returns>Whether target is in lock zone.</returns>
        public virtual bool TargetInLockZone()
        {
            return TargetInAngleZone(lockingAngle);
        }


        /// <summary>
        /// Check if the target is in the wider maintenance zone (for keeping an existing lock).
        /// Falls back to lockingAngle if lockMaintenanceAngle is 0.
        /// </summary>
        /// <returns>Whether target is in maintenance zone.</returns>
        public virtual bool TargetInMaintenanceZone()
        {
            float angle = lockMaintenanceAngle > 0 ? lockMaintenanceAngle : lockingAngle;
            return TargetInAngleZone(angle);
        }


        /// <summary>
        /// Shared helper: checks range + angle cone.
        /// </summary>
        protected virtual bool TargetInAngleZone(float maxAngle)
        {
            // Check if target exists
            if (target == null) return false;

            // Check if target is in range
            float distanceToTarget = Vector3.Distance(lockingReferenceTransform.position, target.transform.position);

            if (distanceToTarget > lockingRange)
                return false;

            if (distanceToTarget > ignoreLockingAngleDistance && Vector3.Angle(lockingReferenceTransform.forward, target.transform.position - lockingReferenceTransform.position) > maxAngle)
                return false;

            return true;
        }


        /// <summary>
        /// Directly set the lock state.
        /// </summary>
        /// <param name="newState">The new lock state.</param>
        public virtual void SetLockState(LockState newState)
        {
            // Update lock state
            lockState = newState;
            lockStateChangeTime = Time.time;

            // Call the event
            switch (lockState)
            {
                case LockState.Locked:

                    if (audioEnabled)
                    {
                        if (lockingAudio != null)
                        {
                            lockingAudio.Stop();
                        }

                        if (lockedAudio != null)
                        {
                            lockedAudio.Play();
                        }
                    }

                    if (lockingFillBar != null) lockingFillBar.gameObject.SetActive(true);

                    currentLockAmount = 1;
                    onLocked.Invoke();

                    break;

                case LockState.Locking:

                    if (audioEnabled && lockingAudio != null) lockingAudio.Play();

                    if (lockingFillBar != null) lockingFillBar.gameObject.SetActive(true);

                    onLocking.Invoke();

                    currentLockAmount = 0;

                    break;

                case LockState.NoLock:

                    if (audioEnabled && lockingAudio != null) lockingAudio.Stop();

                    if (disableBarOnNoLock && lockingFillBar != null) lockingFillBar.gameObject.SetActive(false);

                    currentLockAmount = 0;

                    onNoLock.Invoke();
                    break;

            }       
        }


        // Called every frame
        protected virtual void Update()
        {
            switch (lockState)
            {
                case LockState.NoLock:
                    
                    if (lockingEnabled && TargetInLockZone())
                    {
                        SetLockState(LockState.Locking);
                    }

                    break;

                case LockState.Locking:

                    if (lockingEnabled && TargetInLockZone())
                    {
                        if (Mathf.Approximately(lockingTime, 0) || Time.time - lockStateChangeTime > lockingTime)
                        {
                            SetLockState(LockState.Locked);
                        }
                        else
                        {
                            currentLockAmount = (Time.time - lockStateChangeTime) / lockingTime;
                        }
                    }
                    else
                    {
                        SetLockState(LockState.NoLock);
                    }

                    break;

                case LockState.Locked:

                    if (TargetInMaintenanceZone())
                    {
                        // Target is within the wider maintenance cone — reset grace timer
                        lockLossGraceTimer = -1f;
                    }
                    else
                    {
                        // Target left the maintenance cone — start or continue grace timer
                        if (lockLossGraceTimer < 0f)
                        {
                            lockLossGraceTimer = Time.time;
                        }

                        if (Time.time - lockLossGraceTimer >= lockLossGracePeriod)
                        {
                            lockLossGraceTimer = -1f;
                            SetLockState(LockState.NoLock);
                        }
                    }

                    break;
            }

            if (lockingFillBar != null)
            {
                lockingFillBar.SetFill(currentLockAmount);
            }
        }
    }
}
