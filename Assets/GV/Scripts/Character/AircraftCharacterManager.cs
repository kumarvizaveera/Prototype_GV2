using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using TMPro;

namespace VSX.Engines3D
{
    [RequireComponent(typeof(VehicleEngines3DProfileSwapper))]
    public class AircraftCharacterManager : MonoBehaviour
    {
        [Serializable]
        public class CharacterEntry
        {
            public string name; // Friendly label for Inspector
            public CharacterData data;
            [Tooltip("Root GameObject containing the sprite and text for this character.")]
            public GameObject visualRoot;
            [Tooltip("Text component for stats (optional, usually inside visualRoot).")]
            public TMP_Text statsText;
        }

        [Header("Character Rosters")]
        [Tooltip("List of characters available for Aircraft A (Spaceship)")]
        public List<CharacterEntry> rosterA = new List<CharacterEntry>();
        
        [Tooltip("List of characters available for Aircraft B (Vimana)")]
        public List<CharacterEntry> rosterB = new List<CharacterEntry>();

        [Header("Controls")]
        public KeyCode switchCharacterKey = KeyCode.Alpha4;

        [Header("Fade Settings")]
        [Tooltip("How long the character stays visible before fading.")]
        public float visibleDuration = 3.0f;
        [Tooltip("How long the fade out takes.")]
        public float fadeDuration = 1.0f;

        private VehicleEngines3DProfileSwapper swapper;
        private VehicleEngines3D engines;
        private Coroutine activeFadeRoutine;

        // Track current sub-choice for each aircraft
        private int subIndexA = 0;
        private int subIndexB = 0;
        
        // Track which aircraft is currently active globally (0 or 1)
        private int currentAircraftIndex = -1;

        // Store base values to prevent compounding bonuses
        private Vector3 baseMaxMovement;
        private Vector3 baseMaxSteering;
        private Vector3 baseMaxBoost;

        // Reflection fields to modify engines
        private FieldInfo fMaxMovement;
        private FieldInfo fMaxSteering;
        private FieldInfo fMaxBoost;

