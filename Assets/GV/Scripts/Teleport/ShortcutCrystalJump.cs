using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using GV;

[RequireComponent(typeof(Collider))]
public class ShortcutCrystalJump : MonoBehaviour
{
    [Header("Power Up Settings")]
    public bool manualTriggerOnly = false;
    public PowerUpType powerUpType = PowerUpType.Teleport;

    [Header("Network (leave empty)")]
    public CheckpointNetwork network;

    [Header("Dice (auto from parent)")]
    public DiceRingIndicator dice;

    [Header("Player Check")]
    public bool requirePlayerTag = true;
    public string playerTag = "Player";

    [Header("Teleport")]
    public float cooldownSeconds = 1.0f;
    public bool keepVelocity = true;

    [Tooltip("Reappear this many meters BEFORE the target checkpoint along the checkpoint path.")]
    public float behindDistanceOnPath = 4.0f;

    public float upOffset = 0.0f;
    public float rightOffset = 0.0f;

    [Header("Teleport SFX")]
    public bool playTeleportSfx = true;
    public AudioClip teleportSfx;

    [Tooltip("Optional. If empty, tries Player Rigidbody's AudioSource, then this object's AudioSource, else falls back to PlayClipAtPoint.")]
    public AudioSource teleportSfxSource;

    [Range(0f, 1f)] public float teleportSfxVolume = 1f;
    [Tooltip("Delay before playing SFX after teleport (seconds).")]
    public float teleportSfxDelay = 0f;

    [Tooltip("Prevents the teleport AudioSource from playing on scene start (disables Play On Awake and stops it if it is playing the teleport clip).")]
    public bool enforceTeleportSourceNoPlayOnAwake = true;

    [Header("Start Safety")]
    [Tooltip("If the player starts inside this trigger, ignore OnTriggerEnter for a short time after enable.")]
    public bool ignoreEnterRightAfterEnable = true;

    [Min(0f)]
    public float ignoreEnterSeconds = 0.2f;

    [Header("Auto-follow after teleport")]
    public bool autoPilotAfterTeleport = true;
    public float autoPilotSeconds = 3.0f;
    public bool autoPilotUseCurrentSpeed = true;
    public float autoPilotSpeed = 50f;
    public float autoPilotSpeedMultiplier = 1f;

    static readonly Dictionary<int, float> _nextAllowed = new Dictionary<int, float>();
    float _ignoreEnterUntilTime = 0f;

    void Reset()
    {
        var c = GetComponent<Collider>();
        c.isTrigger = true;
    }

    void Awake() => ResolveRefs();

    void OnEnable()
    {
        ResolveRefs();

        if (ignoreEnterRightAfterEnable)
            _ignoreEnterUntilTime = Time.time + ignoreEnterSeconds;
    }

    void ResolveRefs()
    {
        if (!network)
        {
            network = CheckpointNetwork.Instance;
            if (!network) network = FindObjectOfType<CheckpointNetwork>(true);
        }

        if (!dice)
            dice = GetComponentInParent<DiceRingIndicator>(true);

        // Make sure teleport source doesn't fire on Play due to inspector settings.
        if (enforceTeleportSourceNoPlayOnAwake)
            SanitizeTeleportAudioSource();
    }

    void SanitizeTeleportAudioSource()
    {
        // Only touch the explicitly assigned teleport source (safe), or this object's source if it is clearly the teleport clip.
        if (teleportSfxSource)
        {
            teleportSfxSource.playOnAwake = false;
            if (teleportSfxSource.isPlaying && teleportSfxSource.clip == teleportSfx)
                teleportSfxSource.Stop();
        }
        else
        {
            var src = GetComponent<AudioSource>();
            if (src && src.clip == teleportSfx)
            {
                src.playOnAwake = false;
                if (src.isPlaying) src.Stop();
            }
        }
    }

    bool IsPlayer(Collider other, Rigidbody rb)
    {
        if (!requirePlayerTag) return true;
        if (rb && rb.CompareTag(playerTag)) return true;
        Transform root = other.transform.root;
        return root && root.CompareTag(playerTag);
    }

    void Start()
    {
        Debug.Log($"[ShortcutCrystalJump] Script started on {gameObject.name}. Manual: {manualTriggerOnly}");
    }

    void OnTriggerEnter(Collider other)
    {
        Debug.Log($"[ShortcutCrystalJump] OnTriggerEnter. Manual: {manualTriggerOnly} Other: {other.name}");
        if (manualTriggerOnly) return;

        if (ignoreEnterRightAfterEnable && Time.time < _ignoreEnterUntilTime) return;

        Rigidbody rb = other.attachedRigidbody;
        if (!IsPlayer(other, rb)) return;
        if (!rb) return;

        Debug.Log($"[ShortcutCrystalJump] Trigger valid. Applying to {rb.name}");
        Apply(rb.gameObject);
    }

