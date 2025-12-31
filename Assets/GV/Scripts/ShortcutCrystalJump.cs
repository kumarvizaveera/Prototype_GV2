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

    [Header("Auto-follow after teleport")]
    public bool autoPilotAfterTeleport = true;
    public float autoPilotSeconds = 3.0f;
    public bool autoPilotUseCurrentSpeed = true;
    public float autoPilotSpeed = 50f;
    public float autoPilotSpeedMultiplier = 1f;

    static readonly Dictionary<int, float> _nextAllowed = new Dictionary<int, float>();

    void Reset()
    {
        var c = GetComponent<Collider>();
        c.isTrigger = true;
    }

    void Awake() => ResolveRefs();
    void OnEnable() => ResolveRefs();

    void ResolveRefs()
    {
        if (!network)
        {
            network = CheckpointNetwork.Instance;
            if (!network) network = FindObjectOfType<CheckpointNetwork>(true);
        }

        if (!dice)
            dice = GetComponentInParent<DiceRingIndicator>(true);
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

        int anchorIndex = network.GetNearestIndex(transform.position);

        int delta = dice.IsForward ? dice.DiceValue : -dice.DiceValue;
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
}
