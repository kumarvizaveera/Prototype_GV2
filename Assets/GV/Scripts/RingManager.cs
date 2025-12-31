using System.Collections.Generic;
using UnityEngine;

public class PortalNetwork : MonoBehaviour
{
    [Header("Optional: restrict portals to a parent")]
    public Transform portalsParent;

    [Header("Player detection")]
    public string playerTag = "Player";

    [Header("Teleport behavior")]
    public float cooldownSeconds = 0.8f;
    public float exitOffset = 6f;

    public bool keepVelocity = true;
    public bool alignRotationToExit = false;

    [Header("Debug")]
    public bool debugLogs = true;

    private readonly List<CrystalPortal> portals = new List<CrystalPortal>();
    private readonly Dictionary<int, float> nextAllowedTime = new Dictionary<int, float>();

    private void Awake()
    {
        RefreshPortalList();
        if (debugLogs) Debug.Log($"[PortalNetwork] Found {portals.Count} portals.", this);
    }

    [ContextMenu("Refresh Portal List")]
    public void RefreshPortalList()
    {
        portals.Clear();

        CrystalPortal[] found = portalsParent != null
            ? portalsParent.GetComponentsInChildren<CrystalPortal>(true)
            : FindObjectsOfType<CrystalPortal>(true);

        portals.AddRange(found);
    }

    public void TryTeleport(Collider hit, CrystalPortal from)
    {
        if (hit == null || from == null) return;

        // Get the rigidbody (important for compound colliders)
        Rigidbody rb = hit.attachedRigidbody;

        // Robust tag check:
        // - If the collider is on a child, the child may not be tagged.
        // - So check rb.gameObject and root as well.
        bool isPlayer =
            (rb != null && rb.CompareTag(playerTag)) ||
            hit.CompareTag(playerTag) ||
            hit.transform.root.CompareTag(playerTag);

        if (!isPlayer)
        {
            if (debugLogs) Debug.Log($"[PortalNetwork] Trigger entered by non-player: {hit.name}", hit);
            return;
        }

        if (rb == null)
        {
            if (debugLogs) Debug.LogWarning($"[PortalNetwork] Player trigger detected but no attached Rigidbody on {hit.name}.", hit);
            return;
        }

        if (portals.Count < 2)
        {
            if (debugLogs) Debug.LogWarning("[PortalNetwork] Need at least 2 portals to teleport.", this);
            return;
        }

        int id = rb.GetInstanceID();
        float now = Time.time;

        if (nextAllowedTime.TryGetValue(id, out float t) && now < t)
            return;

        CrystalPortal to = GetRandomDestination(from);
        if (to == null) return;

        nextAllowedTime[id] = now + cooldownSeconds;

        Vector3 oldVel = rb.linearVelocity;
        Vector3 oldAng = rb.angularVelocity;

        rb.position = to.ExitPosition(exitOffset);

        if (alignRotationToExit)
            rb.rotation = to.ExitRotation();

        if (keepVelocity)
        {
            rb.linearVelocity = oldVel;
            rb.angularVelocity = oldAng;
        }
        else
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        if (debugLogs) Debug.Log($"[PortalNetwork] Teleported {rb.name} from {from.name} -> {to.name}", this);
    }

    private CrystalPortal GetRandomDestination(CrystalPortal from)
    {
        // Attempt a few random picks excluding 'from'
        for (int i = 0; i < 25; i++)
        {
            var p = portals[Random.Range(0, portals.Count)];
            if (p != null && p != from) return p;
        }

        // Fallback
        foreach (var p in portals)
            if (p != null && p != from) return p;

        return null;
    }
}
