using System;
using System.Reflection;
using UnityEngine;

namespace VSX.Engines3D
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(VehicleEngines3D))]
    public class VehicleEngines3DProfileSwapper : MonoBehaviour
    {
        [Serializable]
        public class HandlingProfile
        {
            [Header("Engines (VehicleEngines3D)")]
            public Vector3 maxMovementForces = new Vector3(400, 400, 400);
            public Vector3 maxSteeringForces = new Vector3(16f, 16f, 25f);
            public Vector3 maxBoostForces = new Vector3(800, 800, 800);
            public float movementInputResponseSpeed = 5f;
            public AnimationCurve steeringBySpeedCurve = AnimationCurve.Linear(0, 1, 1, 1);
            public float boostSteeringCoefficient = 1f;

            [Header("Optional Rigidbody Overrides")]
            public bool overrideRigidbody = false;
            public float mass = 1f;
            public float linearDamping = 3f;
            public float angularDamping = 4f;
            public bool useGravity = false;
        }

        [Header("References")]
        [SerializeField] private VehicleEngines3D engines;
        [Tooltip("Drag A mesh root (A_Spaceship_...) here")]
        public Transform meshRootA;
        [Tooltip("Drag B mesh root (B_GarudaVahana_...) here")]
        public Transform meshRootB;

        [Header("Profiles")]
        public HandlingProfile profileA = new HandlingProfile();
        public HandlingProfile profileB = new HandlingProfile();

        [Header("Behavior")]
        [Tooltip("If both meshes are active at start, turns B off. If both are off, turns A on.")]
        public bool forceSingleActiveOnStart = true;

        [Tooltip("If true, automatically applies profile based on which meshRoot is active.")]
        public bool autoDetectFromActiveMesh = true;

        [Header("Optional Input Switching (for testing)")]
        public bool allowKeySwitch = false;
        public KeyCode keySelectA = KeyCode.Alpha1;
        public KeyCode keySelectB = KeyCode.Alpha2;

        [Tooltip("If true, this component will also toggle mesh roots when switching by key or API.")]
        public bool toggleMeshesWhenSwitching = true;

        private int activeIndex = -1;

        // Cached reflection fields (VehicleEngines3D has these as protected)
        private FieldInfo fMaxMovement;
        private FieldInfo fMaxSteering;
        private FieldInfo fMaxBoost;
        private FieldInfo fMoveResponse;
        private FieldInfo fSteeringCurve;
        private FieldInfo fBoostSteerCoeff;

        private void Reset()
        {
            engines = GetComponent<VehicleEngines3D>();
        }

        private void Awake()
        {
            if (engines == null) engines = GetComponent<VehicleEngines3D>();
            CacheFields();

            if (forceSingleActiveOnStart && meshRootA != null && meshRootB != null)
            {
                bool a = meshRootA.gameObject.activeSelf;
                bool b = meshRootB.gameObject.activeSelf;

                if (a && b) meshRootB.gameObject.SetActive(false);
                else if (!a && !b) meshRootA.gameObject.SetActive(true);
            }

            // Apply initial profile
            if (autoDetectFromActiveMesh)
            {
                int idx = DetectActiveIndex();
                if (idx != -1) ApplyIndex(idx);
            }
        }

        private void Update()
        {
            if (allowKeySwitch)
            {
                if (Input.GetKeyDown(keySelectA)) SetActiveCraftIndex(0, toggleMeshesWhenSwitching);
                if (Input.GetKeyDown(keySelectB)) SetActiveCraftIndex(1, toggleMeshesWhenSwitching);
            }

            if (autoDetectFromActiveMesh)
            {
                int idx = DetectActiveIndex();
                if (idx != -1 && idx != activeIndex)
                {
                    ApplyIndex(idx);
                }
            }
        }

        /// <summary>
        /// Call this from your swap script after you toggle meshes.
        /// idx: 0 = A, 1 = B
        /// </summary>
        public void SetActiveCraftIndex(int idx) => SetActiveCraftIndex(idx, toggleMeshesWhenSwitching);

        public void SetActiveCraftIndex(int idx, bool alsoToggleMeshes)
        {
            idx = (idx <= 0) ? 0 : 1;

            if (alsoToggleMeshes && meshRootA != null && meshRootB != null)
            {
                meshRootA.gameObject.SetActive(idx == 0);
                meshRootB.gameObject.SetActive(idx == 1);
            }

            ApplyIndex(idx);
        }

        private int DetectActiveIndex()
        {
            if (meshRootA == null || meshRootB == null) return -1;

            // Prefer whichever is active (if both are active, treat A as active after startup fix)
            if (meshRootB.gameObject.activeSelf) return 1;
            if (meshRootA.gameObject.activeSelf) return 0;

            return -1;
        }

        private void ApplyIndex(int idx)
        {
            activeIndex = idx;

            HandlingProfile p = (idx == 0) ? profileA : profileB;

            // Apply to VehicleEngines3D (protected fields)
            SetField(fMaxMovement, p.maxMovementForces);
            SetField(fMaxSteering, p.maxSteeringForces);
            SetField(fMaxBoost, p.maxBoostForces);
            SetField(fMoveResponse, p.movementInputResponseSpeed);
            SetField(fSteeringCurve, p.steeringBySpeedCurve);
            SetField(fBoostSteerCoeff, p.boostSteeringCoefficient);

            // Optional Rigidbody tweaks
            if (p.overrideRigidbody && engines != null && engines.Rigidbody != null)
            {
                Rigidbody rb = engines.Rigidbody;
                rb.useGravity = p.useGravity;
                rb.mass = p.mass;

                // Your project uses these (Unity versions that support linearDamping/angularDamping)
                rb.linearDamping = p.linearDamping;
                rb.angularDamping = p.angularDamping;
            }
        }

        private void CacheFields()
        {
            Type t = typeof(VehicleEngines3D);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;

            fMaxMovement = t.GetField("maxMovementForces", flags);
            fMaxSteering = t.GetField("maxSteeringForces", flags);
            fMaxBoost = t.GetField("maxBoostForces", flags);
            fMoveResponse = t.GetField("movementInputResponseSpeed", flags);
            fSteeringCurve = t.GetField("steeringBySpeedCurve", flags);
            fBoostSteerCoeff = t.GetField("boostSteeringCoefficient", flags);

            // If any are missing, you likely have a different VSX version/field names
            if (fMaxMovement == null || fMaxSteering == null || fMaxBoost == null ||
                fMoveResponse == null || fSteeringCurve == null || fBoostSteerCoeff == null)
            {
                Debug.LogWarning(
                    "[VehicleEngines3DProfileSwapper] Could not find one or more VehicleEngines3D fields. " +
                    "VSX version may differ. The swapper will not fully apply profiles."
                );
            }
        }

        private void SetField(FieldInfo field, object value)
        {
            if (field == null || engines == null) return;
            field.SetValue(engines, value);
        }
    }
}
