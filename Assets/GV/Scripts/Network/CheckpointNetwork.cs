using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum CheckpointPlacementMode
{
    Spline,
    Dispersed
}

[DefaultExecutionOrder(-1000)]
public class CheckpointNetwork : MonoBehaviour
{
    public static CheckpointNetwork Instance { get; private set; }

    [Header("Placement Mode")]
    [Tooltip("Spline = use existing child transforms. Dispersed = randomly scatter inside the BattleZone sphere.")]
    public CheckpointPlacementMode placementMode = CheckpointPlacementMode.Spline;

    [Header("Source (optional manual assign)")]
    public Transform checkpointsParent;

    public string autoFindParentName = "CheckPoints Parent";

    [Header("Dispersed Settings")]
    [Tooltip("Number of checkpoints to generate in Dispersed mode.")]
    public int dispersedCount = 108;
    [Tooltip("Minimum distance between any two dispersed checkpoints.")]
    public float dispersedMinSpacing = 10f;
    [Tooltip("Collider radius for each dispersed checkpoint trigger.")]
    public float dispersedColliderRadius = 5f;
    [Tooltip("Seed for deterministic placement. Use the SAME seed across all clients for multiplayer. 0 = random each time (single-player only).")]
    public int dispersedSeed = 42;

    [Header("BattleZone Sphere Reference")]
    [Tooltip("Reference to the BattleZoneController. If null, auto-finds in scene.")]
    public GV.Network.BattleZoneController battleZone;

    [Header("Turret Prefabs (Dispersed Mode)")]
    [Tooltip("Turret prefabs to instantiate at each dispersed checkpoint. One is randomly chosen per checkpoint (seeded). Leave empty for no turret spawning.")]
    public List<GameObject> turretPrefabs = new List<GameObject>();
    [Tooltip("Offset applied to the turret position relative to the checkpoint.")]
    public Vector3 turretPositionOffset = Vector3.zero;
    [Tooltip("If true, turrets face a random Y rotation. If false, they use identity rotation.")]
    public bool randomTurretYRotation = true;

    [Header("Gizmos")]
    [Tooltip("Show debug spheres at each dispersed checkpoint in Scene view.")]
    public bool showDebugGizmos = true;
    [Tooltip("Color of the debug gizmos.")]
    public Color gizmoColor = new Color(0f, 1f, 0.5f, 0.7f);

    [Header("Indexing")]
    public bool oneBased = true;     // 1..N
    public bool wrapAround = false;  // wrap or clamp

    [Header("Auto Build")]
    public bool autoBuild = true;
    public int buildAfterFrames = 2;

    readonly List<Transform> _checkpoints = new List<Transform>();
    public int Count => _checkpoints.Count;

    [Header("Calibration")]
    [Tooltip("Add/Subtract to shift all indices. E.g. -1 if triggers are 1 too late.")]
    public int indexOffset = 0;

    // ─── Internal ─────────────────────────────────────────────────────
    readonly List<Transform> _splineChildren = new List<Transform>();
    readonly List<GameObject> _dispersedObjects = new List<GameObject>();
    Transform _dispersedParent;

    // ─── Sphere-based proportional repositioning ──────────────────────
    // Each checkpoint stores its position as a normalized offset from the sphere center.
    // normalizedOffset = direction * (distance / initialRadius)
    // Every frame: worldPos = sphereCenter + normalizedOffset * currentRadius
    readonly List<Vector3> _normalizedOffsets = new List<Vector3>();
    float _initialSphereRadius;

    // ─── Fusion awareness ─────────────────────────────────────────────
    bool _fusionWasConnected = false;
    bool _turretsNeedRebuildAfterFusion = false;

    void Awake()
    {
        if (Instance == null) Instance = this;
        TryAutoAssignParent();
        CacheSplineChildren();
        Build();
    }

    void Start()
    {
        if (autoBuild) StartCoroutine(BuildAfterSpawn());
    }

    void TryAutoAssignParent()
    {
        if (checkpointsParent) return;
        if (string.IsNullOrEmpty(autoFindParentName)) return;

        var t = transform.Find(autoFindParentName);
        if (t) checkpointsParent = t;
    }

