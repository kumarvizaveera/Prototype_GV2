using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class ShortcutCrystalJump : MonoBehaviour
{
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

    public enum TeleportSfxTiming
    {
        BeforeTeleport,   // plays at the entry/origin position
        DuringTeleport,   // plays at the destination immediately when teleport happens
        AfterTeleport     // plays at the destination after teleport finishes (end of frame)
    }

    [Header("Teleport SFX")]
    public bool playTeleportSfx = true;
    public AudioClip teleportSfx;

    [Tooltip("Optional. If empty, tries Player Rigidbody's AudioSource, then this object's AudioSource, else falls back to PlayClipAtPoint.")]
    public AudioSource teleportSfxSource;

    [Range(0f, 1f)] public float teleportSfxVolume = 1f;

    [Tooltip("Extra delay applied based on timing (seconds).")]
    public float teleportSfxDelay = 0f;

    public TeleportSfxTiming teleportSfxTiming = TeleportSfxTiming.AfterTeleport;

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

        if (enforceTeleportSourceNoPlayOnAwake)
            SanitizeTeleportAudioSource();
    }

    void SanitizeTeleportAudioSource()
    {
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

    void OnTriggerEnter(Collider other)
    {
        if (ignoreEnterRightAfterEnable && Time.time < _ignoreEnterUntilTime) return;

        Rigidbody rb = other.attachedRigidbody;
        if (!IsPlayer(other, rb)) return;
        if (!rb) return;

        int id = rb.GetInstanceID();
        float now = Time.time;
        if (_nextAllowed.TryGetValue(id, out float allowed) && now < allowed) return;

        if (!network)
        {
            network = CheckpointNetwork.Instance;
            if (!network) network = FindObjectOfType<CheckpointNetwork>(true);
        }
        if (!network) return;

        if (network.Count == 0)
        {
            network.Build();
            if (network.Count == 0) return;
        }

        if (!dice) return;

        // Origin position (where we entered the trigger)
        Vector3 originPos = rb.position;

        int anchorIndex = network.GetNearestIndex(transform.position);

        int delta = dice.IsForward ? dice.DiceValue : -dice.DiceValue;
        int targetIndex = network.ClampOrWrapIndex(anchorIndex + delta);

        Vector3 tangent;
        Vector3 basePos = network.GetPositionBehindOnPath(targetIndex, behindDistanceOnPath, out tangent);

        Vector3 right = Vector3.Cross(Vector3.up, tangent).normalized;
        Vector3 destPos = basePos + Vector3.up * upOffset + right * rightOffset;

        Vector3 savedVel = rb.linearVelocity;
        Vector3 savedAngVel = rb.angularVelocity;

        // SFX BEFORE teleport (at origin)
        if (teleportSfxTiming == TeleportSfxTiming.BeforeTeleport)
            ScheduleTeleportSfx(rb, originPos, waitEndOfFrame: false);

        // TELEPORT
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

        // SFX DURING teleport (at destination immediately)
        if (teleportSfxTiming == TeleportSfxTiming.DuringTeleport)
            ScheduleTeleportSfx(rb, destPos, waitEndOfFrame: false);

        // SFX AFTER teleport (at destination after teleport finishes)
        if (teleportSfxTiming == TeleportSfxTiming.AfterTeleport)
            ScheduleTeleportSfx(rb, destPos, waitEndOfFrame: true);

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

    void ScheduleTeleportSfx(Rigidbody playerRb, Vector3 atPos, bool waitEndOfFrame)
    {
        if (!playTeleportSfx) return;
        if (!teleportSfx) return;

        if (waitEndOfFrame || teleportSfxDelay > 0f)
            StartCoroutine(PlayTeleportSfxCoroutine(playerRb, atPos, waitEndOfFrame, teleportSfxDelay));
        else
            PlayTeleportSfxNow(playerRb, atPos);
    }

    IEnumerator PlayTeleportSfxCoroutine(Rigidbody playerRb, Vector3 atPos, bool waitEndOfFrame, float delay)
    {
        if (waitEndOfFrame) yield return new WaitForEndOfFrame();
        if (delay > 0f) yield return new WaitForSeconds(delay);
        PlayTeleportSfxNow(playerRb, atPos);
    }

    void PlayTeleportSfxNow(Rigidbody playerRb, Vector3 atPos)
    {
        AudioSource src = teleportSfxSource;

        if (!src && playerRb) src = playerRb.GetComponent<AudioSource>();
        if (!src) src = GetComponent<AudioSource>();

        if (src)
        {
            src.playOnAwake = false;
            src.PlayOneShot(teleportSfx, teleportSfxVolume);
        }
        else
        {
            AudioSource.PlayClipAtPoint(teleportSfx, atPos, teleportSfxVolume);
        }
    }
}
