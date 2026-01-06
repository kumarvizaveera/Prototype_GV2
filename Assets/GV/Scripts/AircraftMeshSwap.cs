using UnityEngine;

/// <summary>
/// Mesh-only swap (does not touch movement/physics).
/// Press Alpha1 to show A, Alpha2 to show B.
/// Optional: you can also allow a toggle key to flip between them.
/// </summary>
[DisallowMultipleComponent]
public class AircraftMeshSwapSimple : MonoBehaviour
{
    [Header("Visual Roots (children that contain only meshes/VFX)")]
    public GameObject meshRootA;
    public GameObject meshRootB;

    [Header("Start State")]
    public bool startWithA = true;

    [Header("Direct Select Keys")]
    public bool useAlpha1Alpha2 = true;
    public KeyCode keyForA = KeyCode.Alpha1;
    public KeyCode keyForB = KeyCode.Alpha2;

    [Header("Optional Toggle Key")]
    public bool allowToggleKey = false;
    public KeyCode toggleKey = KeyCode.Tab;

    [SerializeField] private bool isA;

    void Awake()
    {
        isA = startWithA;
        Apply();
    }

    void Update()
    {
        if (useAlpha1Alpha2)
        {
            if (Input.GetKeyDown(keyForA)) SetA(true);
            if (Input.GetKeyDown(keyForB)) SetA(false);
        }

        if (allowToggleKey && Input.GetKeyDown(toggleKey))
        {
            SetA(!isA);
        }
    }

    public void SetA(bool active)
    {
        if (isA == active) return;
        isA = active;
        Apply();
    }

    private void Apply()
    {
        if (meshRootA == null || meshRootB == null)
        {
            Debug.LogError($"{nameof(AircraftMeshSwapSimple)} on '{name}': Assign meshRootA and meshRootB.");
            return;
        }

        meshRootA.SetActive(isA);
        meshRootB.SetActive(!isA);
    }

    public bool IsAActive => isA;
}
