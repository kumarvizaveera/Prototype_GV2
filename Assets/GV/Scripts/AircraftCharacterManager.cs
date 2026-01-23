using System;
using System.Reflection;
using UnityEngine;
using TMPro;

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

        [Header("Bonus UI (Optional)")]
        [Tooltip("TMP Text component for Aircraft A to show stats")]
        public TMP_Text statsTextA;
        [Tooltip("TMP Text component for Aircraft B to show stats")]
        public TMP_Text statsTextB;

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
            TMP_Text activeText = (index == 0) ? statsTextA : statsTextB;
            TMP_Text otherText = (index == 0) ? statsTextB : statsTextA;

            // 1. Update Visuals
            if (activeSprite != null) activeSprite.SetActive(true);
            if (otherSprite != null) otherSprite.SetActive(false);

            if (activeText != null)
            {
                activeText.gameObject.SetActive(true);
                // Also billboard the text if it's separate (optional, but robust)
                if (activeText.GetComponent<BillboardToCamera>() == null)
                    activeText.gameObject.AddComponent<BillboardToCamera>();
            }
            if (otherText != null) otherText.gameObject.SetActive(false);


            // 2. Apply Bonuses & Update Text
            if (activeChar != null)
            {
                if (engines != null) ApplyBonuses(activeChar);
                if (activeText != null) UpdateBonusText(activeChar, activeText);
            }
        }

        private void UpdateBonusText(CharacterData data, TMP_Text textComp)
        {
            // Format: "+10% Spd | +5% Str | +5% Bst"
            // Or simpler if all are same: "+10% All" (but we show full details)
            
            int spdBonus = Mathf.RoundToInt((data.speedMultiplier - 1f) * 100f);
            int strBonus = Mathf.RoundToInt((data.steeringMultiplier - 1f) * 100f);
            int bstBonus = Mathf.RoundToInt((data.boostMultiplier - 1f) * 100f);

            string text = "";
            if (spdBonus != 0) text += $"+{spdBonus}% Speed\n";
            if (strBonus != 0) text += $"+{strBonus}% Handling\n";
            if (bstBonus != 0) text += $"+{bstBonus}% Boost";

            if (string.IsNullOrEmpty(text)) text = "Base Stats";

            textComp.text = text;
        }

        private void ApplyBonuses(CharacterData data)
        {
            // Read current values (which were just set by swapper)
            Vector3 currentMove = (Vector3)fMaxMovement.GetValue(engines);
            Vector3 currentSteer = (Vector3)fMaxSteering.GetValue(engines);
            Vector3 currentBoost = (Vector3)fMaxBoost.GetValue(engines);

            fMaxMovement.SetValue(engines, currentMove * data.speedMultiplier);
            fMaxSteering.SetValue(engines, currentSteer * data.steeringMultiplier);
            fMaxBoost.SetValue(engines, currentBoost * data.boostMultiplier);

            Debug.Log($"[CharacterManager] Applied {data.characterName} bonuses.");
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
