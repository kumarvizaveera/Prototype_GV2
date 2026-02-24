using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum CheckpointPlacementMode
{
    Spline,
    Dispersed
}

public enum DispersedSpawnShape
{
    [Tooltip("Spawn checkpoints inside the BattleZone sphere.")]
    Sphere,
    [Tooltip("Spawn checkpoints inside a box volume (GameObject). They still shrink with the BattleZone sphere.")]
    Box
}

[DefaultExecutionOrder(-1000)]
public class CheckpointNetwork : MonoBehaviour
{
    public static CheckpointNetwork Instance { get; private set; }

    [Header("Placement Mode")]
    [Tooltip("Spline = use existing child transforms. Dispersed = randomly scatter in 3D space.")]
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

    [Header("Spawn Shape")]
    [Tooltip("Sphere = spawn inside the BattleZone sphere. Box = spawn inside a box volume GameObject. Either way, checkpoints shrink with the BattleZone sphere.")]
    public DispersedSpawnShape spawnShape = DispersedSpawnShape.Sphere;

    [Tooltip("Box volume GameObject for spawn shape Box. Uses this object's position and lossy scale as the bounding box. Leave null to use the BattleZone sphere.")]
    public Transform spawnBoxVolume;

    [Header("BattleZone Sphere Reference")]
    [Tooltip("Reference to the BattleZoneController (drives shrinking). If null, auto-finds in scene.")]
    public GV.Network.BattleZoneController battleZone;

    [Header("Turret Prefabs (Dispersed Mode)")]
    [Tooltip("Turret prefabs to instantiate at each dispersed checkpoint. One is randomly chosen per checkpoint (seeded). Leave empty for no turret spawning.")]
    public List<GameObject> turretPrefabs = new List<GameObject>();
    [Tooltip("Offset applied to the turret position relative to the checkpoint.")]
    public Vector3 turretPositionOffset = Vector3.zero;
    [Tooltip("If true, turrets face a random Y rotation. If false, they use identity rotation.")]
    public bool randomTurretYRotation = true;

    [Header("Turret Team")]
    [Tooltip("Team to assign to spawned turrets. If set, turrets will target ships on hostile teams. Drag the 'Enemy' Team asset here.")]
    public VSX.Teams.Team turretTeam;

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

        // Give turrets a chance to acquire targets after everything is initialized.
        // The ship's Trackable may not be registered yet when turrets spawn in Awake().
        if (placementMode == CheckpointPlacementMode.Dispersed)
        {
            StartCoroutine(DelayedTargetReacquisition(1f));
            StartCoroutine(DelayedTargetReacquisition(3f));
        }
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

        // Only rebuild if we don't already have checkpoints (Awake already built them).
        // Rebuilding here would destroy turrets that already acquired targets and were firing.
        if (_checkpoints.Count == 0)
        {
            Debug.Log("[CheckpointNetwork] BuildAfterSpawn: no checkpoints yet, building...");
            Build();
        }
        else
        {
            Debug.Log($"[CheckpointNetwork] BuildAfterSpawn: already have {_checkpoints.Count} checkpoints, skipping rebuild.");
        }
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

        float fallback = battleZone.InitialRadius > 0f ? battleZone.InitialRadius : 500f;

        // Try the networked property first (only valid after Fusion Spawned)
        try
        {
            float r = battleZone.CurrentRadius;
            if (r > 0f) return r;
        }
        catch { /* Networked state not yet allocated — fall through */ }