        private void Awake()
        {
            swapper = GetComponent<VehicleEngines3DProfileSwapper>();
            engines = GetComponent<VehicleEngines3D>();

            // Ensure billboard script is on all roots
            foreach(var c in rosterA) AddBillboardIfNeeded(c.visualRoot);
            foreach(var c in rosterB) AddBillboardIfNeeded(c.visualRoot);

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

        private void Update()
        {
            if (Input.GetKeyDown(switchCharacterKey))
            {
                // Checkpoint Restriction
                if (CheckpointNetwork.Instance != null && !CheckpointNetwork.Instance.CanSwapCharacter) 
                    return;

                CycleCharacter();
            }
        }

        private void CycleCharacter()
        {
            if (currentAircraftIndex == 0)
            {
                if (rosterA.Count == 0) return;
                subIndexA = (subIndexA + 1) % rosterA.Count;
                RefreshActiveCharacter();
            }
            else if (currentAircraftIndex == 1)
            {
                if (rosterB.Count == 0) return;
                subIndexB = (subIndexB + 1) % rosterB.Count;
                RefreshActiveCharacter();
            }
        }

        // Called automatically when aircraft swaps
        private void OnProfileApplied(int index)
        {
            currentAircraftIndex = index;

            // Capture base engine values BEFORE applying any character bonuses
            // This happens right after the Profile Swapper has applied the raw profile data
            if (engines != null)
            {
                baseMaxMovement = (Vector3)fMaxMovement.GetValue(engines);
                baseMaxSteering = (Vector3)fMaxSteering.GetValue(engines);
                baseMaxBoost = (Vector3)fMaxBoost.GetValue(engines);
            }

            RefreshActiveCharacter();
        }

        private void RefreshActiveCharacter()
        {
            if (activeFadeRoutine != null) StopCoroutine(activeFadeRoutine);

            // 1. Disable ALL roots first to be safe
            DisableRosterRoots(rosterA);
            DisableRosterRoots(rosterB);

            // 2. Identify Active Character Entry
            CharacterEntry activeEntry = null;

            if (currentAircraftIndex == 0 && rosterA.Count > 0)
            {
                // Safety clamp
                if (subIndexA >= rosterA.Count) subIndexA = 0;
                activeEntry = rosterA[subIndexA];
            }
            else if (currentAircraftIndex == 1 && rosterB.Count > 0)
            {
                // Safety clamp
                if (subIndexB >= rosterB.Count) subIndexB = 0;
                activeEntry = rosterB[subIndexB];
            }

            // 3. Enable & Setup Active Entry
            if (activeEntry != null)
            {
                if (activeEntry.visualRoot != null)
                {
                    activeEntry.visualRoot.SetActive(true);
                    
                    // Reset Alpha
                    SetRootAlpha(activeEntry.visualRoot, 1f);

                    // Ensure billboard on text if separated
                    if (activeEntry.statsText != null)
                    {
                        activeEntry.statsText.gameObject.SetActive(true);
                        if (activeEntry.statsText.GetComponent<BillboardToCamera>() == null)
                            activeEntry.statsText.gameObject.AddComponent<BillboardToCamera>();
                        
                        UpdateBonusText(activeEntry.data, activeEntry.statsText);
                    }

                    // Start Fade
                    activeFadeRoutine = StartCoroutine(FadeRoutine(activeEntry.visualRoot));
                }

                // Apply Bonuses
                if (activeEntry.data != null && engines != null)
                {
                    ApplyBonuses(activeEntry.data);
                }
            }
        }

        private void DisableRosterRoots(List<CharacterEntry> roster)
        {
            foreach (var entry in roster)
            {
                if (entry.visualRoot != null) entry.visualRoot.SetActive(false);
                if (entry.statsText != null) entry.statsText.gameObject.SetActive(false);
            }
        }

        private IEnumerator FadeRoutine(GameObject root)
        {
            yield return new WaitForSeconds(visibleDuration);

            float timer = 0f;
            while (timer < fadeDuration)
            {
                timer += Time.deltaTime;
                float alpha = Mathf.Lerp(1f, 0f, timer / fadeDuration);
                SetRootAlpha(root, alpha);
                yield return null;
            }

            SetRootAlpha(root, 0f);
            root.SetActive(false);
        }

        private void SetRootAlpha(GameObject root, float alpha)
        {
            if (root == null) return;

            // 1. SpriteRenderers
            var sprites = root.GetComponentsInChildren<SpriteRenderer>(true);
            foreach (var s in sprites)
            {
                Color c = s.color;
                c.a = alpha;
                s.color = c;
            }

            // 2. UI Graphics
            var graphics = root.GetComponentsInChildren<UnityEngine.UI.Graphic>(true);
            foreach (var g in graphics)
            {
                Color c = g.color;
                c.a = alpha;
                g.color = c;
            }

            // 3. Standard Renderers
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            foreach (var r in renderers)
            {
                if (r is SpriteRenderer) continue;
                Material mat = r.material;
                if (mat.HasProperty("_Color"))
                {
                    Color c = mat.color;
                    c.a = alpha;
                    mat.color = c;
                }
                else if (mat.HasProperty("_BaseColor"))
                {
                    Color c = mat.GetColor("_BaseColor");
                    c.a = alpha;
                    mat.SetColor("_BaseColor", c);
                }
            }

            // 4. TMP Text (if inside root)
            var tmps = root.GetComponentsInChildren<TMP_Text>(true);
            foreach (var t in tmps) t.alpha = alpha;
        }

        private void UpdateBonusText(CharacterData data, TMP_Text textComp)
        {
            if (data == null) return;
            
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

        public void RefreshStats()
        {
            RefreshActiveCharacter();
        }

        private void ApplyBonuses(CharacterData data)
        {
            // Check for Artifact Manager
            var artifactManager = GetComponent<AircraftArtifactManager>();
            float artSpeed = 1f;
            float artSteer = 1f;
            float artBoost = 1f;

            if (artifactManager != null)
            {
                var multipliers = artifactManager.GetTotalMultipliers();
                artSpeed = multipliers.speed;
                artSteer = multipliers.steering;
                artBoost = multipliers.boost;
            }

            // Formula: Base * Character * Artifacts
            fMaxMovement.SetValue(engines, baseMaxMovement * data.speedMultiplier * artSpeed);
            fMaxSteering.SetValue(engines, baseMaxSteering * data.steeringMultiplier * artSteer);
            fMaxBoost.SetValue(engines, baseMaxBoost * data.boostMultiplier * artBoost);

            Debug.Log($"[CharacterManager] Applied {data.characterName} + Artifacts. " +
                      $"Multipliers: Spd x{data.speedMultiplier * artSpeed:F2}, " +
                      $"Str x{data.steeringMultiplier * artSteer:F2}, " +
                      $"Bst x{data.boostMultiplier * artBoost:F2}");
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
