using System;
using System.Reflection;
using UnityEngine;

namespace VSX.Engines3D
{
    [RequireComponent(typeof(VehicleEngines3DProfileSwapper))]
    public class AircraftCharacterManager : MonoBehaviour
    {
        [Header("Character Configuration")]
        [Tooltip("Character for Aircraft A (Spaceship)")]
        public CharacterData characterA;
        [Tooltip("Character for Aircraft B (Vimana)")]
        public CharacterData characterB;

        [Header("Visuals (Existing Sprites)")]
        [Tooltip("Existing sprite GameObject for Aircraft A")]
        public GameObject spriteObjectA;
        [Tooltip("Existing sprite GameObject for Aircraft B")]
        public GameObject spriteObjectB;

        private VehicleEngines3DProfileSwapper swapper;
        private VehicleEngines3D engines;

        // Reflection fields to modify engines
        private FieldInfo fMaxMovement;
        private FieldInfo fMaxSteering;
        private FieldInfo fMaxBoost;

        private void Awake()
        {
            swapper = GetComponent<VehicleEngines3DProfileSwapper>();
            engines = GetComponent<VehicleEngines3D>();

            // Ensure billboard script is on sprites if they exist
            AddBillboardIfNeeded(spriteObjectA);
            AddBillboardIfNeeded(spriteObjectB);

            CacheFields();
        }

        private void AddBillboardIfNeeded(GameObject obj)
        {
            if (obj != null && obj.GetComponent<BillboardToCamera>() == null)
            {
                obj.AddComponent<BillboardToCamera>();
            }
        }

        private void OnEnable()
        {
            if (swapper != null) swapper.onProfileApplied += OnProfileApplied;
        }

        private void OnDisable()
        {
            if (swapper != null) swapper.onProfileApplied -= OnProfileApplied;
        }

        private void Start()
        {
            // Initial apply if swapper already ran
            if (swapper != null && swapper.ActiveIndex != -1)
            {
                OnProfileApplied(swapper.ActiveIndex);
            }
        }

        private void OnProfileApplied(int index)
        {
            CharacterData activeChar = (index == 0) ? characterA : characterB;
            GameObject activeSprite = (index == 0) ? spriteObjectA : spriteObjectB;
            GameObject otherSprite = (index == 0) ? spriteObjectB : spriteObjectA;

            // 1. Update Visuals
            if (activeSprite != null) activeSprite.SetActive(true);
            if (otherSprite != null) otherSprite.SetActive(false);

            // 2. Apply Bonuses
            if (activeChar != null && engines != null)
            {
                ApplyBonuses(activeChar);
            }
        }

        private void ApplyBonuses(CharacterData data)
        {
            // Read current values (which were just set by swapper)
            Vector3 currentMove = (Vector3)fMaxMovement.GetValue(engines);
            Vector3 currentSteer = (Vector3)fMaxSteering.GetValue(engines);
            Vector3 currentBoost = (Vector3)fMaxBoost.GetValue(engines);

            // Apply multiplier
            // Using MAX of 1 to ensure we don't accidentally reduce if multiplier is 0 (unless intended, but assuming multiplier is 1.x)
            // User said "10% improvement", so multiplier should be 1.1. 
            // If they put 0, it would zero it out. I'll assume they know what they are doing.
            
            fMaxMovement.SetValue(engines, currentMove * data.speedMultiplier);
            fMaxSteering.SetValue(engines, currentSteer * data.steeringMultiplier);
            fMaxBoost.SetValue(engines, currentBoost * data.boostMultiplier);

            Debug.Log($"[CharacterManager] Applied {data.characterName} bonuses. " +
                      $"Move: {currentMove}->{fMaxMovement.GetValue(engines)} | " +
                      $"Steer: {currentSteer}->{fMaxSteering.GetValue(engines)} | " +
                      $"Boost: {currentBoost}->{fMaxBoost.GetValue(engines)}");
        }

        private void CacheFields()
        {
            Type t = typeof(VehicleEngines3D);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;

            fMaxMovement = t.GetField("maxMovementForces", flags);
            fMaxSteering = t.GetField("maxSteeringForces", flags);
            fMaxBoost = t.GetField("maxBoostForces", flags);

            if (fMaxMovement == null || fMaxSteering == null || fMaxBoost == null)
            {
                Debug.LogError("[AircraftCharacterManager] Failed to reflect VehicleEngines3D fields!");
            }
        }
    }
}