    void CacheSplineChildren()
    {
        _splineChildren.Clear();
        if (!checkpointsParent) return;

        foreach (Transform child in checkpointsParent)
            _splineChildren.Add(child);

        _splineChildren.Sort((a, b) => a.GetSiblingIndex().CompareTo(b.GetSiblingIndex()));
    }

    IEnumerator BuildAfterSpawn()
    {
        for (int i = 0; i < Mathf.Max(0, buildAfterFrames); i++)
            yield return null;

        TryAutoAssignParent();

        if (_splineChildren.Count == 0)
            CacheSplineChildren();

        Build();
    }

    // ═══════════════════════════════════════════════════════════════════
    //  FUSION-AWARE REBUILD
    // ═══════════════════════════════════════════════════════════════════

    void LateUpdate()
    {
        if (placementMode != CheckpointPlacementMode.Dispersed) return;

        // ── Detect Fusion connection and rebuild ──
        bool fusionConnected = IsFusionConnected();

        if (fusionConnected && !_fusionWasConnected)
        {
            Debug.Log("[CheckpointNetwork] Fusion connection detected — scheduling turret rebuild after scene registration...");
            _turretsNeedRebuildAfterFusion = true;
            StartCoroutine(RebuildAfterFusionSceneRegistration());
        }

        _fusionWasConnected = fusionConnected;

        // ── Reposition checkpoints to follow shrinking BattleZone sphere ──
        RepositionCheckpointsToSphere();
    }

    /// <summary>
    /// Every frame, reposition all dispersed checkpoints proportionally
    /// based on the BattleZone sphere's current radius and center.
    /// </summary>
    void RepositionCheckpointsToSphere()
    {
        if (_normalizedOffsets.Count == 0) return;
        if (_dispersedObjects.Count == 0) return;

        // Find BattleZone if not assigned
        if (battleZone == null)
            battleZone = FindFirstObjectByType<GV.Network.BattleZoneController>();
        if (battleZone == null) return;

        float currentRadius = GetBattleZoneRadius();
        Vector3 sphereCenter = battleZone.transform.position;

        for (int i = 0; i < _dispersedObjects.Count; i++)
        {
            if (_dispersedObjects[i] == null) continue;
            if (i >= _normalizedOffsets.Count) break;

            // World position = sphere center + normalized offset scaled by current radius
            Vector3 newPos = sphereCenter + _normalizedOffsets[i] * currentRadius;
            _dispersedObjects[i].transform.position = newPos;
        }
    }

    /// <summary>
    /// Safely reads the BattleZone's current radius.
    /// CurrentRadius is a [Networked] property that throws before Fusion Spawned().
    /// Falls back to InitialRadius (plain serialized field, always safe).
    /// </summary>
    float GetBattleZoneRadius()
    {
        if (battleZone == null) return 500f;

        // Try the networked property first (only valid after Fusion Spawned)
        try
        {
            float r = battleZone.CurrentRadius;
            if (r > 0f) return r;
        }
        catch { /* Networked state not yet allocated — fall through */ }

        // Fallback: read the serialized initialRadius (always safe)
        return battleZone.InitialRadius;
    }

    bool IsFusionConnected()
    {
        var nm = GV.Network.NetworkManager.Instance;
        return nm != null && nm.IsConnected;
    }

    IEnumerator RebuildAfterFusionSceneRegistration()
    {
        for (int i = 0; i < 5; i++)
            yield return null;

        if (!_turretsNeedRebuildAfterFusion) yield break;
        _turretsNeedRebuildAfterFusion = false;

        Debug.Log("[CheckpointNetwork] Rebuilding dispersed checkpoints + turrets AFTER Fusion scene registration...");
        Build();

        ForceEnableAllDispersedObjects();
        Debug.Log($"[CheckpointNetwork] Post-Fusion rebuild complete — {_checkpoints.Count} checkpoints, turrets force-enabled");
    }

    void ForceEnableAllDispersedObjects()
    {
        if (_dispersedParent != null)
            _dispersedParent.gameObject.SetActive(true);

        foreach (var go in _dispersedObjects)
        {
            if (go == null) continue;
            go.SetActive(true);

            foreach (Transform child in go.GetComponentsInChildren<Transform>(true))
                child.gameObject.SetActive(true);

            foreach (var wc in go.GetComponentsInChildren<VSX.Weapons.WeaponController>(true))
            {
                wc.enabled = true;
                wc.Activated = true;
            }

            foreach (var w in go.GetComponentsInChildren<VSX.Weapons.Weapon>(true))
                w.enabled = true;
        }
    }

