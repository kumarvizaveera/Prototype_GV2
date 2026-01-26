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

    [Header("Calibration")]
    [Tooltip("Add/Subtract to shift all indices. E.g. -1 if triggers are 1 too late.")]
    public int indexOffset = 0;

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

    int ToZeroBased(int index) => (oneBased ? index - 1 : index) + indexOffset;
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
            // Handle destroyed/missing checkpoints gracefully
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

    [Header("Common Settings")]
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

    public bool CanSwapAircraft => aircraftZoneIndex != -1;
    public bool CanSwapCharacter => characterZoneIndex != -1;

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
                // Priority 1: Counting Down (Just passed checkpoint, window open)
                // We check if inactive in physics zone (timer decreasing)
                // epsilon check for float safe comparison
                if (CanSwapAircraft && aircraftTimer < (swapDuration - 0.01f))
                {
                     aircraftStatusText.text = string.Format(aircraftActiveMessage, aircraftTimer);
                }
                // Priority 2: In Proximity (Approaching OR Inside safe zone)
                else if (aircraftUiVisible)
                {
                     aircraftStatusText.text = aircraftMessage;
                }
                // Fallback (Should be covered by P1, but safe to keep)
                else if (CanSwapAircraft)
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
                // Priority 1: Counting Down
                if (CanSwapCharacter && characterTimer < (swapDuration - 0.01f))
                {
                    characterStatusText.text = string.Format(characterActiveMessage, characterTimer);
                }
                // Priority 2: Proximity
                else if (charUiVisible)
                {
                    characterStatusText.text = characterMessage;
                }
                // Fallback
                else if (CanSwapCharacter)
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
}