        // Fallback: read the serialized initialRadius (always safe).
        // NEVER return 0 — that collapses all checkpoints to the sphere center.
        return fallback;
    }

    bool IsFusionConnected()
    {
        var nm = GV.Network.NetworkManager.Instance;
        return nm != null && nm.IsConnected;
    }

    /// <summary>
    /// Returns true if Fusion is NOT connected (single-player) or if we are the server/host.
    /// Turret weapons should only fire on the host; clients get damage via NetworkedHealthSync.
    /// </summary>
    bool IsHostOrOffline()
    {
        var nm = GV.Network.NetworkManager.Instance;
        if (nm == null || !nm.IsConnected || nm.Runner == null) return true; // offline / single-player
        return nm.Runner.IsServer;
    }

    IEnumerator RebuildAfterFusionSceneRegistration()
    {
        Debug.Log($"[CPNET-DBG] RebuildAfterFusion STARTED — waiting 15 frames...");

        // Wait 15 frames (up from 5) to ensure Fusion scene registration is fully complete
        for (int i = 0; i < 15; i++)
            yield return null;

        if (!_turretsNeedRebuildAfterFusion)
        {
            Debug.LogWarning($"[CPNET-DBG] RebuildAfterFusion ABORTED — _turretsNeedRebuildAfterFusion was already false!");
            yield break;
        }
        _turretsNeedRebuildAfterFusion = false;

        // ═══ KEY FIX: Do NOT destroy and rebuild turrets. ═══
        // The old approach (Build()) destroyed all existing turrets — including ones that
        // had already acquired targets and were firing — then recreated them from scratch.
        // The new turrets couldn't find targets because the ship's Trackable was temporarily
        // inactive during Fusion scene registration.
        //
        // Instead, just force-enable the existing turrets that Fusion may have deactivated.
        Debug.Log("[CheckpointNetwork] Post-Fusion: force-enabling existing turrets (no rebuild)...");
        ForceEnableAllDispersedObjects();

        Debug.Log($"[CheckpointNetwork] Post-Fusion force-enable complete — {_checkpoints.Count} checkpoints, {_dispersedObjects.Count} dispersed objects");

        // ─── Delayed target re-acquisition ───
        // The ship's Trackable may still be inactive right now. Wait a bit, then
        // kick turrets that don't have a target by refreshing their Tracker.
        StartCoroutine(DelayedTargetReacquisition(1f));
        StartCoroutine(DelayedTargetReacquisition(3f));
        StartCoroutine(DelayedTargetReacquisition(6f));

        // ─── DEBUG: Delayed diagnostics ───
        StartCoroutine(DelayedTargetDiagnostic(5f));
        StartCoroutine(DelayedTargetDiagnostic(10f));
    }

    IEnumerator DelayedTargetDiagnostic(float delay)
    {
        yield return new WaitForSeconds(delay);

        Debug.Log($"[CPNET-DBG] ═══ DELAYED DIAGNOSTIC ({delay}s after rebuild) ═══");

        // 1. What's in TrackableSceneManager?
        var tsm = VSX.RadarSystem.TrackableSceneManager.Instance;
        if (tsm != null)
        {
            Debug.Log($"[CPNET-DBG] TrackableSceneManager: {tsm.Trackables.Count} trackables registered:");
            foreach (var t in tsm.Trackables)
            {
                Debug.Log($"[CPNET-DBG]   → {t.gameObject.name} | team={t.Team?.name ?? "NULL"} | " +
                          $"active={t.gameObject.activeInHierarchy} | enabled={t.enabled} | " +
                          $"activated={t.Activated}");
            }
        }
        else
        {
            Debug.LogError($"[CPNET-DBG] TrackableSceneManager.Instance is NULL!");
        }

        // 2. Check first 3 turrets' TargetSelector state
        int checked_count = 0;
        foreach (var go in _dispersedObjects)
        {
            if (go == null) continue;
            if (checked_count >= 3) break;

            foreach (var tc in go.GetComponentsInChildren<VSX.Weapons.TurretController>(true))
            {
                var ts = tc.TargetSelector;
                if (ts != null)
                {
                    string selTeams = ts.SelectableTeams != null
                        ? string.Join(", ", ts.SelectableTeams.ConvertAll(t => t != null ? t.name : "null"))
                        : "EMPTY";
                    string selTarget = ts.SelectedTarget != null ? ts.SelectedTarget.gameObject.name : "NULL";

                    // Check if it's a TrackerTargetSelector and inspect the Tracker
                    var trackerTS = ts as VSX.RadarSystem.TrackerTargetSelector;
                    string trackerInfo = "N/A (base TargetSelector)";
                    if (trackerTS != null)
                    {
                        var trackerField = trackerTS.GetType().GetField("tracker",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        var tracker = trackerField?.GetValue(trackerTS) as VSX.RadarSystem.Tracker;
                        if (tracker != null)
                            trackerInfo = $"Tracker found, targets={tracker.Targets.Count}";
                        else
                            trackerInfo = "Tracker is NULL!";
                    }

                    Debug.Log($"[CPNET-DBG] Turret '{tc.gameObject.name}': " +
                              $"selectableTeams=[{selTeams}], selectedTarget={selTarget}, " +
                              $"scanEveryFrame={ts.ScanEveryFrame}, " +
                              $"trackerInfo={trackerInfo}");
                }
                checked_count++;
            }
        }
    }

    /// <summary>
    /// After Fusion connects, the ship's Trackable may be temporarily inactive.
    /// This coroutine waits, then forces all turret Trackers to re-query TrackableSceneManager
    /// so they can re-acquire the ship once its Trackable comes back online.
    /// </summary>
    IEnumerator DelayedTargetReacquisition(float delay)
    {
        yield return new WaitForSeconds(delay);

        var tsm = VSX.RadarSystem.TrackableSceneManager.Instance;
        int trackableCount = tsm != null ? tsm.Trackables.Count : 0;

        if (trackableCount == 0)
        {
            Debug.Log($"[CPNET-DBG] TargetReacquisition ({delay}s): TrackableSceneManager has 0 trackables — skipping (will retry later)");
            yield break;
        }

        int kicked = 0;
        foreach (var go in _dispersedObjects)
        {
            if (go == null) continue;

            // Force Trackers to refresh their target list from TrackableSceneManager
            foreach (var tracker in go.GetComponentsInChildren<VSX.RadarSystem.Tracker>(true))
            {
                if (tracker.enabled && tracker.gameObject.activeInHierarchy)
                {
                    tracker.UpdateTargets();
                }
            }

            // Force TargetSelectors to re-scan if they have no target
            foreach (var ts in go.GetComponentsInChildren<VSX.RadarSystem.TargetSelector>(true))
            {
                if (ts.enabled && ts.SelectedTarget == null)
                {
                    ts.SelectFirstSelectableTarget();
                    kicked++;
                }
            }
        }

        Debug.Log($"[CPNET-DBG] TargetReacquisition ({delay}s): kicked {kicked} targetless turrets, " +
                  $"trackablesInScene={trackableCount}");
    }

    void ForceEnableAllDispersedObjects()
    {
        Debug.Log($"[CPNET-DBG] ForceEnableAllDispersedObjects — dispersedObjects={_dispersedObjects.Count}, " +
                  $"fusionConnected={IsFusionConnected()}, isHostOrOffline={IsHostOrOffline()}");

        if (_dispersedParent != null)
            _dispersedParent.gameObject.SetActive(true);

        int wcCount = 0, weaponCount = 0, disabledGO = 0;
        foreach (var go in _dispersedObjects)
        {
            if (go == null) continue;

            if (!go.activeInHierarchy) disabledGO++;
            go.SetActive(true);

            foreach (Transform child in go.GetComponentsInChildren<Transform>(true))
                child.gameObject.SetActive(true);

            foreach (var wc in go.GetComponentsInChildren<VSX.Weapons.WeaponController>(true))
            {
                bool wasBefore = wc.Activated;
                wc.enabled = true;
                wc.Activated = true;
                wcCount++;
                if (!wasBefore)
                    Debug.Log($"[CPNET-DBG]   WeaponController '{wc.gameObject.name}' was DEACTIVATED, now forced ON");
            }

            foreach (var w in go.GetComponentsInChildren<VSX.Weapons.Weapon>(true))
            {
                w.enabled = true;
                weaponCount++;
            }
        }

        Debug.Log($"[CPNET-DBG] ForceEnable DONE — enabled {wcCount} WeaponControllers, {weaponCount} Weapons, " +
                  $"reactivated {disabledGO} disabled GameObjects");
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

    // ─── Dispersed ──────────────────────────────────────────────────────
    // Spawn shape: Sphere (inside BattleZone) or Box (inside a volume GameObject).
    // Either way, checkpoints shrink proportionally with the BattleZone sphere.

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

        // ── Get BattleZone sphere (used for shrinking regardless of spawn shape) ──
        if (battleZone == null)
            battleZone = FindFirstObjectByType<GV.Network.BattleZoneController>();

        Vector3 sphereCenter;
        float sphereRadius;

        if (battleZone != null)
        {
            sphereCenter = battleZone.transform.position;
            sphereRadius = battleZone.InitialRadius;
        }
        else
        {
            sphereCenter = transform.position;
            sphereRadius = 500f;
            Debug.LogWarning("[CheckpointNetwork] No BattleZoneController found! Using fallback sphere at transform.position with radius 500.");
        }

        _initialSphereRadius = sphereRadius;

        // ═══ NETWORK-SAFE DETERMINISTIC RNG ═══
        int seed = dispersedSeed != 0 ? dispersedSeed : 42;
        System.Random rng = new System.Random(seed);

        // ── Generate positions based on spawn shape ──
        List<Vector3> positions = new List<Vector3>(dispersedCount);
        List<Vector3> normalizedOffsets = new List<Vector3>(dispersedCount);
        float minSqr = dispersedMinSpacing * dispersedMinSpacing;
        int maxAttempts = dispersedCount * 200;
        int attempts = 0;

        // Box spawn shape: use the spawnBoxVolume's position and lossyScale as extents
        Vector3 boxCenter = sphereCenter;
        Vector3 boxHalfExtents = Vector3.one * sphereRadius;

        if (spawnShape == DispersedSpawnShape.Box && spawnBoxVolume != null)
        {
            boxCenter = spawnBoxVolume.position;
            boxHalfExtents = spawnBoxVolume.lossyScale * 0.5f;
            Debug.Log($"[CheckpointNetwork] Spawn shape = Box → center={boxCenter}, halfExtents={boxHalfExtents}");
        }
        else if (spawnShape == DispersedSpawnShape.Sphere)
        {
            Debug.Log($"[CheckpointNetwork] Spawn shape = Sphere → center={sphereCenter}, radius={sphereRadius}");
        }
        else if (spawnShape == DispersedSpawnShape.Box && spawnBoxVolume == null)
        {
            Debug.LogWarning("[CheckpointNetwork] Spawn shape is Box but no spawnBoxVolume assigned! Falling back to Sphere.");
        }

        while (positions.Count < dispersedCount && attempts < maxAttempts)
        {
            attempts++;

            Vector3 candidate;
            Vector3 normalizedOffset;

            if (spawnShape == DispersedSpawnShape.Box && spawnBoxVolume != null)
            {
                // ── Box spawn: random point inside the box volume ──
                Vector3 localPoint = new Vector3(
                    RngRange(rng, -boxHalfExtents.x, boxHalfExtents.x),
                    RngRange(rng, -boxHalfExtents.y, boxHalfExtents.y),
                    RngRange(rng, -boxHalfExtents.z, boxHalfExtents.z)
                );
                candidate = boxCenter + localPoint;

                // Convert to a normalized offset relative to the BattleZone sphere center.
                // This allows the checkpoints to shrink with the sphere even though
                // they were spawned in a box shape.
                Vector3 offsetFromSphereCenter = candidate - sphereCenter;
                normalizedOffset = (sphereRadius > 0.01f)
                    ? offsetFromSphereCenter / sphereRadius
                    : Vector3.zero;
            }
            else
            {
                // ── Sphere spawn: random point inside a unit sphere (rejection sampling) ──
                Vector3 unitPoint;
                do
                {
                    unitPoint = new Vector3(
                        RngRange(rng, -1f, 1f),
                        RngRange(rng, -1f, 1f),
                        RngRange(rng, -1f, 1f)
                    );
                }
                while (unitPoint.sqrMagnitude > 1f);

                candidate = sphereCenter + unitPoint * sphereRadius;
                normalizedOffset = unitPoint;
            }

            // Min-spacing check
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
            normalizedOffsets.Add(normalizedOffset);
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

                    // Assign team to turret (so TargetSelector knows which teams are hostile)
                    if (turretTeam != null)
                    {
                        foreach (var tc in turret.GetComponentsInChildren<VSX.Weapons.TurretController>(true))
                        {
                            tc.SetTeam(turretTeam);
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"[CheckpointNetwork] turretTeam is not assigned! Turrets won't know which teams are hostile. " +
                                         "Drag the 'Enemy' Team asset into CheckpointNetwork → Turret Team in the Inspector.");
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

        string shapeStr = (spawnShape == DispersedSpawnShape.Box && spawnBoxVolume != null) ? "Box" : "Sphere";
        Debug.Log($"[CheckpointNetwork] Dispersed ({shapeStr}): placed {positions.Count}/{dispersedCount} " +
                  $"(seed={seed}, sphereCenter={sphereCenter}, sphereRadius={sphereRadius}, turrets={hasTurrets})");

        if (positions.Count < dispersedCount)
        {
            Debug.LogWarning($"[CheckpointNetwork] Only placed {positions.Count}/{dispersedCount} " +
                             $"dispersed checkpoints (minSpacing={dispersedMinSpacing}). " +
                             "Reduce minSpacing or increase volume size.");
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

        // If using Box spawn shape, also draw the box volume
        if (spawnShape == DispersedSpawnShape.Box && spawnBoxVolume != null)
        {
            Gizmos.color = new Color(1f, 1f, 0f, 0.3f); // yellow
            Gizmos.DrawWireCube(spawnBoxVolume.position, spawnBoxVolume.lossyScale);
        }
    }
}
