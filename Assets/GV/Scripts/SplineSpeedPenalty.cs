using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;

namespace GV.Movement
{
    /// <summary>
    /// Increases the Rigidbody's linear damping (drag) when the object moves away from the spline.
    /// Safely handles base drag by syncing when within the safe radius.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class SplineSpeedPenalty : MonoBehaviour
    {
        [Header("Spline Settings")]
        [Tooltip("The spline container to follow.")]
        public SplineContainer targetSpline;

        [Tooltip("Index of the spline within the container.")]
        public int splineIndex = 0;

        [Header("Penalty Settings")]
        [Tooltip("Radius within which no penalty is applied.")]
        [Min(0f)] public float safeRadius = 5f;

        [Tooltip("Distance from the safe radius where the penalty reaches its maximum.")]
        [Min(0f)] public float penaltyFalloffDistance = 10f;

        [Tooltip("The additional drag (linear damping) added at maximum penalty.")]
        [Min(0f)] public float maxAddedDrag = 5f;

        [Header("Debug")]
        public bool showDebugVisuals = true;
        [SerializeField] private float _currentBaseDrag; // Visual aid
        [SerializeField] private bool _isPenalized = false;

        private Rigidbody _rb;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _currentBaseDrag = _rb.linearDamping;
        }

        private void OnDisable()
        {
            // Restore base drag when disabled to prevent sticking at high drag
            if (_rb != null && _isPenalized)
            {
                _rb.linearDamping = _currentBaseDrag;
                _isPenalized = false;
            }
        }

        private void FixedUpdate()
        {
            if (targetSpline == null || _rb == null) return;

            // 1. Find nearest point
            Vector3 currentPos = transform.position;
            float3 localPos = targetSpline.transform.InverseTransformPoint(currentPos);
            SplineUtility.GetNearestPoint(targetSpline.Spline, localPos, out float3 nearestLocal, out float t);
            Vector3 nearestWorld = targetSpline.transform.TransformPoint(nearestLocal);

            // 2. Calculate Distance and Penalty
            float distance = Vector3.Distance(currentPos, nearestWorld);
            float penaltyFactor = 0f;

            if (distance > safeRadius)
            {
                float excessDist = distance - safeRadius;
                penaltyFactor = Mathf.Clamp01(excessDist / Mathf.Max(0.001f, penaltyFalloffDistance));
            }

            // 3. Apply Logic
            if (penaltyFactor > 0)
            {
                // We are in penalty zone
                if (!_isPenalized)
                {
                    // Just entered penalty zone. Capture the current drag as our base.
                    // This ensures we work relative to whatever the drag was a moment ago.
                    _currentBaseDrag = _rb.linearDamping;
                    _isPenalized = true;
                }

                // Apply penalty on top of the captured base
                float targetDrag = _currentBaseDrag + (penaltyFactor * maxAddedDrag);
                _rb.linearDamping = targetDrag;
            }
            else
            {
                // We are in safe zone
                if (_isPenalized)
                {
                    // Just exited penalty zone. Restore the base drag.
                    _rb.linearDamping = _currentBaseDrag;
                    _isPenalized = false;
                }
                else
                {
                    // Staying in safe zone. Continuously update base drag to stay in sync with other scripts.
                    // This allows other components (like engines) to change drag while we are safe.
                    _currentBaseDrag = _rb.linearDamping;
                }
            }
        }

        private void OnDrawGizmos()
        {
            if (!showDebugVisuals || targetSpline == null) return;

            if (Application.isPlaying)
            {
                Vector3 currentPos = transform.position;
                float3 localPos = targetSpline.transform.InverseTransformPoint(currentPos);
                SplineUtility.GetNearestPoint(targetSpline.Spline, localPos, out float3 nearestLocal, out float t);
                Vector3 nearestWorld = targetSpline.transform.TransformPoint(nearestLocal);

                // Draw safe radius
                Gizmos.color = new Color(0, 1, 1, 0.3f); // Cyan
                Gizmos.DrawWireSphere(nearestWorld, safeRadius);

                // Draw Max Penalty radius
                Gizmos.color = new Color(1, 0, 0, 0.3f); // Red
                Gizmos.DrawWireSphere(nearestWorld, safeRadius + penaltyFalloffDistance);
            }
        }
    }
}
