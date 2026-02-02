using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;

namespace GV.Movement
{
    /// <summary>
    /// component that tethers a Rigidbody to a spline, applying forces to keep it within a "tube"
    /// while allowing 6DOF movement.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class SplineTether : MonoBehaviour
    {
        [Header("Spline Settings")]
        [Tooltip("The spline container to follow.")]
        public SplineContainer targetSpline;
        
        [Tooltip("Index of the spline within the container.")]
        public int splineIndex = 0;

        [Header("Tether Physics")]
        [Tooltip("Radius around the spline where movement is free (deadzone).")]
        [Min(0f)] public float tetherRadius = 5f;

        [Tooltip("Strength of the force pulling back to the spline center.")]
        [Min(0f)] public float attractionForce = 20f;

        [Tooltip("Damping to prevent oscillation when being pulled back.")]
        [Min(0f)] public float damping = 5f;

        [Header("Alignment")]
        [Tooltip("Strength of the torque aligning the vehicle with the spline tangent.")]
        [Min(0f)] public float alignmentTorque = 10f;

        [Tooltip("If true, automatically sets the minimum forward speed to match spline direction.")]
        public bool autoForwardAssist = false;

        [Tooltip("The forward force to apply when auto forward assist is active.")]
        [Min(0f)] public float autoForwardForce = 50f;

        [Header("Debug")]
        public bool showDebugVisuals = true;

        private Rigidbody _rb;
        private float _nearestT;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
        }

        private void FixedUpdate()
        {
            if (targetSpline == null || _rb == null) return;

            // 1. Find nearest point on the spline
            // Convert current position to spline local space
            Vector3 currentPos = transform.position;
            float3 localPos = targetSpline.transform.InverseTransformPoint(currentPos);
            
            // Get nearest point on the spline
            SplineUtility.GetNearestPoint(targetSpline.Spline, localPos, out float3 nearestLocal, out float t, 8, 2);
            _nearestT = t;

            Vector3 nearestWorld = targetSpline.transform.TransformPoint(nearestLocal);

            // 2. Calculate tether force
            Vector3 toSpline = nearestWorld - currentPos;
            float distToSpline = toSpline.magnitude;

            // Only apply force if outside the tether radius
            if (distToSpline > tetherRadius)
            {
                Vector3 pullDirection = toSpline.normalized;
                
                // Spring force: proportional to how far (minus radius) we are
                float displacement = distToSpline - tetherRadius;
                Vector3 springForce = pullDirection * (displacement * attractionForce);

                // Damping force: opposes velocity perpendicular to spline
                // Projected velocity onto the pull direction
                Vector3 velocityTowardSpline = Vector3.Project(_rb.linearVelocity, pullDirection);
                Vector3 dampingForce = -velocityTowardSpline * damping;

                _rb.AddForce(springForce + dampingForce, ForceMode.Acceleration);
            }

            // 3. Alignment Torque (Optional)
            // Align forward vector with spline tangent
             Vector3 tangent = Vector3.Normalize(targetSpline.EvaluateTangent(targetSpline.Splines[splineIndex], _nearestT));
            Vector3 tangentWorld = targetSpline.transform.TransformDirection(tangent);

            // Calculate rotation towards tangent
            if (alignmentTorque > 0)
            {
                Quaternion targetRotation = Quaternion.LookRotation(tangentWorld, transform.up);
                // We only want to align the forward vector, allowing roll to be free?
                // Or full alignment? Let's do a soft look-at torque.
                
                Vector3 alignmentError = Vector3.Cross(transform.forward, tangentWorld);
                _rb.AddTorque(alignmentError * alignmentTorque, ForceMode.Acceleration);
            }

            // 4. Auto Forward Assist
            if (autoForwardAssist)
            {
                 // Project forward vector onto tangent to see if we are facing the right way
                 float dot = Vector3.Dot(transform.forward, tangentWorld);
                 if (dot > 0)
                 {
                     _rb.AddForce(tangentWorld * autoForwardForce, ForceMode.Acceleration);
                 }
            }
        }

        private void OnDrawGizmos()
        {
            if (!showDebugVisuals || targetSpline == null) return;
            
            if (Application.isPlaying)
            {
                 // Draw the nearest point line
                 Vector3 currentPos = transform.position;
                 Vector3 localPos = targetSpline.transform.InverseTransformPoint(currentPos);
                 SplineUtility.GetNearestPoint(targetSpline.Spline, localPos, out float3 nearestLocal, out float t);
                 Vector3 nearestWorld = targetSpline.transform.TransformPoint(nearestLocal);

                 Gizmos.color = Color.green;
                 Gizmos.DrawLine(currentPos, nearestWorld);
                 Gizmos.DrawWireSphere(nearestWorld, 0.5f);

                 // Draw tether radius
                 Gizmos.color = new Color(0, 1, 0, 0.2f);
                 Gizmos.DrawWireSphere(nearestWorld, tetherRadius);
            }
        }
    }
}
