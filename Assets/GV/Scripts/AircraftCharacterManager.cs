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

        [Header("Visuals (Existing Roots)")]
        [Tooltip("Root GameObject for Aircraft A (Atom Rider)")]
        public GameObject spriteObjectA;
        [Tooltip("Root GameObject for Aircraft B (Sarathi)")]
        public GameObject spriteObjectB;

        [Header("Bonus UI (Optional)")]
        [Tooltip("TMP Text component for Aircraft A to show stats")]
        public TMP_Text statsTextA;
        [Tooltip("TMP Text component for Aircraft B to show stats")]
        public TMP_Text statsTextB;

        [Header("Fade Settings")]
        [Tooltip("How long the character stays visible before fading.")]
        public float visibleDuration = 3.0f;
        [Tooltip("How long the fade out takes.")]
        public float fadeDuration = 1.0f;

        private VehicleEngines3DProfileSwapper swapper;
        private VehicleEngines3D engines;
        private Coroutine activeFadeRoutine;

        // Reflection fields to modify engines
        private FieldInfo fMaxMovement;
        private FieldInfo fMaxSteering;
        private FieldInfo fMaxBoost;

        private void Awake()
        {
            swapper = GetComponent<VehicleEngines3DProfileSwapper>();
            engines = GetComponent<VehicleEngines3D>();

            // Ensure billboard script is on roots if they exist
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
            if (activeFadeRoutine != null) StopCoroutine(activeFadeRoutine);

            CharacterData activeChar = (index == 0) ? characterA : characterB;
            GameObject activeRoot = (index == 0) ? spriteObjectA : spriteObjectB;
            GameObject otherRoot = (index == 0) ? spriteObjectB : spriteObjectA;
            TMP_Text activeText = (index == 0) ? statsTextA : statsTextB;

            // 1. Disable the OTHER root immediately
            if (otherRoot != null) otherRoot.SetActive(false);

            // 2. Setup ACTIVE root
            if (activeRoot != null)
            {
                activeRoot.SetActive(true);
                
                // Reset Alpha to 1 first (in case it was mid-fade)
                SetRootAlpha(activeRoot, 1f);

                // Billboard text if needed
                if (activeText != null)
                {
                    if (activeText.GetComponent<BillboardToCamera>() == null)
                        activeText.gameObject.AddComponent<BillboardToCamera>();
                    
                    // Update the text content
                    if (activeChar != null) UpdateBonusText(activeChar, activeText);
                }

                // Start the wait & fade routine
                activeFadeRoutine = StartCoroutine(FadeRoutine(activeRoot));
            }

            // 3. Apply Bonuses
            if (activeChar != null && engines != null)
            {
                ApplyBonuses(activeChar);
            }
        }

        private System.Collections.IEnumerator FadeRoutine(GameObject root)
        {
            // Wait
            yield return new WaitForSeconds(visibleDuration);

            // Fade Out
            float timer = 0f;
            while (timer < fadeDuration)
            {
                timer += Time.deltaTime;
                float alpha = Mathf.Lerp(1f, 0f, timer / fadeDuration);
                SetRootAlpha(root, alpha);
                yield return null;
            }

            // Ensure fully invisible and disable
            SetRootAlpha(root, 0f);
            root.SetActive(false);
        }

        private void SetRootAlpha(GameObject root, float alpha)
        {
            if (root == null) return;

            // 1. Handle SpriteRenderers (most likely for 3D world sprites)
            SpriteRenderer[] sprites = root.GetComponentsInChildren<SpriteRenderer>(true);
            foreach (var s in sprites)
            {
                Color c = s.color;
                c.a = alpha;
                s.color = c;
            }

            // 2. Handle UI Graphics (Image, RawImage) - for World Space Canvas
            UnityEngine.UI.Graphic[] graphics = root.GetComponentsInChildren<UnityEngine.UI.Graphic>(true);
            foreach (var g in graphics)
            {
                Color c = g.color;
                c.a = alpha;
                g.color = c;
            }

            // 3. Handle Standard Renderers (MeshRenderer) - fallback
            // Note: This only works if the shader supports transparency and exposes "_Color" or "_BaseColor"
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            foreach (var r in renderers)
            {
                if (r is SpriteRenderer) continue; // Already handled above

                Material mat = r.material;
                if (mat.HasProperty("_Color"))
                {
                    Color c = mat.color;
                    c.a = alpha;
                    mat.color = c;
                }
                else if (mat.HasProperty("_BaseColor")) // URP/HDRP often use BaseColor
                {
                    Color c = mat.GetColor("_BaseColor");
                    c.a = alpha;
                    mat.SetColor("_BaseColor", c);
                }
            }

            // 4. Handle TMP Text (handled separately as it often has its own property)
            TMP_Text[] tmps = root.GetComponentsInChildren<TMP_Text>(true);
            foreach (var t in tmps)
            {
                t.alpha = alpha;
            }
        }

        private void UpdateBonusText(CharacterData data, TMP_Text textComp)
        {
            // Format: "+10% Speed | +5% Handling | +5% Boost"
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