    public void ForceRebuild()
    {
        Debug.Log("[CheckpointNetwork] ForceRebuild() called externally");
        Build();
        if (placementMode == CheckpointPlacementMode.Dispersed)
            ForceEnableAllDispersedObjects();
    }

    // ═══════════════════════════════════════════════════════════════════
    //  BUILD
    // ═══════════════════════════════════════════════════════════════════

    [ContextMenu("Build / Rebuild")]
    public void Build()
    {
        _checkpoints.Clear();
        _normalizedOffsets.Clear();

        if (placementMode == CheckpointPlacementMode.Dispersed)
        {
            SetSplineChildrenActive(false);
            BuildDispersed();
        }
        else
        {
            CleanUpDispersed();
            SetSplineChildrenActive(true);
            BuildSpline();
        }

        ConfigureRaceCheckpoints();
        Debug.Log($"[CheckpointNetwork] Build complete — mode={placementMode}, count={_checkpoints.Count}");
    }

    // ─── Spline ───────────────────────────────────────────────────────

    void BuildSpline()
    {
        foreach (var child in _splineChildren)
        {
            if (child != null && child.gameObject.activeSelf)
                _checkpoints.Add(child);
        }
    }

    // ─── Dispersed (BattleZone Sphere) ────────────────────────────────

