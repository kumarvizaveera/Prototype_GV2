using UnityEngine;

[DisallowMultipleComponent]
public class CrystalPortal : MonoBehaviour
{
    [Header("Optional: where the aircraft should appear")]
    public Transform exitPoint;

    private PortalNetwork network;

    private void Awake()
    {
        // Works in older Unity versions (no FindFirstObjectByType dependency)
        network = FindObjectOfType<PortalNetwork>(true);

        if (exitPoint == null) exitPoint = transform;

        // Safety: ensure there is a trigger collider on THIS object
        var col = GetComponent<Collider>();
        if (col == null)
            Debug.LogError($"[CrystalPortal] No Collider found on {name}. Add a Collider and enable IsTrigger.", this);
        else if (!col.isTrigger)
            Debug.LogError($"[CrystalPortal] Collider on {name} is not Trigger. Enable IsTrigger.", this);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (network == null)
        {
            Debug.LogError("[CrystalPortal] PortalNetwork not found in scene.", this);
            return;
        }

        network.TryTeleport(other, this);
    }

    public Vector3 ExitPosition(float offset)
    {
        var p = exitPoint != null ? exitPoint : transform;
        return p.position + p.forward * offset;
    }

    public Quaternion ExitRotation()
    {
        var p = exitPoint != null ? exitPoint : transform;
        return p.rotation;
    }
}
