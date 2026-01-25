using UnityEngine;

/// <summary>
/// 3-ring gyroscope (free / non-upright)
/// - Outer pivot rotates on THREE axes
/// - Middle and inner rotate on one axis each
/// - Single master speed multiplier controls all rotation uniformly
/// - No overlap via spacing along shared normal (local Y / green)
///
/// RANDOMIZATION:
/// - Per-instance randomization (seeded) so every duplicated ring animates differently
/// - Optional randomized axis directions + speed multipliers + phase offsets
/// - Master speed randomization via ONE slider (0..10):
///     0  = no master randomization (masterSpeed stays as-is)
///     10 = high master randomization (per-instance factor varies more)
/// </summary>
public class GyroFree3Ring_MasterSpeed_Outer3Axis : MonoBehaviour
{
    [Header("Pivots (empty transforms)")]
    public Transform outerPivot;
    public Transform middlePivot;
    public Transform innerPivot;

    [Header("Ring Meshes (visible)")]
    public Transform outerRing;
    public Transform middleRing;
    public Transform innerRing;

    [Header("Master Speed")]
    [Tooltip("Base master speed (before per-ring randomization factor is applied).")]
    public float masterSpeed = 1f;

    [Tooltip("Final master speed used at runtime (after randomization). Read-only in Inspector.")]
    [SerializeField] private float runtimeMasterSpeed = 1f;

    [Header("Base Speeds (deg/sec)")]
    public float outerXBase = 12f;
    public float outerYBase = 25f;
    public float outerZBase = 18f;
    public float middleBase = 40f;
    public float innerBase = 90f;

    [Header("Rotation Axes (LOCAL to each pivot)")]
    public Vector3 outerXAxis = Vector3.right;   // X
    public Vector3 outerYAxis = Vector3.up;      // Y
    public Vector3 outerZAxis = Vector3.forward; // Z
    public Vector3 middleAxis = Vector3.right;   // X
    public Vector3 innerAxis = Vector3.forward;  // Z

    [Header("No-overlap spacing (all ring normals are GREEN / local Y)")]
    public float gap = 0.12f;

    // ------------------ Randomization ------------------
    [Header("Per-ring Randomization")]
    [Tooltip("If enabled, this ring instance will get unique randomized speeds/axes/phase.")]
    public bool randomizePerRing = true;

    [Tooltip("If true, uses object position/name based hashing to create stable per-instance values.")]
    public bool stablePerInstance = true;

    [Tooltip("Optional: set != 0 for deterministic variations across runs.")]
    public int seedOverride = 0;

    [Tooltip("Random multiplier range applied to ALL base speeds (outerX/Y/Z, middle, inner).")]
    public Vector2 speedMultiplierRange = new Vector2(0.8f, 1.25f);

    [Tooltip("Random extra degrees/sec added per axis (can be 0).")]
    public Vector2 speedAddRange = new Vector2(0f, 10f);

    [Tooltip("Random sign flip chance for each axis (0 = never flip, 1 = always random +/-).")]
    [Range(0f, 1f)] public float axisFlipChance = 0.5f;

    [Tooltip("Randomly perturbs the axis direction a bit (0 = no change).")]
    [Range(0f, 1f)] public float axisJitter = 0.15f;

    [Tooltip("Applies a random starting rotation (phase) to pivots on Awake.")]
    public bool randomizeStartPhase = true;

    [Header("Master Speed Randomization (single slider)")]
    [Tooltip("0 = no master randomization, 10 = high per-ring master variation.")]
    [Range(0f, 10f)] public float masterRandomness = 0f;

    [Tooltip("Safety clamp so random master speed never becomes too slow/fast.")]
    public Vector2 masterRandomClamp = new Vector2(0.2f, 3.0f);

    // Internal randomized values (computed once per instance)
    float outerXSpeed, outerYSpeed, outerZSpeed, middleSpeed, innerSpeed;
    Vector3 outerXAxisR, outerYAxisR, outerZAxisR, middleAxisR, innerAxisR;
    // ----------------------------------------------------

    Vector3 outerBasePos, middleBasePos, innerBasePos;

    void Awake()
    {
        if (outerRing) outerBasePos = outerRing.localPosition;
        if (middleRing) middleBasePos = middleRing.localPosition;
        if (innerRing) innerBasePos = innerRing.localPosition;

        ApplyPerRingRandomization();
    }

    void OnValidate()
    {
        masterSpeed = Mathf.Max(0f, masterSpeed);
        gap = Mathf.Max(0f, gap);

        if (speedMultiplierRange.x > speedMultiplierRange.y)
            speedMultiplierRange = new Vector2(speedMultiplierRange.y, speedMultiplierRange.x);

        if (speedAddRange.x > speedAddRange.y)
            speedAddRange = new Vector2(speedAddRange.y, speedAddRange.x);

        if (masterRandomClamp.x > masterRandomClamp.y)
            masterRandomClamp = new Vector2(masterRandomClamp.y, masterRandomClamp.x);

        axisJitter = Mathf.Clamp01(axisJitter);
        axisFlipChance = Mathf.Clamp01(axisFlipChance);
        masterRandomness = Mathf.Clamp(masterRandomness, 0f, 10f);
    }