    void BuildDispersed()
    {
        CleanUpDispersed();

        // Separate parent for dispersed objects
        if (_dispersedParent == null)
        {
            var go = new GameObject("__DispersedCheckpoints__");
            go.transform.SetParent(transform);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            _dispersedParent = go.transform;
        }

        // Physics layer from existing checkpoints
        int targetLayer = 0;
        if (_splineChildren.Count > 0 && _splineChildren[0] != null)
            targetLayer = _splineChildren[0].gameObject.layer;
        else if (checkpointsParent != null)
            targetLayer = checkpointsParent.gameObject.layer;

        // ── Get sphere center and radius from BattleZoneController ──
        if (battleZone == null)
            battleZone = FindFirstObjectByType<GV.Network.BattleZoneController>();

        Vector3 sphereCenter;
        float sphereRadius;

        if (battleZone != null)
        {
            sphereCenter = battleZone.transform.position;
            // Use InitialRadius (always safe, reads serialized field directly).
            // CurrentRadius is [Networked] and throws before Fusion Spawned().
            sphereRadius = battleZone.InitialRadius;
            Debug.Log($"[CheckpointNetwork] Using BattleZone sphere → center={sphereCenter}, radius={sphereRadius}");
        }
        else
        {
            // Fallback: use transform position with a default radius
            sphereCenter = transform.position;
            sphereRadius = 500f;
            Debug.LogWarning("[CheckpointNetwork] No BattleZoneController found! Using fallback sphere at transform.position with radius 500.");
        }

        _initialSphereRadius = sphereRadius;

        // ═══ NETWORK-SAFE DETERMINISTIC RNG ═══
        int seed = dispersedSeed != 0 ? dispersedSeed : 42;
        System.Random rng = new System.Random(seed);

        // ── Generate positions inside the sphere ──
        List<Vector3> positions = new List<Vector3>(dispersedCount);
        List<Vector3> normalizedOffsets = new List<Vector3>(dispersedCount);
        float minSqr = dispersedMinSpacing * dispersedMinSpacing;
        int maxAttempts = dispersedCount * 200;
        int attempts = 0;

        while (positions.Count < dispersedCount && attempts < maxAttempts)
        {
            attempts++;

            // Generate a random point inside a unit sphere using rejection sampling
            Vector3 unitPoint;
            do
            {
                unitPoint = new Vector3(
                    RngRange(rng, -1f, 1f),
                    RngRange(rng, -1f, 1f),
                    RngRange(rng, -1f, 1f)
                );
            }
            while (unitPoint.sqrMagnitude > 1f); // reject points outside unit sphere

            // Scale to actual sphere
            Vector3 candidate = sphereCenter + unitPoint * sphereRadius;

            bool tooClose = false;
            for (int i = 0; i < positions.Count; i++)
            {
                if ((candidate - positions[i]).sqrMagnitude < minSqr)
                {
                    tooClose = true;
                    break;
                }
            }
            if (tooClose) continue;

            positions.Add(candidate);
            normalizedOffsets.Add(unitPoint); // Store the unit-sphere offset (0..1 magnitude)
        }

        // ── Create checkpoint GameObjects and spawn turrets ──
        bool hasTurrets = turretPrefabs != null && turretPrefabs.Count > 0;

        for (int i = 0; i < positions.Count; i++)
        {
            var cpGo = new GameObject($"DispersedCP_{i}");
            cpGo.transform.SetParent(_dispersedParent);
            cpGo.transform.position = positions[i];
            cpGo.layer = targetLayer;

            var col = cpGo.AddComponent<SphereCollider>();
            col.isTrigger = true;
            col.radius = dispersedColliderRadius;

            // Spawn a random turret prefab at this checkpoint (seeded, deterministic)
            if (hasTurrets)
            {
                int turretIndex = rng.Next(0, turretPrefabs.Count);
                GameObject prefab = turretPrefabs[turretIndex];

                if (prefab != null)
                {
                    Quaternion rot = randomTurretYRotation
                        ? Quaternion.Euler(0f, RngRange(rng, 0f, 360f), 0f)
                        : Quaternion.identity;

                    var turret = Instantiate(prefab, positions[i] + turretPositionOffset, rot, cpGo.transform);
                    turret.name = $"Turret_{prefab.name}_{i}";

                    // Force-enable the turret and all children (prefabs may have root disabled)
                    turret.SetActive(true);
                    foreach (Transform child in turret.GetComponentsInChildren<Transform>(true))
                    {
                        child.gameObject.SetActive(true);
                    }

                    // NOTE: We do NOT force-enable renderers — respect the prefab's
                    // original renderer states so meshes toggled off in the prefab stay off.

                    // Prevent turrets from targeting each other
                    foreach (var trackable in turret.GetComponentsInChildren<VSX.RadarSystem.Trackable>(true))
                    {
                        trackable.enabled = false;
                    }

                    // Activate WeaponControllers (turret firing logic)
                    foreach (var wc in turret.GetComponentsInChildren<VSX.Weapons.WeaponController>(true))
                    {
                        wc.enabled = true;
                        wc.Activated = true;
                    }

                    // Activate Weapons
                    foreach (var w in turret.GetComponentsInChildren<VSX.Weapons.Weapon>(true))
                    {
                        w.enabled = true;
                    }

                    Debug.Log($"[CheckpointNetwork] Spawned turret '{prefab.name}' at CP {i}, " +
                              $"renderers={turret.GetComponentsInChildren<Renderer>(true).Length}, " +
                              $"weaponControllers={turret.GetComponentsInChildren<VSX.Weapons.WeaponController>(true).Length}");
                }
            }

            _dispersedObjects.Add(cpGo);
            _checkpoints.Add(cpGo.transform);
            _normalizedOffsets.Add(normalizedOffsets[i]);
        }

        Debug.Log($"[CheckpointNetwork] Dispersed inside BattleZone sphere: placed {positions.Count}/{dispersedCount} " +
                  $"(seed={seed}, center={sphereCenter}, radius={sphereRadius}, turrets={hasTurrets})");

        if (positions.Count < dispersedCount)
        {
            Debug.LogWarning($"[CheckpointNetwork] Only placed {positions.Count}/{dispersedCount} " +
                             $"dispersed checkpoints (minSpacing={dispersedMinSpacing}). " +
                             "Reduce minSpacing or wait for a larger sphere radius.");
        }
    }

    // ─── Network-safe RNG helper ─────────────────────────────────────
    static float RngRange(System.Random rng, float min, float max)
    {
        return min + (float)(rng.NextDouble() * (max - min));
    }