    public void Apply(GameObject target)
    {
        Rigidbody rb = target.GetComponent<Rigidbody>();
        if (!rb)
        {
            rb = target.GetComponentInParent<Rigidbody>();
        }
        
        if (!rb) return;

        // Register Collection
        if (PowerUpManager.Instance != null)
        {
            PowerUpManager.Instance.RegisterCollection(powerUpType);
        }

        int id = rb.GetInstanceID();
        float now = Time.time;
        if (_nextAllowed.TryGetValue(id, out float allowed) && now < allowed) return;

        if (!network)
        {
            network = CheckpointNetwork.Instance;
            if (!network) network = FindObjectOfType<CheckpointNetwork>(true);
        }
        if (!network) 
        {
            Debug.LogError("[ShortcutCrystalJump] Apply failed: No CheckpointNetwork found.");
            return;
        }

        if (network.Count == 0)
        {
            network.Build();
            if (network.Count == 0)
            {
                Debug.LogError("[ShortcutCrystalJump] Apply failed: CheckpointNetwork has 0 checkpoints.");
                 return;
            }
        }

        if (!dice)
        {
            // If no dice is hooked up (e.g. manual Mystery Sphere), assume 1 forward? 
            // Or maybe we can't teleport without dice context?
            // Existing logic enforced dice return, let's keep it safe but maybe try to find one if missing.
             dice = GetComponentInParent<DiceRingIndicator>(true);
             if (!dice)
             {
                 Debug.LogWarning("[ShortcutCrystalJump] No DiceRingIndicator found. Using default +1 increment.");
                 // Fallback logic implemented below
             } 
        }

        int anchorIndex = network.GetNearestIndex(transform.position);

        int delta = 1;
        if (dice != null)
        {
            delta = dice.IsForward ? dice.DiceValue : -dice.DiceValue;
        }
        else
        {
             // Default behavior for power-ups without dice: Jump 1 checkpoint forward?
             // Or maybe we want to jump 3? Let's stick to 1 for now or make it a public variable later.
             delta = 1;
        }

        int targetIndex = network.ClampOrWrapIndex(anchorIndex + delta);

        Vector3 tangent;
        Vector3 basePos = network.GetPositionBehindOnPath(targetIndex, behindDistanceOnPath, out tangent);

        Vector3 right = Vector3.Cross(Vector3.up, tangent).normalized;
        Vector3 destPos = basePos + Vector3.up * upOffset + right * rightOffset;

        Vector3 savedVel = rb.linearVelocity;
        Vector3 savedAngVel = rb.angularVelocity;

        rb.position = destPos;
        if (tangent.sqrMagnitude > 0.000001f)
            rb.rotation = Quaternion.LookRotation(tangent, Vector3.up);

        if (keepVelocity)
        {
            rb.linearVelocity = savedVel;
            rb.angularVelocity = savedAngVel;
        }
        else
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        _nextAllowed[id] = now + cooldownSeconds;

        // Play SFX AFTER teleport
        TryPlayTeleportSfx(rb, destPos);

        if (autoPilotAfterTeleport)
        {
            var ap = rb.GetComponent<PathAutoPilot>();
            if (!ap) ap = rb.gameObject.AddComponent<PathAutoPilot>();

            float speedToUse = autoPilotSpeed;
            if (autoPilotUseCurrentSpeed)
            {
                float v = savedVel.magnitude;
                speedToUse = Mathf.Max(5f, v * autoPilotSpeedMultiplier);
            }

            ap.Begin(targetIndex, autoPilotSeconds, speedToUse);
        }
    }

    void TryPlayTeleportSfx(Rigidbody playerRb, Vector3 atPos)
    {
        if (!playTeleportSfx) return;
        if (!teleportSfx) return;

        if (teleportSfxDelay > 0f)
        {
            StartCoroutine(PlayTeleportSfxDelayed(playerRb, atPos));
            return;
        }

        PlayTeleportSfxNow(playerRb, atPos);
    }

    IEnumerator PlayTeleportSfxDelayed(Rigidbody playerRb, Vector3 atPos)
    {
        yield return new WaitForSeconds(teleportSfxDelay);
        PlayTeleportSfxNow(playerRb, atPos);
    }

    void PlayTeleportSfxNow(Rigidbody playerRb, Vector3 atPos)
    {
        AudioSource src = teleportSfxSource;

        if (!src && playerRb) src = playerRb.GetComponent<AudioSource>();
        if (!src) src = GetComponent<AudioSource>();

        if (src)
        {
            // Ensure we don't accidentally Play() a clip assigned on the source; use OneShot only.
            src.playOnAwake = false;
            src.PlayOneShot(teleportSfx, teleportSfxVolume);
        }
        else
        {
            AudioSource.PlayClipAtPoint(teleportSfx, atPos, teleportSfxVolume);
        }
    }
}
