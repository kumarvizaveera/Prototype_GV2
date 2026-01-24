using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-1000)]
public class CheckpointNetwork : MonoBehaviour
{
    public static CheckpointNetwork Instance { get; private set; }

    [Header("Source (optional manual assign)")]
    public Transform checkpointsParent;

    public string autoFindParentName = "CheckPoints Parent";

    [Header("Indexing")]
    public bool oneBased = true;     // 1..N
    public bool wrapAround = false;  // wrap or clamp

    [Header("Auto Build")]
    public bool autoBuild = true;
    public int buildAfterFrames = 2;

    readonly List<Transform> _checkpoints = new List<Transform>();
    public int Count => _checkpoints.Count;

    void Awake()
    {
        if (Instance == null) Instance = this;
        TryAutoAssignParent();
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

    IEnumerator BuildAfterSpawn()
    {
        for (int i = 0; i < Mathf.Max(0, buildAfterFrames); i++)
            yield return null;

        TryAutoAssignParent();
        Build();
    }

    [ContextMenu("Build / Rebuild")]
    public void Build()
    {
        _checkpoints.Clear();
        if (!checkpointsParent) return;

        foreach (Transform child in checkpointsParent)
            _checkpoints.Add(child);

        _checkpoints.Sort((a, b) => a.GetSiblingIndex().CompareTo(b.GetSiblingIndex()));
    }

    int ToZeroBased(int index) => oneBased ? index - 1 : index;
    int ToIndex(int zeroBased) => oneBased ? zeroBased + 1 : zeroBased;

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
            float d = (worldPos - _checkpoints[i].position).sqrMagnitude;
            if (d < bestDist)
            {
                bestDist = d;
                best = i;
            }
        }

        return ToIndex(best);
    }

    /// <summary>Point backDistance meters BEFORE the checkpoint along the checkpoint-chain path.</summary>
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

            Vector3 dir = (currPos - prevPos).normalized; // forward
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

    // --- Swap Restriction Logic ---

    [Header("Swap Restrictions")]
    public List<int> aircraftSwapIndices = new List<int> { 18, 36, 54, 72, 90 };
    public List<int> characterSwapIndices = new List<int> { 36, 72 };
    
    [Tooltip("Radius around the checkpoint center where swapping is allowed.")]
    public float swapZoneRadius = 30f;
    
    [Tooltip("How long (in seconds) the swap remains active after reaching/leaving the checkpoint.")]
    public float swapDuration = 10f;
    
    [Tooltip("Auto-assigned to tag 'Player' if empty.")]
    public Transform playerTransform;

    [Tooltip("Current Checkpoint Index giving permission. -1 if none.")]
    [SerializeField] private int currentZoneIndex = -1;
    public int CurrentZoneIndex => currentZoneIndex;
    
    [SerializeField] private float swapTimer = 0f;

    public bool CanSwapAircraft => currentZoneIndex != -1 && aircraftSwapIndices.Contains(currentZoneIndex);
    public bool CanSwapCharacter => currentZoneIndex != -1 && characterSwapIndices.Contains(currentZoneIndex);

    private bool IsInAnyZone(List<int> indices)
    {
        return currentZoneIndex != -1 && indices.Contains(currentZoneIndex);
    }
    
    // --- UI Feedback ---
    [Header("UI Feedback")]
    public TMPro.TMP_Text swapStatusText;
    [TextArea] public string swapMessage = "CHECKPOINT APPROACHING\nSWAP MECHANISMS ACTIVE\nPRESS '1'/'2' TO SWAP";

    void Update()
    {
        if (playerTransform == null)
        {
            GameObject p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) playerTransform = p.transform;
        }

        if (playerTransform == null) return;

        int foundIndex = -1;
        float sqrRadius = swapZoneRadius * swapZoneRadius;
        Vector3 playerPos = playerTransform.position;

        // 1. Check if we are physically inside any valid zone
        foreach (int idx in aircraftSwapIndices)
        {
            if (CheckDistance(idx, playerPos, sqrRadius))
            {
                foundIndex = idx;
                break;
            }
        }
        
        if (foundIndex == -1) // If not found in aircraft list, check character list
        {
            foreach (int idx in characterSwapIndices)
            {
                if (CheckDistance(idx, playerPos, sqrRadius))
                {
                    foundIndex = idx;
                    break;
                }
            }
        }

        // 2. Timer Logic
        if (foundIndex != -1)
        {
            // Player is inside a valid zone: Refresh timer
            currentZoneIndex = foundIndex;
            swapTimer = swapDuration;
        }
        else
        {
            // Player is outside: Count down
            if (swapTimer > 0f)
            {
                swapTimer -= Time.deltaTime;
                if (swapTimer <= 0f)
                {
                    swapTimer = 0f;
                    currentZoneIndex = -1; // Time expired
                }
            }
            else
            {
                currentZoneIndex = -1;
            }
        }

        // 3. Update UI
        if (swapStatusText != null)
        {
            bool show = (currentZoneIndex != -1);
            if (show)
            {
                swapStatusText.text = swapMessage;
                if (!swapStatusText.gameObject.activeSelf) swapStatusText.gameObject.SetActive(true);
            }
            else
            {
                 if (swapStatusText.gameObject.activeSelf) swapStatusText.gameObject.SetActive(false);
            }
        }
    }

    private bool CheckDistance(int index, Vector3 playerPos, float sqrRadius)
    {
        Transform cp = GetCheckpoint(index);
        if (cp == null) return false;

        if ((cp.position - playerPos).sqrMagnitude <= sqrRadius)
            return true;
            
        return false;
    }
}