    void CleanUpDispersed()
    {
        foreach (var go in _dispersedObjects)
        {
            if (go != null)
            {
#if UNITY_EDITOR
                if (!Application.isPlaying) DestroyImmediate(go);
                else
#endif
                Destroy(go);
            }
        }
        _dispersedObjects.Clear();
        _normalizedOffsets.Clear();

        if (_dispersedParent != null)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) DestroyImmediate(_dispersedParent.gameObject);
            else
#endif
            Destroy(_dispersedParent.gameObject);
            _dispersedParent = null;
        }
    }

    void SetSplineChildrenActive(bool active)
    {
        foreach (var child in _splineChildren)
        {
            if (child != null)
                child.gameObject.SetActive(active);
        }
    }

    void ConfigureRaceCheckpoints()
    {
        for (int i = 0; i < _checkpoints.Count; i++)
        {
            var cp = _checkpoints[i];
            var raceCp = cp.GetComponent<GV.Race.RaceCheckpoint>();
            if (raceCp == null)
                raceCp = cp.gameObject.AddComponent<GV.Race.RaceCheckpoint>();
            raceCp.checkoutPointIndex = i;
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  INDEX HELPERS (unchanged)
    // ═══════════════════════════════════════════════════════════════════

    int ToZeroBased(int index) => (oneBased ? index - 1 : index) + indexOffset;
    int ToIndex(int zeroBased) => (oneBased ? zeroBased + 1 : zeroBased) - indexOffset;

    public Transform GetCheckpoint(int index)
    {
        if (_checkpoints.Count == 0) return null;

        int zb = ToZeroBased(index);
        if (wrapAround) zb = Mod(zb, _checkpoints.Count);
        else zb = Mathf.Clamp(zb, 0, _checkpoints.Count - 1);

        return _checkpoints[zb];
    }

    public int ClampOrWrapIndex(int index)
    {
        if (_checkpoints.Count == 0) return index;

        int zb = ToZeroBased(index);
        if (wrapAround) zb = Mod(zb, _checkpoints.Count);
        else zb = Mathf.Clamp(zb, 0, _checkpoints.Count - 1);

        return ToIndex(zb);
    }

    public int GetPrevIndex(int index)
    {
        if (_checkpoints.Count == 0) return index;

        int zb = ToZeroBased(index);
        int prev = zb - 1;

        if (wrapAround) prev = Mod(prev, _checkpoints.Count);
        else prev = Mathf.Clamp(prev, 0, _checkpoints.Count - 1);

        return ToIndex(prev);
    }

    public int GetNextIndex(int index)
    {
        if (_checkpoints.Count == 0) return index;

        int zb = ToZeroBased(index);
        int next = zb + 1;

        if (wrapAround) next = Mod(next, _checkpoints.Count);
        else next = Mathf.Clamp(next, 0, _checkpoints.Count - 1);

        return ToIndex(next);
    }

    public int GetNearestIndex(Vector3 worldPos)
    {
        if (_checkpoints.Count == 0) return oneBased ? 1 : 0;

        int best = 0;
        float bestDist = float.MaxValue;

        for (int i = 0; i < _checkpoints.Count; i++)
        {
            if (_checkpoints[i] == null) continue;

            float d = (worldPos - _checkpoints[i].position).sqrMagnitude;
            if (d < bestDist)
            {
                bestDist = d;
                best = i;
            }
        }

        return ToIndex(best);
    }

    public Vector3 GetPositionBehindOnPath(int index, float backDistance, out Vector3 forwardDir)
    {
        forwardDir = Vector3.forward;

        if (_checkpoints.Count == 0)
            return Vector3.zero;

        index = ClampOrWrapIndex(index);

        Transform currT = GetCheckpoint(index);
        if (!currT) return Vector3.zero;

        int prevIndex0 = GetPrevIndex(index);
        Transform prevT0 = GetCheckpoint(prevIndex0);
        if (prevT0 && prevT0 != currT)
        {
            Vector3 d0 = (currT.position - prevT0.position);
            if (d0.sqrMagnitude > 0.000001f) forwardDir = d0.normalized;
        }

        if (backDistance <= 0f)
            return currT.position;

        float remaining = backDistance;
        Vector3 currPos = currT.position;
        int currIndex = index;

        int safety = 0;
        while (remaining > 0f && safety < _checkpoints.Count)
        {
            int prevIndex = GetPrevIndex(currIndex);
            if (!wrapAround && prevIndex == currIndex) break;

            Transform prevT = GetCheckpoint(prevIndex);
            if (!prevT) break;

            Vector3 prevPos = prevT.position;
            float segLen = Vector3.Distance(prevPos, currPos);
            if (segLen < 0.0001f) break;

            Vector3 dir = (currPos - prevPos).normalized;
            forwardDir = dir;

            if (remaining <= segLen)
                return currPos - dir * remaining;

            remaining -= segLen;
            currPos = prevPos;
            currIndex = prevIndex;
            safety++;
        }

        return currPos;
    }

    static int Mod(int x, int m) => (x % m + m) % m;

    // ═══════════════════════════════════════════════════════════════════
    //  SWAP ZONE LOGIC (unchanged)
    // ═══════════════════════════════════════════════════════════════════

    [Header("Common Settings")]
    [Tooltip("If true, swapping is restricted to checkpoints. If false, swapping is always allowed.")]
    public bool enableAircraftCheckpointSwap = true;
    public bool enableCharacterCheckpointSwap = true;

    [Tooltip("How long (in seconds) the swap remains active after reaching/leaving the checkpoint.")]
    public float swapDuration = 10f;
    [Tooltip("Auto-assigned to tag 'Player' if empty.")]
    public Transform playerTransform;

    [Header("Aircraft Settings")]
    public List<int> aircraftSwapIndices = new List<int> { 18, 54, 90 };
    public float aircraftSwapRadius = 30f;
    public float aircraftUiRadius = 60f;

    [Header("Aircraft UI")]
    public TMPro.TMP_Text aircraftStatusText;
    [TextArea] public string aircraftMessage = "AIRCRAFT SWAP ACTIVE\nPRESS '1' OR '2'";
    [TextArea] public string aircraftActiveMessage = "SWAP WINDOW OPEN ({0:0.0}s)\nPRESS '1' OR '2'";

    [Header("Character Settings")]
    public List<int> characterSwapIndices = new List<int> { 36, 72 };
    public float characterSwapRadius = 30f;
    public float characterUiRadius = 60f;

    [Header("Character UI")]
    public TMPro.TMP_Text characterStatusText;
    [TextArea] public string characterMessage = "CHARACTER SWAP ACTIVE\nPRESS '4'";
    [TextArea] public string characterActiveMessage = "SWAP WINDOW OPEN ({0:0.0}s)\nPRESS '4'";

    // Internal State
    [SerializeField] private int aircraftZoneIndex = -1;
    [SerializeField] private int characterZoneIndex = -1;

    private float aircraftTimer = 0f;
    private float characterTimer = 0f;

    public bool CanSwapAircraft => !enableAircraftCheckpointSwap || aircraftZoneIndex != -1;
    public bool CanSwapCharacter => !enableCharacterCheckpointSwap || characterZoneIndex != -1;

    void Update()
    {
        if (playerTransform == null)
        {
            GameObject p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) playerTransform = p.transform;
        }

        if (playerTransform == null) return;

        Vector3 playerPos = playerTransform.position;

        // --- AIRCRAFT LOGIC ---
        int foundAircraftIdx = -1;
        bool aircraftUiVisible = false;

        float sqrAirSwap = aircraftSwapRadius * aircraftSwapRadius;
        float sqrAirUi = aircraftUiRadius * aircraftUiRadius;

        foreach (int idx in aircraftSwapIndices)
        {
            float distSq = GetDistanceSq(idx, playerPos);
            if (distSq < 0f) continue;

            if (distSq <= sqrAirSwap) foundAircraftIdx = idx;
            if (distSq <= sqrAirUi) aircraftUiVisible = true;

             if (foundAircraftIdx != -1) break;
        }

        if (foundAircraftIdx != -1)
        {
            aircraftZoneIndex = foundAircraftIdx;
            aircraftTimer = swapDuration;
        }
        else
        {
            if (aircraftTimer > 0f)
            {
                aircraftTimer -= Time.deltaTime;
                if (aircraftTimer <= 0f)
                {
                    aircraftTimer = 0f;
                    aircraftZoneIndex = -1;
                }
            }
            else
            {
                aircraftZoneIndex = -1;
            }
        }

        // --- CHARACTER LOGIC ---
        int foundCharIdx = -1;
        bool charUiVisible = false;

        float sqrCharSwap = characterSwapRadius * characterSwapRadius;
        float sqrCharUi = characterUiRadius * characterUiRadius;

        foreach (int idx in characterSwapIndices)
        {
            float distSq = GetDistanceSq(idx, playerPos);
            if (distSq < 0f) continue;

            if (distSq <= sqrCharSwap) foundCharIdx = idx;
            if (distSq <= sqrCharUi) charUiVisible = true;

             if (foundCharIdx != -1) break;
        }

        if (foundCharIdx != -1)
        {
            characterZoneIndex = foundCharIdx;
            characterTimer = swapDuration;
        }
        else
        {
            if (characterTimer > 0f)
            {
                characterTimer -= Time.deltaTime;
                if (characterTimer <= 0f)
                {
                    characterTimer = 0f;
                    characterZoneIndex = -1;
                }
            }
            else
            {
                characterZoneIndex = -1;
            }
        }

        // --- UI UPDATE ---

        if (aircraftStatusText != null)
        {
            bool show = aircraftUiVisible || CanSwapAircraft;
            if (show)
            {
                if (CanSwapAircraft && enableAircraftCheckpointSwap && aircraftTimer < (swapDuration - 0.01f))
                {
                     aircraftStatusText.text = string.Format(aircraftActiveMessage, aircraftTimer);
                }
                else if (aircraftUiVisible || !enableAircraftCheckpointSwap)
                {
                     aircraftStatusText.text = aircraftMessage;
                }
                else if (CanSwapAircraft && enableAircraftCheckpointSwap)
                {
                    aircraftStatusText.text = string.Format(aircraftActiveMessage, aircraftTimer);
                }

                if (!aircraftStatusText.gameObject.activeSelf) aircraftStatusText.gameObject.SetActive(true);
            }
            else
            {
                if (aircraftStatusText.gameObject.activeSelf) aircraftStatusText.gameObject.SetActive(false);
            }
        }

        if (characterStatusText != null)
        {
            bool show = charUiVisible || CanSwapCharacter;
            if (show)
            {
                if (CanSwapCharacter && enableCharacterCheckpointSwap && characterTimer < (swapDuration - 0.01f))
                {
                    characterStatusText.text = string.Format(characterActiveMessage, characterTimer);
                }
                else if (charUiVisible || !enableCharacterCheckpointSwap)
                {
                    characterStatusText.text = characterMessage;
                }
                else if (CanSwapCharacter && enableCharacterCheckpointSwap)
                {
                    characterStatusText.text = string.Format(characterActiveMessage, characterTimer);
                }

                if (!characterStatusText.gameObject.activeSelf) characterStatusText.gameObject.SetActive(true);
            }
            else
            {
                if (characterStatusText.gameObject.activeSelf) characterStatusText.gameObject.SetActive(false);
            }
        }
    }

    private float GetDistanceSq(int index, Vector3 playerPos)
    {
        Transform cp = GetCheckpoint(index);
        if (cp == null) return -1f;
        return (cp.position - playerPos).sqrMagnitude;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  GIZMOS
    // ═══════════════════════════════════════════════════════════════════

    void OnDrawGizmos()
    {
        if (!showDebugGizmos) return;
        if (placementMode != CheckpointPlacementMode.Dispersed) return;

        if (_checkpoints.Count > 0)
        {
            Gizmos.color = gizmoColor;
            for (int i = 0; i < _checkpoints.Count; i++)
            {
                if (_checkpoints[i] == null) continue;
                Gizmos.DrawSphere(_checkpoints[i].position, dispersedColliderRadius);
            }

#if UNITY_EDITOR
            var labelStyle = new GUIStyle();
            labelStyle.normal.textColor = Color.white;
            labelStyle.fontSize = 10;
            for (int i = 0; i < _checkpoints.Count; i++)
            {
                if (_checkpoints[i] == null) continue;
                UnityEditor.Handles.Label(_checkpoints[i].position + Vector3.up * (dispersedColliderRadius + 1f), i.ToString(), labelStyle);
            }
#endif
        }

        // Draw BattleZone sphere boundary (use InitialRadius — safe in editor/before Fusion)
        Vector3 sphereCenter = transform.position;
        float sphereRadius = 500f;

        var gizmoBz = battleZone;
        if (gizmoBz == null)
            gizmoBz = FindFirstObjectByType<GV.Network.BattleZoneController>();

        if (gizmoBz != null)
        {
            sphereCenter = gizmoBz.transform.position;
            sphereRadius = gizmoBz.InitialRadius;
        }

        Gizmos.color = new Color(gizmoColor.r, gizmoColor.g, gizmoColor.b, 0.25f);
        Gizmos.DrawWireSphere(sphereCenter, sphereRadius);
    }
}
