using UnityEngine;
using System.Collections.Generic;
using VSX.Engines3D;
using VSX.ResourceSystem;
using Fusion;

public class BoostGlow : MonoBehaviour
{
    [Header("Boost Settings (Tab)")]
    public float boostIntensity = 6.0f;
    public Color boostColor = Color.magenta;
    public KeyCode boostKey = KeyCode.W; // Updated default to W to match new controls

    [Header("Forward Settings (Auto / W)")]
    public bool autoForwardGlow = true; // New: Default to ON
    public float forwardIntensity = 2.0f;
    public Color forwardColor = Color.cyan;
    public KeyCode forwardKey = KeyCode.W; // Kept for reference, though W is now boost
    public KeyCode brakeKey = KeyCode.S;   // New: Brake key to turn off glow

    [Header("Side Settings (Q & E)")]
    public float sideIntensity = 3.0f;     // Intensity for Q and E
    public Color leftColor = Color.yellow; // Color for Q
    public KeyCode leftKey = KeyCode.Q;
    public Color rightColor = Color.green; // Color for E
    public KeyCode rightKey = KeyCode.E;

    [Header("General")]
    public float transitionSpeed = 5.0f;

    [Header("References")]
    public GameObject[] targetModelParents;
    public VehicleEngines3D vehicleEngines; // Made public to verify assignment

    private List<Renderer> _allRenderers = new List<Renderer>();
    private float _currentIntensity;
    private Color _currentColor;

    // Network awareness — only read keyboard for local player's ship
    private NetworkObject _networkObject;
    private bool _isLocalPlayer;

    public Color CurrentBaseColor => _currentColor;
    public float CurrentIntensity => _currentIntensity;

    void Start()
    {
        // Set default starting color
        _currentColor = forwardColor;

        if (vehicleEngines == null)
            vehicleEngines = GetComponent<VehicleEngines3D>();

        if (vehicleEngines == null)
            vehicleEngines = GetComponentInParent<VehicleEngines3D>();

        // Find NetworkObject to determine local vs remote ship
        _networkObject = transform.root.GetComponent<NetworkObject>();
        if (_networkObject == null)
            _networkObject = GetComponentInParent<NetworkObject>();

        // Find all renderers in all assigned parents
        foreach (GameObject parentObj in targetModelParents)
        {
            if (parentObj != null)
            {
                Renderer[] foundRenderers = parentObj.GetComponentsInChildren<Renderer>();
                _allRenderers.AddRange(foundRenderers);
            }
        }

        // Enable emission for everything and log diagnostics
        foreach (Renderer r in _allRenderers)
        {
            if (r != null && r.material != null)
            {
                r.material.EnableKeyword("_EMISSION");
                
                // DIAGNOSTIC LOG FOR MATERIALS
                bool hasEmissionColor = r.material.HasProperty("_EmissionColor");
                bool hasEmission = r.material.HasProperty("_Emission");
                bool hasBaseColor = r.material.HasProperty("_BaseColor");
                
                Debug.Log($"[BoostGlow] Renderer: {r.name} | Shader: {r.material.shader.name} | " +
                          $"Has _EmissionColor: {hasEmissionColor} | Has _Emission: {hasEmission} | Has _BaseColor: {hasBaseColor}");
            }
        }
    }

    void Update()
    {
        // Re-determine local player in case of delayed spawn
        _isLocalPlayer = (_networkObject == null) || _networkObject.HasInputAuthority;

        float targetIntensity = 0f;
        Color targetBaseColor = _currentColor;

        Vector3 boostState = Vector3.zero;
        Vector3 moveState = Vector3.zero;
        float rollState = 0f;
        bool canBoost = true;

        if (_isLocalPlayer)
        {
            // Foolproof local check bypassing the old InputSystem that breaks in build, 
            // and bypassing SCK's physics consumption step which zeroes inputs!
            var nm = GV.Network.NetworkManager.Instance;
            if (nm != null)
            {
                var netInput = nm.GetComponent<GV.Network.NetworkedPlayerInput>();
                if (netInput != null)
                {
                    var data = netInput.CurrentInputData;
                    moveState = new Vector3(data.moveX, data.moveY, data.moveZ);
                    rollState = data.steerRoll; // Q = 1, E = -1
                    if (data.boost) boostState = new Vector3(0, 0, 1f);
                }
            }
        }
        else if (vehicleEngines != null)
        {
            // REMOTE PLAYER: physically-simulated synced inputs on proxy ships.
            boostState = vehicleEngines.BoostInputs;
            moveState = vehicleEngines.MovementInputs;
            rollState = vehicleEngines.SteeringInputs.z;
        }

        if (vehicleEngines != null)
        {
            // Check if boost is permitted (resources available)
            foreach (var handler in vehicleEngines.BoostResourceHandlers)
            {
                if (!handler.Ready())
                {
                    canBoost = false;
                    break;
                }
            }
        }

        // 1. Boost active
        if (boostState.magnitude > 0.1f && canBoost)
        {
            targetIntensity = boostIntensity;
            targetBaseColor = boostColor;
        }
        // 2. Reverse / brake (negative Z movement)
        else if (moveState.z < -0.1f)
        {
            targetIntensity = 0f;
        }
        // 3. Left strafe / Q (Roll positive or move negative X)
        else if (rollState > 0.1f || moveState.x < -0.1f)
        {
            targetIntensity = sideIntensity;
            targetBaseColor = leftColor;
        }
        // 4. Right strafe / E (Roll negative or move positive X)
        else if (rollState < -0.1f || moveState.x > 0.1f)
        {
            targetIntensity = sideIntensity;
            targetBaseColor = rightColor;
        }
        // 5. Forward movement / Auto-forward
        else if (moveState.z > 0.1f || autoForwardGlow)
        {
            targetIntensity = forwardIntensity;
            targetBaseColor = forwardColor;
        }
        // 6. Idle
        else
        {
            targetIntensity = 0f;
        }

        // --- DIAGNOSTICS ---
        if (Time.frameCount % 60 == 0) // Log approx once per second
        {
            Debug.Log($"[BoostGlow] Local: {_isLocalPlayer} | Move: {moveState} | Boost: {boostState} | Final Intensity: {targetIntensity}");
            if (_allRenderers.Count == 0) Debug.LogWarning("[BoostGlow] No renderers found to apply glow to!");
        }

        // SMOOTHING
        _currentIntensity = Mathf.Lerp(_currentIntensity, targetIntensity, Time.deltaTime * transitionSpeed);
        _currentColor = Color.Lerp(_currentColor, targetBaseColor, Time.deltaTime * transitionSpeed);

        // APPLY
        Color finalColor = _currentColor * _currentIntensity;

        foreach (Renderer r in _allRenderers)
        {
            if (r != null && r.material != null)
            {
                r.material.EnableKeyword("_EMISSION");
                
                // Primary Application (Works in Editor, stripped unless preserved in Build)
                r.material.SetColor("_EmissionColor", finalColor);
                
                // URP/Build Specific: Force Global Illumination to recognize the dynamic emission
                r.material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
        }
    }
}