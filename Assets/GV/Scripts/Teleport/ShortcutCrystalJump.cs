using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using GV;
using Fusion;

[RequireComponent(typeof(Collider))]
public class ShortcutCrystalJump : NetworkBehaviour
{
    [Header("Configuration")]
    public bool manualTriggerOnly = false;
    
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

    static readonly Dictionary<NetworkId, float> _nextAllowed = new Dictionary<NetworkId, float>();
    float _ignoreEnterUntilTime = 0f;

    void Reset()
    {
        var c = GetComponent<Collider>();
        c.isTrigger = true;
    }

    void Awake() => ResolveRefs();

    // Use Spawned instead of OnEnable for Network logic initialization if needed, 
    // but OnEnable is fine for local refs.
    public override void Spawned()
    {
        if (ignoreEnterRightAfterEnable)
            _ignoreEnterUntilTime = Time.time + ignoreEnterSeconds;
    }

    void OnEnable()
    {
        ResolveRefs();
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

    void OnTriggerEnter(Collider other)
    {
        if (!Object) 
        {
            Debug.LogWarning("[ShortcutCrystalJump] OnTriggerEnter: No NetworkObject on this script!");
            return;
        }
        
        // PREDICTIVE LOGIC: Run on State Authority (Server) AND Input Authority (Client)
        // Proxies do nothing; they wait for valid state serialization from server.
        
        if (manualTriggerOnly) return;
        if (ignoreEnterRightAfterEnable && Time.time < _ignoreEnterUntilTime) return;

        Rigidbody rb = other.attachedRigidbody;

        if (!IsPlayer(other, rb)) return;
        if (!rb) return;

        var no = rb.GetComponent<NetworkObject>();
        if (!no) no = rb.GetComponentInParent<NetworkObject>();
        if (!no) 
        {
             Debug.LogWarning($"[ShortcutCrystalJump] Player {rb.name} has no NetworkObject!");
             return; 
        }

        // Only run if we control this player (Host or Client Owner)
        if (!no.HasStateAuthority && !no.HasInputAuthority) return;

        Debug.Log($"[ShortcutCrystalJump] Triggered by Player {no.name} (ID: {no.Id}) [Auth: {no.HasStateAuthority}/{no.HasInputAuthority}]. Executing Teleport.");
        ExecuteTeleport(no, rb);
    }

    // Shared predictive logic
    void ExecuteTeleport(NetworkObject targetPlayer, Rigidbody rb)
    {
        // Rate Limit (Local check is fine for prediction)
        float now = Runner.SimulationTime;
        if (_nextAllowed.TryGetValue(targetPlayer.Id, out float allowed) && now < allowed) 
        {
            Debug.Log($"[ShortcutCrystalJump] Rate limit active for {targetPlayer.Id}. Wait {allowed - now:F2}s");
            return;
        }
        _nextAllowed[targetPlayer.Id] = now + cooldownSeconds;

        if (!network)
        {
            network = CheckpointNetwork.Instance;
            if (!network) network = FindObjectOfType<CheckpointNetwork>(true);
        }
        
        if (!network || network.Count == 0)
        {
             if (network) network.Build();
             if (!network || network.Count == 0) 
             {
                 Debug.LogError("[ShortcutCrystalJump] No CheckpointNetwork found or built!");
                 return;
             }
        }

        if (!dice) dice = GetComponentInParent<DiceRingIndicator>(true);
        
        // Calculate destination
        int anchorIndex = network.GetNearestIndex(transform.position);
        int delta = 1;

        if (dice)
        {
            delta = dice.CurrentIsForward ? dice.CurrentDiceValue : -dice.CurrentDiceValue;
            Debug.Log($"[ShortcutCrystalJump] Using Dice: Forward={dice.CurrentIsForward}, Value={dice.CurrentDiceValue}, Delta={delta}");
        }
        else
        {
             Debug.LogWarning("[ShortcutCrystalJump] No DiceRingIndicator found! Defaulting to +1.");
        }

        int targetIndex = network.ClampOrWrapIndex(anchorIndex + delta);

        // SIMPLIFIED LOGIC: Direct High-Level Teleport (Bypassing Path Walking)
        // We just go to the checkpoint position + local offsets.
        Transform targetT = network.GetCheckpoint(targetIndex);
        if (!targetT)
        {
             Debug.LogError($"[ShortcutCrystalJump] Target Checkpoint Index {targetIndex} is null!");
             return;
        }

        Vector3 basePos = targetT.position;
        // Assume forward is the checkpoint's forward, or calculate from previous
        Vector3 tangent = targetT.forward; 
        
        // Optional: specific tangent from track if available
        // But for now, trust the checkpoint's rotation
        
        Vector3 right = targetT.right;
        Vector3 up = targetT.up;

        Vector3 destPos = basePos + up * upOffset + right * rightOffset - tangent * behindDistanceOnPath;

        Quaternion destRot = targetT.rotation; // Match checkpoint rotation

        Debug.Log($"[ShortcutCrystalJump] Teleporting NetId {targetPlayer.Id} to {targetT.name} (Index {targetIndex}). Pos: {destPos}");
        
        // PERFOM TELEPORT
        if (targetPlayer.TryGetComponent<NetworkTransform>(out var nt))
        {
            nt.Teleport(destPos, destRot);
            
            if (rb)
            {
                if (!keepVelocity)
                {
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
                else
                {
                    // Redirect velocity to match new forward direction so we don't fly sideways
                    float speed = rb.linearVelocity.magnitude;
                    if (speed > 1f)
                    {
                        rb.linearVelocity = destRot * Vector3.forward * speed;
                        // Also kill angular velocity to stop spinning
                        rb.angularVelocity = Vector3.zero;
                    }
                }
            }
        }
        else
        {
            // Fallback
            rb.position = destPos;
            rb.rotation = destRot;
            rb.transform.position = destPos;
            rb.transform.rotation = destRot;
            
            if (!keepVelocity)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            else
            {
                // Redirect velocity to match new forward direction so we don't fly sideways
                float speed = rb.linearVelocity.magnitude;
                if (speed > 1f)
                {
                    rb.linearVelocity = destRot * Vector3.forward * speed;
                    rb.angularVelocity = Vector3.zero;
                }
            }
        }

        // SFX
        TryPlayTeleportSfx(rb, destPos);

        // AutoPilot
        if (autoPilotAfterTeleport)
        {
            var ap = rb.GetComponent<PathAutoPilot>();
            if (!ap) ap = rb.gameObject.AddComponent<PathAutoPilot>();

            float speedToUse = autoPilotSpeed;
            if (autoPilotUseCurrentSpeed)
            {
                float v = rb.linearVelocity.magnitude;
                speedToUse = Mathf.Max(5f, v * autoPilotSpeedMultiplier);
            }

            int nextIndex = network.GetNextIndex(targetIndex);

            Debug.Log($"[ShortcutCrystalJump] Engaging AutoPilot towards Next Index: {nextIndex} (Current: {targetIndex})");
            ap.Begin(nextIndex, autoPilotSeconds, speedToUse);
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
            src.playOnAwake = false;
            src.PlayOneShot(teleportSfx, teleportSfxVolume);
        }
        else
        {
            AudioSource.PlayClipAtPoint(teleportSfx, atPos, teleportSfxVolume);
        }
    }
}