    void ApplyPerRingRandomization()
    {
        // Defaults: use original values
        outerXSpeed = outerXBase;
        outerYSpeed = outerYBase;
        outerZSpeed = outerZBase;
        middleSpeed = middleBase;
        innerSpeed  = innerBase;

        outerXAxisR = SafeAxis(outerXAxis);
        outerYAxisR = SafeAxis(outerYAxis);
        outerZAxisR = SafeAxis(outerZAxis);
        middleAxisR = SafeAxis(middleAxis);
        innerAxisR  = SafeAxis(innerAxis);

        runtimeMasterSpeed = masterSpeed;

        if (!randomizePerRing)
            return;

        // Choose a stable seed per instance
        int seed;
        if (seedOverride != 0)
        {
            seed = seedOverride;
        }
        else if (stablePerInstance)
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + Mathf.RoundToInt(transform.position.x * 1000f);
                h = h * 31 + Mathf.RoundToInt(transform.position.y * 1000f);
                h = h * 31 + Mathf.RoundToInt(transform.position.z * 1000f);
                h = h * 31 + gameObject.name.GetHashCode();
                h = h * 31 + transform.GetInstanceID();
                seed = h;
            }
        }
        else
        {
            seed = Random.Range(int.MinValue, int.MaxValue);
        }

        var prevState = Random.state;
        Random.InitState(seed);

        // 1) Master speed randomization via single slider (0..10)
        // Map slider to a variation band around 1.0:
        // randomness 0  -> +/- 0%
        // randomness 10 -> +/- 60% (tunable)
        float maxBand = 0.60f;
        float band = (masterRandomness / 10f) * maxBand; // 0..0.6
        float masterFactor = (band <= 0f) ? 1f : Random.Range(1f - band, 1f + band);

        runtimeMasterSpeed = Mathf.Clamp(masterSpeed * masterFactor, masterRandomClamp.x, masterRandomClamp.y);

        // 2) Speed randomization (per-axis), separate from master
        float mult = Random.Range(speedMultiplierRange.x, speedMultiplierRange.y);

        outerXSpeed = (outerXBase * mult) + Random.Range(speedAddRange.x, speedAddRange.y);
        outerYSpeed = (outerYBase * mult) + Random.Range(speedAddRange.x, speedAddRange.y);
        outerZSpeed = (outerZBase * mult) + Random.Range(speedAddRange.x, speedAddRange.y);
        middleSpeed = (middleBase * mult) + Random.Range(speedAddRange.x, speedAddRange.y);
        innerSpeed  = (innerBase  * mult) + Random.Range(speedAddRange.x, speedAddRange.y);

        // 3) Axis randomization (flip + slight direction jitter)
        outerXAxisR = RandomizeAxis(outerXAxisR);
        outerYAxisR = RandomizeAxis(outerYAxisR);
        outerZAxisR = RandomizeAxis(outerZAxisR);
        middleAxisR = RandomizeAxis(middleAxisR);
        innerAxisR  = RandomizeAxis(innerAxisR);

        // 4) Random starting phase
        if (randomizeStartPhase)
        {
            if (outerPivot)  outerPivot.localRotation  = Random.rotation * outerPivot.localRotation;
            if (middlePivot) middlePivot.localRotation = Random.rotation * middlePivot.localRotation;
            if (innerPivot)  innerPivot.localRotation  = Random.rotation * innerPivot.localRotation;
        }

        Random.state = prevState;
    }

    Vector3 SafeAxis(Vector3 axis)
    {
        if (axis == Vector3.zero) return Vector3.up;
        return axis.normalized;
    }

    Vector3 RandomizeAxis(Vector3 axis)
    {
        Vector3 a = SafeAxis(axis);

        // Random sign flip
        if (Random.value < axisFlipChance)
            a *= (Random.value < 0.5f) ? -1f : 1f;

        // Small jitter in direction
        if (axisJitter > 0f)
        {
            Vector3 jitter = new Vector3(
                Random.Range(-1f, 1f),
                Random.Range(-1f, 1f),
                Random.Range(-1f, 1f)
            ) * axisJitter;

            a = (a + jitter).normalized;
        }

        return a;
    }

    void Update()
    {
        if (!outerPivot || !middlePivot || !innerPivot) return;

        float dt = Time.deltaTime;
        float m = runtimeMasterSpeed; // <- use randomized master here

        // Outer: 3-axis rotation (same pivot)
        outerPivot.localRotation *= Quaternion.AngleAxis(outerXSpeed * m * dt, outerXAxisR);
        outerPivot.localRotation *= Quaternion.AngleAxis(outerYSpeed * m * dt, outerYAxisR);
        outerPivot.localRotation *= Quaternion.AngleAxis(outerZSpeed * m * dt, outerZAxisR);

        // Middle: one axis
        middlePivot.localRotation *= Quaternion.AngleAxis(middleSpeed * m * dt, middleAxisR);

        // Inner: one axis
        innerPivot.localRotation *= Quaternion.AngleAxis(innerSpeed * m * dt, innerAxisR);
    }

    void LateUpdate()
    {
        // Keep rings separated along shared normal (local Y / green)
        if (outerRing)  outerRing.localPosition  = outerBasePos;
        if (middleRing) middleRing.localPosition = middleBasePos + Vector3.up * gap;
        if (innerRing)  innerRing.localPosition  = innerBasePos  + Vector3.up * gap * 2f;
    }
}
